namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse.CountryAnalysis;

/// <summary>
/// Shared helper for the headline "admin1 derivability" finding. Runs a country-supplied
/// derivation function over every row and compares it to the stored admin1 code. When a high
/// fraction matches, admin1 is a pure function of the code and can be dropped from the image
/// entirely (derived at load time) rather than merely dictionary-indexed.
/// </summary>
internal static class AdminDerivability
{
    /// <param name="derive">Returns the derived admin1 code for a ZpCode, or null if undecidable.</param>
    /// <param name="mechanism">Short human description of how derivation works (for the report).</param>
    public static IEnumerable<Finding> Evaluate(
        AnalysisContext ctx,
        IDictionary<string, string> metrics,
        Func<string, string?> derive,
        string mechanism)
    {
        if (ctx.TotalRows == 0) yield break;

        int decidable = 0, matches = 0;
        var conflicts = new List<(string Code, string Stored, string Derived)>();

        foreach (var r in ctx.Rows)
        {
            var derived = derive(r.ZpCode);
            if (string.IsNullOrEmpty(derived)) continue;
            decidable++;

            if (string.Equals(derived, r.Admin1Code, StringComparison.OrdinalIgnoreCase))
                matches++;
            else if (conflicts.Count < 200)
                conflicts.Add((r.ZpCode, r.Admin1Code, derived));
        }

        if (decidable == 0) yield break;

        var matchPct  = matches * 100.0 / decidable;
        var conflictN = decidable - matches;

        metrics["admin1.derivable.decidablePct"] = $"{decidable * 100.0 / ctx.TotalRows:F1}";
        metrics["admin1.derivable.matchPct"]     = $"{matchPct:F1}";
        metrics["admin1.derivable.conflicts"]    = conflictN.ToString("N0");

        // Finding A — the compression opportunity
        yield return new Finding(
            Id: "C01",
            Category: "Compression",
            Title: "Admin1 is derivable from the code — drop the column",
            Severity: matchPct >= 99 ? "High" : matchPct >= 90 ? "Medium" : "Low",
            Summary:
                $"Admin1 can be derived from the code alone ({mechanism}). Of {decidable:N0} decidable " +
                $"rows, {matches:N0} ({matchPct:F1}%) match the stored admin1. At this match rate the " +
                $"admin1 name+code columns are redundant in the shipped image — derive them at load time " +
                $"and store only the {conflictN:N0} exceptions inline.",
            AiPrompt:
                $"For {ctx.Cc}, admin1 is {matchPct:F1}% derivable from the postal code via {mechanism}. " +
                $"Propose moving derivation into the runtime: BuiltInDataSource computes admin1 from the " +
                $"code using the same rule, and the CSV stores admin1 only for the {conflictN:N0} exception " +
                $"rows. Address: (1) where the derivation table lives so the library has no DB dependency, " +
                $"(2) how an exception row overrides the derived value, (3) keeping ZpImageLookup parity, " +
                $"and (4) the size saving vs simple dictionary indexing.");

        // Finding B — the data-quality / enrichment hit-list (only when conflicts exist)
        if (conflictN > 0)
        {
            var sample = string.Join(", ",
                conflicts.Take(15).Select(c => $"{c.Code}:{c.Stored}≠{c.Derived}"));

            yield return new Finding(
                Id: "C02",
                Category: "Data Quality",
                Title: "Stored admin1 disagrees with the derivation rule",
                Severity: matchPct < 90 ? "High" : "Medium",
                Summary:
                    $"{conflictN:N0} rows have a stored admin1 that differs from the rule-derived value. " +
                    $"These are either genuine boundary exceptions or curation errors and form a focused " +
                    $"review/enrichment list. Sample (code:stored≠derived): {sample}.",
                AiPrompt:
                    $"In {ctx.Cc}, {conflictN:N0} codes have a stored admin1 that disagrees with the " +
                    $"derivation rule ({mechanism}). Sample: {sample}. Help me classify these into " +
                    $"(a) legitimate cross-border exceptions to keep, and (b) data errors to correct, and " +
                    $"suggest a SQL or CLI workflow to resolve them in data.Reference.");
        }
    }
}
