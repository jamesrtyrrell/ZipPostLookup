# How the AI Import Helper Works (Architecture Deep-Dive)

## 🧠 The Three Intelligence Layers

The AI Import Helper uses a **hybrid intelligence architecture** with three complementary systems:

### 1. **Oracle Layer** (Deterministic, Data-Driven)
**What it does:** Identifies the postal code column by brute-force lookup

```
For each column in file:
    For each cell value:
        Try ZpImageLookup.US.GetByCode(value)
        Try ZpImageLookup.CA.GetByCode(value)
        Try ZpImageLookup.MX.GetByCode(value)
        // Now dynamic: tries ALL available countries!
        
    Calculate hit rate = successful lookups / total cells
    
Winner = column with highest hit rate (≥70% threshold)
```

**How it "learns":**
- ✅ **Data-driven:** Add more countries → automatically detects them
- ✅ **Self-improving:** The oracle-miss feedback loop:
  ```
  Import file → Oracle misses codes → Generate .oracle-misses.txt
  → User enriches those codes → Export updated .zpi.br
  → Next import: Oracle now hits those codes! (90% → 95% hit rate)
  ```
- 🔧 **Tunable:** `--min-hit-rate` threshold (default 0.70)

**Example improvement path:**
```bash
# First import: Oracle hits 70% (CA postal codes incomplete)
ingest auto canada_data.csv  # Generates .oracle-misses.txt with 1,500 codes

# User enriches the missing codes
enrich direct --country CA  # Add those 1,500 codes to reference DB
export --country CA --target zpimage  # Regenerate CA .zpi.br

# Second import: Oracle now hits 95%! (learned 1,500 new codes)
ingest auto canada_data2.csv  # Higher confidence, faster
```

---

### 2. **Correlation Layer** (Heuristic, Algorithmic)
**What it does:** Maps remaining columns to fields using similarity metrics

```csharp
// Phase 3: For each oracle hit, compare cell values to oracle entry fields
foreach (var hit in oracleHits) {
    foreach (var column in otherColumns) {
        var cellValue = row[column];
        
        // String similarity (Levenshtein distance)
        var placeNameScore = LevenshteinSimilarity(cellValue, hit.Entry.PlaceName);
        var admin1Score = LevenshteinSimilarity(cellValue, hit.Entry.Admin1);
        
        // Type inference
        if (IsFloat(cellValue) && InRange(-90, 90)) latitudeScore++;
        if (cellValue.Contains("/")) timezoneScore++;  // IANA format hint
    }
    
    // Aggregate: confidence = (match_count / total_hits) × avg_similarity
}

// Greedy assignment: highest confidence unmapped pairing first
```

**How it "learns":**
- 🔧 **Threshold tuning:** Hard-coded confidence thresholds (60% PlaceName, 30% others)
  ```csharp
  // Current in ColumnCorrelationService.cs:528
  var hasPlaceName = mappings.Any(m => m.FieldName == "PlaceName" && m.Confidence >= 0.6);
  ```
  **Improvement:** Track success/failure rate, adjust thresholds dynamically
  
- 📊 **Coverage × Similarity formula:**
  ```csharp
  confidence = (matches / totalHits) × avgSimilarity
  ```
  **Improvement:** Add weighted scoring (exact match = 1.5×, partial = 1.0×)

- 🎯 **Type inference rules:**
  ```csharp
  // Current logic
  if (numValue >= -90 && numValue <= 90) → Latitude candidate
  if (cellValue.Contains("/")) → Timezone candidate
  ```
  **Improvement:** Add more patterns (e.g., Admin codes = 2-3 chars, all caps)

**Not "trainable" in ML sense** — these are hand-crafted heuristics, but can be **tuned via code changes**.

---

### 3. **LLM Layer** (AI, Prompt-Engineered)
**What it does:** Resolves ambiguities the oracle/correlation can't decide

```
When to trigger:
- Two columns within 10% hit rate (which is postal code?)
- PlaceName confidence <60% but multiple >40% (which is city name?)
- Competing lat/lng candidates (columns 6 vs 9)

Prompt structure:
1. File structure (format, columns, headers)
2. Oracle results (hit rates, country tally)
3. Proposed mappings with confidence scores
4. Sample rows (first 5)
5. Ambiguity reasons

LLM returns: Refined JSON mapping with updated confidence + reasoning
```

**How it "learns":**

#### **A. Prompt Engineering** (Immediate improvement)
Current prompt is generic. You can improve by:

