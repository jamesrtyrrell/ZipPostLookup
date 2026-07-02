using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ZipPostLookup.CountryDataTools.Ingestion.Models;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Phase 4: LLM-based disambiguation for ambiguous cases.
/// Only called when oracle/correlation cannot decide with high confidence.
/// </summary>
public class DisambiguationService
{
    private const string ClaudeApiUrl = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-sonnet-4-5-20250929";
    private const int DefaultMaxTokens = 2000;

    /// <summary>
    /// Disambiguate ambiguous mapping proposal using LLM.
    /// </summary>
    /// <param name="request">Disambiguation request with context.</param>
    /// <returns>Resolved mapping proposal.</returns>
    public async Task<MappingProposal> DisambiguateAsync(DisambiguationRequest request)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Claude API key not found. Set ANTHROPIC_API_KEY environment variable or pass --no-llm to skip disambiguation.");
        }

        var prompt = BuildPrompt(request);
        var response = await CallClaudeApiAsync(apiKey, prompt);
        var json = ExtractJson(response);

        try
        {
            var resolved = JsonSerializer.Deserialize<MappingProposal>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (resolved == null || resolved.Mappings.Count == 0)
            {
                throw new InvalidOperationException("LLM returned empty or invalid mapping proposal.");
            }

            return resolved;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse LLM response as JSON: {ex.Message}\nResponse: {json}", ex);
        }
    }

    /// <summary>
    /// Build LLM prompt for disambiguation.
    /// </summary>
    private string BuildPrompt(DisambiguationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a data format expert helping import postal code data.");
        sb.AppendLine();
        sb.AppendLine("**File structure:**");
        sb.AppendLine($"- Format: {request.Sniff.Format}");
        sb.AppendLine($"- Columns: {request.Sniff.ColumnCount}");
        sb.AppendLine($"- Header: {(request.Sniff.HasHeaderRow ? "Yes" : "No")}");
        if (request.Sniff.HasHeaderRow && request.Sniff.HeaderNames != null)
        {
            sb.AppendLine($"- Header names: {string.Join(" | ", request.Sniff.HeaderNames)}");
        }
        sb.AppendLine();

        sb.AppendLine("**Oracle probe results:**");
        sb.AppendLine($"- Postal code column: {request.Probe.PostalCodeColumnIndex} (hit rate: {request.Probe.ColumnHitRates[request.Probe.PostalCodeColumnIndex]:P0})");
        sb.AppendLine($"- Dominant country: {request.Probe.DominantCountry}");
        sb.AppendLine();

        sb.AppendLine("**Proposed field mappings:**");
        foreach (var mapping in request.Proposal.Mappings)
        {
            sb.AppendLine($"- {mapping.FieldName}: column {mapping.ColumnIndex} (confidence: {mapping.Confidence:P0})");
            sb.AppendLine($"  Reasoning: {mapping.Reasoning}");
        }
        sb.AppendLine();

        sb.AppendLine("**Ambiguities to resolve:**");
        foreach (var reason in request.Proposal.AmbiguityReasons)
        {
            sb.AppendLine($"- {reason}");
        }
        sb.AppendLine();

        sb.AppendLine($"**Sample rows (first {Math.Min(request.MaxSampleRows, request.SampleRows.Length)}):**");
        for (int i = 0; i < Math.Min(request.MaxSampleRows, request.SampleRows.Length); i++)
        {
            sb.AppendLine($"Row {i}: {string.Join(" | ", request.SampleRows[i])}");
        }
        sb.AppendLine();

        sb.AppendLine("**Task:**");
        sb.AppendLine("Review the proposed mappings and resolve the ambiguities. Return a JSON object with this structure:");
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"mappings\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"fieldName\": \"ZpCode\",");
        sb.AppendLine("      \"columnIndex\": 0,");
        sb.AppendLine("      \"confidence\": 0.95,");
        sb.AppendLine("      \"reasoning\": \"Clear postal code format in column 0\"");
        sb.AppendLine("    },");
        sb.AppendLine("    {");
        sb.AppendLine("      \"fieldName\": \"PlaceName\",");
        sb.AppendLine("      \"columnIndex\": 1,");
        sb.AppendLine("      \"confidence\": 0.87,");
        sb.AppendLine("      \"reasoning\": \"City names in column 1\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"requireDisambiguation\": false,");
        sb.AppendLine("  \"ambiguityReasons\": []");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Only include fields you are confident about (≥60% confidence).");
        sb.AppendLine("- Do NOT duplicate column assignments (one column per field).");
        sb.AppendLine("- If still ambiguous, set requireDisambiguation: true and explain why.");
        sb.AppendLine("- Return ONLY the JSON, no other text.");

        return sb.ToString();
    }

    /// <summary>
    /// Call Claude API with the given prompt.
    /// </summary>
    private async Task<string> CallClaudeApiAsync(string apiKey, string prompt)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var requestBody = new
        {
            model = DefaultModel,
            max_tokens = DefaultMaxTokens,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(ClaudeApiUrl, content);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Claude API error ({response.StatusCode}): {responseText}");
        }

        // Parse response
        using var doc = JsonDocument.Parse(responseText);
        var contentArray = doc.RootElement.GetProperty("content");
        if (contentArray.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Claude API returned empty content array.");
        }

        var textContent = contentArray[0].GetProperty("text").GetString();
        return textContent ?? string.Empty;
    }

    /// <summary>
    /// Extract JSON from LLM response (handles markdown code blocks).
    /// </summary>
    private string ExtractJson(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return "{}";
        }

        // Strip markdown code blocks if present
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```json"))
        {
            var start = trimmed.IndexOf('\n') + 1;
            var end = trimmed.LastIndexOf("```");
            if (end > start)
            {
                return trimmed.Substring(start, end - start).Trim();
            }
        }
        else if (trimmed.StartsWith("```"))
        {
            var start = trimmed.IndexOf('\n') + 1;
            var end = trimmed.LastIndexOf("```");
            if (end > start)
            {
                return trimmed.Substring(start, end - start).Trim();
            }
        }

        // No code blocks, return as-is
        return trimmed;
    }

    /// <summary>
    /// Get API key from environment variable.
    /// </summary>
    private string? GetApiKey()
    {
        return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    }

    /// <summary>
    /// Generate conversational summary after ingestion (Phase 7).
    /// </summary>
    public async Task<string> GenerateSummaryAsync(string summaryPrompt)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "(LLM summary skipped - no API key)";
        }

        try
        {
            var response = await CallClaudeApiAsync(apiKey, summaryPrompt);
            return response.Trim();
        }
        catch (Exception ex)
        {
            return $"(LLM summary failed: {ex.Message})";
        }
    }
}

