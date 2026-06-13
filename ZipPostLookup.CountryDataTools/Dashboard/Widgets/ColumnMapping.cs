using System.Reflection;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

/// <summary>
/// One row of the universal mapping template: a <see cref="CodesCandidate"/> data field and
/// the incoming column (if any) the user has bound it to. Only <see cref="Mandatory"/> fields
/// are editable — non-starred fields render locked.
/// </summary>
internal sealed class ColumnMappingField
{
    public required string Name { get; init; }

    /// <summary>The '*' marker — mandatory AND editable. Must be bound before Accept.</summary>
    public bool Mandatory { get; init; }

    /// <summary>Bound incoming column index, or null when unmapped.</summary>
    public int? ColumnIndex { get; set; }

    public bool IsMapped => ColumnIndex.HasValue;
}

/// <summary>
/// The universal column-mapping template. The field list is derived once from the data fields
/// of <see cref="CodesCandidate"/> — every public read/write property except the system fields
/// in <see cref="SystemFields"/> — so the same template renders on every page; a page only
/// decides which fields are starred. <see cref="CodesCandidate.ZpCode"/> is always starred.
/// </summary>
internal sealed class ColumnMapping
{
    /// <summary>CodesCandidate columns that are never user-mappable (identity / pipeline bookkeeping).</summary>
    public static readonly IReadOnlySet<string> SystemFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(CodesCandidate.CandidateId),
            nameof(CodesCandidate.RunId),
            nameof(CodesCandidate.RecordNumber),
            nameof(CodesCandidate.Status),
            nameof(CodesCandidate.CreatedAt),
            nameof(CodesCandidate.AdminCandidateList),
        };

    public IReadOnlyList<ColumnMappingField> Fields { get; }

    private ColumnMapping(IReadOnlyList<ColumnMappingField> fields) => Fields = fields;

    /// <summary>
    /// Builds the template from <see cref="CodesCandidate"/>, starring <paramref name="mandatoryFields"/>
    /// (plus <see cref="CodesCandidate.ZpCode"/>, which is always mandatory). Fields keep their
    /// declaration order. Unknown names in <paramref name="mandatoryFields"/> are ignored.
    /// </summary>
    public static ColumnMapping ForTemplate(params string[] mandatoryFields)
    {
        var stars = new HashSet<string>(mandatoryFields, StringComparer.OrdinalIgnoreCase)
        {
            nameof(CodesCandidate.ZpCode),
        };

        var fields = typeof(CodesCandidate)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !SystemFields.Contains(p.Name))
            .OrderBy(p => p.MetadataToken)   // declaration order within the type
            .Select(p => new ColumnMappingField
            {
                Name      = p.Name,
                Mandatory = stars.Contains(p.Name),
            })
            .ToList();

        return new ColumnMapping(fields);
    }

    /// <summary>The starred (editable + mandatory) fields, in template order.</summary>
    public IReadOnlyList<ColumnMappingField> EditableFields =>
        Fields.Where(f => f.Mandatory).ToList();

    /// <summary>True once every mandatory field is bound to a column.</summary>
    public bool AllMandatoryMapped => Fields.Where(f => f.Mandatory).All(f => f.IsMapped);

    /// <summary>The bound fields as a field-name → column-index map (mapped fields only).</summary>
    public IReadOnlyDictionary<string, int> ToColumnMap() =>
        Fields.Where(f => f.IsMapped)
              .ToDictionary(f => f.Name, f => f.ColumnIndex!.Value, StringComparer.OrdinalIgnoreCase);
}