```csharp
// CURRENT (generic)
sb.AppendLine("You are a data format expert helping import postal code data.");

// IMPROVED (domain-specific examples)
sb.AppendLine("""
You are a data format expert specializing in GeoNames and postal code datasets.

Common patterns to recognize:
- GeoNames 12-column TSV: [country, zip, place, admin1, admin1code, admin2, admin2code, admin3, admin3code, lat, lng, accuracy]
  → If you see column 6/7/8 = "13", "", "" (admin2code, admin3, admin3code), columns 9/10 are LAT/LNG, not 6/9
- Canadian FSA: 3-char format (e.g., "T0A") → high confidence for CA country
- US ZIP+4: Format "12345-6789" → high confidence for US
""");
```

**Location:** `ZipPostLookup.CountryDataTools/Ingestion/AutoImport/DisambiguationService.cs:62` (BuildPrompt method)

#### **B. Few-Shot Learning** (Add examples to prompt)
```csharp
// Add to prompt:
sb.AppendLine("""
**Example 1:** GeoNames TSV with 12 columns, columns 9/11 both numeric
- Column 9 = latitude (values 40-50 range, typical lat)
- Column 10 = longitude (values -120 to -80 range, typical lng)
- Column 11 = accuracy (values 1-6, small integers)
→ Choose 9/10, not 9/11

**Example 2:** Ambiguous admin columns
- Column 3 = "Alberta" (full name)
- Column 4 = "AB" (2-char code)
→ Column 3 = Admin1, Column 4 = Admin1Code
""");
```

#### **C. Chain-of-Thought Prompting**
```csharp
// Current: "Return a JSON object"
// Improved: "Think step-by-step, then return JSON"
sb.AppendLine("""
Think through this step-by-step:
1. Look at the sample rows - what patterns do you see?
2. Check column 9 values: are they latitude-range (-90 to 90) or something else?
3. Check column 10 values: are they longitude-range (-180 to 180)?
4. What is column 11? (small integers = accuracy, floats = coordinate)
5. Based on this analysis, which columns are lat/lng?

Now return your final JSON mapping.
""");
```

#### **D. Model Tuning** (Advanced)
**Current:** Uses Claude Sonnet 4.5 (fixed model, API)
```csharp
// DisambiguationService.cs:15
private const string DefaultModel = "claude-sonnet-4-5-20250929";
```

**Improvement options:**

1. **Model selection by file type:**
   ```csharp
   var model = sniff.AmbiguityReasons.Contains("column count varies")
       ? "claude-opus-4-8"  // Complex case → use Opus
       : "claude-sonnet-4-5";  // Normal case → use Sonnet
   ```

2. **Fine-tuning** (if you had labeled data):
   - Not currently supported via Anthropic API for Claude
   - Alternative: Collect 100+ examples of (file → correct mapping)
   - Use for few-shot prompting or switch to fine-tunable model (GPT-4)

3. **Feedback loop via corrections:**
   ```csharp
   // When user corrects mapping in Phase 5 UI:
   if (user.OverrideLLM) {
       LogCorrection(llmProposal, userMapping);  // Save to correction log
       // Future: Add top-3 corrections to prompt as "lessons learned"
   }
   ```

---

## 🎓 Training / Improvement Strategies

### **Strategy 1: Oracle Expansion** (Easiest, Highest ROI)
**Current:** 3 countries (US/CA/MX)  
**Target:** 50+ countries

**How:**
1. Export data for new country: `export --country FR --target zpimage`
2. Embed `fr.u16` in library csproj
3. Rebuild → `CountryRegistry` auto-discovers FR
4. Next import: Oracle automatically tries FR lookups!

**Improvement per country added:**
- +1 country = +10-30% hit rate for files from that country
- Oracle-miss feedback improves coverage over time

---

### **Strategy 2: Correlation Threshold Tuning**
**Current:** Fixed thresholds (60% PlaceName, 30% others, 10% ambiguity gap)

**How to tune:**
1. **Collect success metrics:**
   ```csharp
   // Add to IngestionService.cs
   if (result.Discrepancies > result.Inserted * 0.5) {
       // High discrepancy rate → correlation was probably wrong
       LogCorrelationFailure(mapping, filePath);
   }
   ```

2. **A/B test thresholds:**
   ```csharp
   // In ColumnCorrelationService.cs, make configurable:
   public class CorrelationConfig {
       public double PlaceNameMinConfidence { get; set; } = 0.6;
       public double AmbiguityThreshold { get; set; } = 0.15;
       public bool UseWeightedScoring { get; set; } = false;
   }
   
   // Test different configs, measure success rate
   ```

