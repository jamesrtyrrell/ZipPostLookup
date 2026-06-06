namespace ZipPostLookup.CountryDataTools.Export.Stages;

/// <summary>
/// Replaces repeated timezone and admin1 string values with integer indices
/// and records the lookup tables in <see cref="ExportMeta"/>.
///
/// The indices are written into the <c>#meta:</c> CSV header line so that
/// <c>BuiltInDataSource</c> can reconstruct the original strings at load time.
/// Both indexes are sorted alphabetically for deterministic output across runs.
///
/// Size savings (CA):
///   Timezone  — 11 unique values × avg ~16 chars → saves ~12.6 MB
///   Admin1    — 13 unique pairs  × avg ~12 chars → saves ~8.8 MB
/// </summary>
internal sealed class StringIndexStage : IExportStage
{
    public string StageName => "String Index";

    public (List<ExportRow> Rows, ExportMeta Meta) Apply(
        List<ExportRow> rows, ExportMeta meta)
    {
        if (rows.Count == 0)
        {
            return (rows, meta);
        }

        // Build timezone index — sorted for determinism
        var tzSet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            tzSet.Add(r.Timezone);
        }
        var tzIndex = tzSet.ToArray();

        // Build admin1 index — sorted by code for determinism
        var adminSeen = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (!adminSeen.ContainsKey(r.Admin1Code))
            {
                adminSeen[r.Admin1Code] = r.Admin1;
            }
        }
        var adminIndex = adminSeen
            .Select(kv => (Code: kv.Key, Name: kv.Value))
            .ToArray();

        Console.WriteLine(
            $"    {StageName}: {tzIndex.Length} timezone(s), " +
            $"{adminIndex.Length} admin1 division(s) indexed.");

        return (rows, meta with { TimezoneIndex = tzIndex, AdminIndex = adminIndex });
    }
}
