namespace ZipPostLookup.Core;

/// <summary>
/// Represents one administrative subdivision level for a postal code entry.
/// </summary>
/// <remarks>
/// <para>
/// Administrative levels are country-specific and ordered from largest to smallest
/// as defined in the embedded <c>zippost_config.json</c>.
/// </para>
/// <para>
/// Examples:
/// <list type="bullet">
///   <item><description>US: Admin1 = State (e.g. "New York" / "NY"), Admin2 = County (e.g. "Kings County" / "---")</description></item>
///   <item><description>CA: Admin1 = Province (e.g. "Ontario" / "ON")</description></item>
///   <item><description>MX: Admin1 = Estado (e.g. "Jalisco" / "JAL"), Admin2 = Municipio, Admin3 = Ciudad</description></item>
/// </list>
/// </para>
/// <para>
/// When a code has no value for a given level, <see cref="Value"/> and <see cref="Code"/>
/// are set to <c>"---"</c> rather than null to avoid null-reference issues in consuming code.
/// </para>
/// </remarks>
/// <param name="Value">
/// The human-readable name for this administrative division,
/// e.g. <c>"Ontario"</c>, <c>"Jalisco"</c>, <c>"New York"</c>.
/// <c>"---"</c> when no value exists for this level.
/// </param>
/// <param name="Code">
/// The short identifier code for this division,
/// e.g. <c>"ON"</c>, <c>"JAL"</c>, <c>"NY"</c>.
/// <c>"---"</c> when no code exists for this level.
/// </param>
/// <param name="LevelName">
/// The name of this administrative level as defined in the country schema,
/// e.g. <c>"Province"</c>, <c>"Estado"</c>, <c>"State"</c>.
/// <c>"---"</c> when the country is not in the config.
/// </param>
public sealed record AdminLevel(
    string Value,
    string Code,
    string LevelName
)
{
    /// <summary>
    /// Returns <c>true</c> if this level has no meaningful value
    /// (both <see cref="Value"/> and <see cref="Code"/> are <c>"---"</c>).
    /// </summary>
    public bool IsEmpty => Value == "---" && Code == "---";

    /// <summary>
    /// Returns a short human-readable summary, e.g. <c>"State: New York (NY)"</c>.
    /// </summary>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return $"{LevelName}: ---";
        }

        return Code == "---"
            ? $"{LevelName}: {Value}"
            : $"{LevelName}: {Value} ({Code})";
    }
}