3. **Per-country tuning:**
   ```csharp
   var config = country switch {
       "US" => new CorrelationConfig { PlaceNameMinConfidence = 0.7 },  // High quality
       "JP" => new CorrelationConfig { PlaceNameMinConfidence = 0.5 },  // Different names
       _ => CorrelationConfig.Default
   };
   ```

---

### **Strategy 3: LLM Prompt Refinement** (Medium effort, high impact)

**A. Collect failure cases:**
```bash
# When LLM gets it wrong, save the case:
# .claude/llm-corrections.jsonl
{"file": "jp-sample.csv", "llm_mapping": {...}, "correct_mapping": {...}, "lesson": "Column 11 was accuracy, not longitude"}
```

**B. Build prompt library:**
```csharp
// Add country-specific hints
var countryHints = country switch {
    "US" => "US ZIPs are 5 digits, ZIP+4 is 9. Column with 5-digit numbers = postal code.",
    "CA" => "Canadian FSAs are 3-char (letter-digit-letter). E.g., 'T0A', 'M5H'.",
    "MX" => "Mexican postal codes are 5 digits. Estados are derived from first 2 digits.",
    _ => ""
};
sb.AppendLine($"**Country-specific notes:** {countryHints}");
```

**C. Add format-specific templates:**
```csharp
// Detect GeoNames format (12 columns, no header, column 0 = country code)
if (sniff.ColumnCount == 12 && !sniff.HasHeaderRow && probe.ColumnHitRates[1] > 0.8) {
    sb.AppendLine("""
    **DETECTED: GeoNames 12-column TSV format**
    Standard column layout:
    - 0: country code
    - 1: postal code
    - 2: place name
    - 3: admin1 name
    - 4: admin1 code
    - 5-8: admin2-3 (often empty)
    - 9: latitude
    - 10: longitude
    - 11: accuracy (1-6 integer)
    
    Use this knowledge to resolve ambiguities.
    """);
}
```

---

### **Strategy 4: Active Learning Loop** (Advanced)

**Concept:** Learn from user corrections in the UI

```csharp
// Phase 5: MappingConfirmationService
public MappingProposal ShowConfirmationUI(...) {
    var userMapping = widget.Show(...);
    
    // If user edited the mapping, log the correction
    if (!userMapping.Equals(proposal)) {
        var correction = new CorrectionRecord {
            FileStructure = sniff,
            ProbeResults = probe,
            LlmProposal = proposal,
            UserCorrection = userMapping,
            Timestamp = DateTime.UtcNow
        };
        
        await SaveCorrectionAsync(correction);  // → .claude/corrections.jsonl
    }
    
    return userMapping;
}
```

**Then use corrections in future runs:**
```csharp
// In DisambiguationService, load recent corrections
var recentCorrections = LoadRecentCorrections(limit: 5);
if (recentCorrections.Any()) {
    sb.AppendLine("**Recent corrections from users:**");
    foreach (var correction in recentCorrections) {
        sb.AppendLine($"- File with {correction.FileStructure.ColumnCount} columns: " +
                      $"Column {correction.LlmProposal.GetMapping("Latitude")} was initially chosen for Latitude, " +
                      $"but user corrected it to {correction.UserCorrection.GetMapping("Latitude")}. " +
                      $"Reason: {correction.Notes}");
    }
}
```

**Result:** The system learns from its mistakes over time!

---

### **Strategy 5: Ensemble Voting** (Advanced, expensive)

Instead of 1 LLM call, use multiple perspectives:

```csharp
public async Task<MappingProposal> DisambiguateAsync(DisambiguationRequest request) {
    // Call LLM 3 times with different perspectives
    var tasks = new[] {
        CallClaudeWithPrompt(BuildPrompt(request, perspective: "data-scientist")),
        CallClaudeWithPrompt(BuildPrompt(request, perspective: "gis-specialist")),
        CallClaudeWithPrompt(BuildPrompt(request, perspective: "database-admin"))
    };
    
    var results = await Task.WhenAll(tasks);
    
    // Majority vote on each field
    var finalMapping = VoteOnMappings(results);
    return finalMapping;
}

string BuildPrompt(..., string perspective) {
    var rolePrompt = perspective switch {
        "data-scientist" => "You are a data scientist specializing in data cleaning and ETL...",
        "gis-specialist" => "You are a GIS specialist who works with coordinate data daily...",
        "database-admin" => "You are a database administrator importing postal code tables..."
    };
    // ...
}
```

