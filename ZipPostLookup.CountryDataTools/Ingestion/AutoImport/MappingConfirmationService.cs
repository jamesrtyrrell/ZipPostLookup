using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Ingestion.Models;
using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Phase 5: Interactive mapping confirmation UI (Milestone 2).
/// Wraps ColumnMappingWidget with auto-import-specific features.
/// </summary>
public class MappingConfirmationService
{
    /// <summary>
    /// Show interactive confirmation UI for proposed mapping.
    /// </summary>
    public MappingProposal ShowConfirmationUI(
        FileSniffResult sniff,
        ProbeResult probe,
        MappingProposal proposal,
        string[][] sampleRows,
        ICountryRules rules)
    {
        // 1. Build ColumnMapping from proposal (pre-bind fields with confidence)
        var mapping = ColumnMapping.ForIngestion();

        foreach (var fieldMapping in proposal.Mappings)
        {
            var templateField = mapping.Fields.FirstOrDefault(f =>
                string.Equals(f.Name, fieldMapping.FieldName, StringComparison.OrdinalIgnoreCase));

            if (templateField != null && fieldMapping.ColumnIndex.HasValue)
            {
                templateField.ColumnIndex = fieldMapping.ColumnIndex;
                templateField.Confidence = fieldMapping.Confidence;
            }
        }

        // 2. Build column headers (use file headers if present, else generate indices)
        var fileColumns = sniff.HasHeaderRow && sniff.HeaderNames != null
            ? sniff.HeaderNames
            : Enumerable.Range(0, sniff.ColumnCount).Select(i => $"Column {i}").ToArray();

        // 3. Build derived-values provider (Admin1 from rules, Timezone from coords, Oracle verification)
        var derivedProvider = BuildDerivedValuesProvider(probe, rules, mapping);

        // 4. Render ColumnMappingWidget with confidence badges and validation preview
        var accepted = ColumnMappingWidget.Show(
            mapping: mapping,
            fileColumns: fileColumns,
            sampleRows: sampleRows,
            derivedValuesProvider: derivedProvider,
            showValidation: true,
            confidenceBadges: true
        );

        if (!accepted)
        {
            throw new OperationCanceledException("User cancelled mapping confirmation.");
        }

        // 5. Convert confirmed ColumnMapping back to MappingProposal
        var confirmedMappings = new List<FieldMapping>();
        foreach (var field in mapping.Fields.Where(f => f.IsMapped))
        {
            confirmedMappings.Add(new FieldMapping
            {
                FieldName = field.Name,
                ColumnIndex = field.ColumnIndex,
                Confidence = field.Confidence,
                Reasoning = field.Confidence > 0 ? $"Confirmed with {field.Confidence:P0} confidence" : "Manually bound"
            });
        }

        return new MappingProposal
        {
            Mappings = confirmedMappings,
            RequireDisambiguation = false,
            AmbiguityReasons = Array.Empty<string>()
        };
    }

    /// <summary>
    /// Build derived-values provider for validation preview.
    /// Shows Admin1 (derived from ZpCode), Timezone (derived from coords), and Oracle verification.
    /// </summary>
    private Func<ColumnMapping, string[], (string[] derivedValues, string[] validationNotes)> BuildDerivedValuesProvider(
        ProbeResult probe,
        ICountryRules rules,
        ColumnMapping mapping)
    {
        return (map, row) =>
        {
            var fieldCount = map.Fields.Count;
            var derived = new string[fieldCount];
            var notes = new string[fieldCount];

            // Find ZpCode column
            var zpCodeField = map.Fields.FirstOrDefault(f => f.Name == "ZpCode");
            var zpCode = zpCodeField?.IsMapped == true && zpCodeField.ColumnIndex < row.Length
                ? row[zpCodeField.ColumnIndex.Value]?.Trim()
                : null;

            // Admin1 derivation (if rules support it)
            if (!string.IsNullOrWhiteSpace(zpCode) && rules.SupportsAdmin1Derivation)
            {
                var adminResult = rules.ResolveAdmin1(zpCode);
                var admin1Code = adminResult?.Code;
                var admin1Name = adminResult?.Name;

                var admin1Field = map.Fields.FirstOrDefault(f => f.Name == "Admin1");
                if (admin1Field != null)
                {
                    var idx = ((IList<ColumnMappingField>)map.Fields).IndexOf(admin1Field);
                    if (idx >= 0) derived[idx] = admin1Name ?? string.Empty;
                }

                var admin1CodeField = map.Fields.FirstOrDefault(f => f.Name == "Admin1Code");
                if (admin1CodeField != null)
                {
                    var idx = ((IList<ColumnMappingField>)map.Fields).IndexOf(admin1CodeField);
                    if (idx >= 0) derived[idx] = admin1Code ?? string.Empty;
                }
            }

            // Timezone from coordinates
            var latField = map.Fields.FirstOrDefault(f => f.Name == "Latitude");
            var lngField = map.Fields.FirstOrDefault(f => f.Name == "Longitude");

            if (latField?.IsMapped == true && lngField?.IsMapped == true &&
                latField.ColumnIndex < row.Length && lngField.ColumnIndex < row.Length)
            {
                var latStr = row[latField.ColumnIndex.Value]?.Trim() ?? "";
                var lngStr = row[lngField.ColumnIndex.Value]?.Trim() ?? "";

                var tz = TimezoneResolver.TryResolveWithCoordinates(latStr, lngStr);
                if (!string.IsNullOrWhiteSpace(tz))
                {
                    var tzField = map.Fields.FirstOrDefault(f => f.Name == "Timezone");
                    if (tzField != null)
                    {
                        var idx = ((IList<ColumnMappingField>)map.Fields).IndexOf(tzField);
                        if (idx >= 0) derived[idx] = tz;
                    }
                }
            }

            // Oracle verification on ZpCode
            if (!string.IsNullOrWhiteSpace(zpCode) && zpCodeField != null)
            {
                var oracleHit = probe.SampleHits.Any(h =>
                    string.Equals(h.InputValue, zpCode, StringComparison.OrdinalIgnoreCase));

                var idx = ((IList<ColumnMappingField>)map.Fields).IndexOf(zpCodeField);
                if (idx >= 0) notes[idx] = oracleHit ? "✓ Oracle verified" : "✗ Not in built-in data";
            }

            return (derived, notes);
        };
    }
}
