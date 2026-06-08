namespace ZipPostLookup.CountryDataTools.Commands.Handlers.Analyse;

/// <summary>
/// A single analysis result. <see cref="AiPrompt"/> is a self-contained prompt that
/// can be pasted into an AI assistant to get an implementation tailored to this dataset.
/// </summary>
public sealed record Finding(
    string Id,
    string Category,
    string Title,
    string Severity,
    string Summary,
    string AiPrompt);