**Trade-off:** 3× cost, but higher accuracy on hard cases.

---

## 📊 Metrics to Track Improvement

Add instrumentation to measure success:

```csharp
public class AutoImportMetrics {
    public double OracleHitRate { get; set; }
    public double CorrelationConfidence { get; set; }
    public bool LlmWasNeeded { get; set; }
    public bool UserEditedMapping { get; set; }
    public double DiscrepancyRate { get; set; }  // discrepancies / inserted
    public TimeSpan TotalDuration { get; set; }
}

// After Phase 7:
var metrics = new AutoImportMetrics {
    OracleHitRate = probe.ColumnHitRates[probe.PostalCodeColumnIndex],
    CorrelationConfidence = proposal.Mappings.Average(m => m.Confidence),
    LlmWasNeeded = proposal.RequireDisambiguation,
    UserEditedMapping = userMadeChanges,  // from Phase 5
    DiscrepancyRate = result.Discrepancies / (double)result.Inserted,
    TotalDuration = stopwatch.Elapsed
};

await SaveMetricsAsync(metrics, filePath);  // → .claude/metrics.jsonl
```

**Then analyze:**
```bash
# Find cases where LLM was wrong (user edited + high discrepancy rate)
jq 'select(.UserEditedMapping == true and .DiscrepancyRate > 0.3)' metrics.jsonl

# Average oracle hit rate by country
jq -s 'group_by(.Country) | map({country: .[0].Country, avgHitRate: (map(.OracleHitRate) | add / length)})' metrics.jsonl
```

---

## 🎯 Summary: How to Improve

| Layer | Method | Effort | Impact | Ongoing? |
|-------|--------|--------|--------|----------|
| **Oracle** | Add more countries | Low | ⭐⭐⭐ High | Yes (cumulative) |
| **Oracle** | Oracle-miss feedback loop | Low | ⭐⭐ Medium | Yes (automatic) |
| **Correlation** | Tune thresholds | Low | ⭐⭐ Medium | One-time |
| **Correlation** | Add type inference rules | Medium | ⭐⭐ Medium | One-time |
| **LLM** | Improve prompts (few-shot, COT) | Low | ⭐⭐⭐ High | Iterative |
| **LLM** | Format-specific templates | Medium | ⭐⭐⭐ High | Per-format |
| **LLM** | User correction feedback | High | ⭐⭐⭐⭐ Very High | Yes (continuous) |
| **LLM** | Ensemble voting | High | ⭐⭐ Medium | One-time |
| **All** | Add metrics/instrumentation | Medium | ⭐⭐⭐⭐ Very High | Yes (enables others) |

---

## 🚀 Quick Wins (Next 1 Hour)

1. **Add 5 more countries to oracle** → +50% coverage
   ```bash
   # Export FR, DE, GB, JP, AU
   export --country FR --target zpimage
   # Embed in csproj, rebuild
   ```

2. **Improve LLM prompt with GeoNames format hint** → +20% accuracy
   ```csharp
   // In DisambiguationService.BuildPrompt(), add GeoNames template detection
   ```

---

## 🎯 Long-Term Vision (Next Month)

1. **Implement user correction logging** → continuous improvement
   - Save corrections to `.claude/corrections.jsonl`
   - Load top-5 corrections into LLM prompt
   - Measure: correction rate should drop from 30% → 10%

2. **Add metrics dashboard** → data-driven tuning
   - Instrument all phases
   - Generate weekly success rate reports
   - A/B test threshold changes

3. **Oracle expansion to 50 countries** → universal coverage
   - Process all 121 samples in `samples/` directory
   - Auto-detect any country with embedded data
   - Hit rate goal: 80%+ for any supported country

---

## 💡 Key Insight

**The AI Import Helper is already intelligent and self-improving** via the oracle feedback loop. The LLM layer can be iteratively improved via prompt engineering without model retraining. It's a hybrid system that combines:

- **Deterministic lookup** (fast, accurate when data exists)
- **Heuristic correlation** (pattern matching, tunable)
- **AI reasoning** (handles edge cases, learns from examples)

This architecture is **more practical than pure ML** because:
- ✅ No training data required to start
- ✅ Improves automatically as data grows
- ✅ Explainable (you can see why it chose column X)
- ✅ Iteratively refinable (prompt engineering > retraining)
- ✅ Fails gracefully (ambiguity → ask LLM → ask user → always get answer)

The system gets smarter every time you:
1. Add a country
2. Enrich missing postal codes
3. Improve the prompts
4. Log user corrections

**No machine learning infrastructure required!** 🎉
