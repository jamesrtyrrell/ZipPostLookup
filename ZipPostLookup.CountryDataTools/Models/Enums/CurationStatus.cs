using System.Text.Json.Serialization;

namespace ZipPostLookup.CountryDataTools.Models.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]

public enum CurationStatus
{
    /// <summary>No reference CSV has been loaded yet.</summary>
    NoData,

    /// <summary>Data exists but has not been through a full validation cycle.</summary>
    UnderReview,

    /// <summary>Validation pipeline completed and issues documented; not yet signed off.</summary>
    Reviewed,

    /// <summary>Fully validated, signed off, and safe to treat as a source of truth.</summary>
    Curated,
}