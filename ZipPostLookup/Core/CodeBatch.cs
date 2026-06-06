using System.Collections;
using System.Collections.Concurrent;

namespace ZipPostLookup.Core;

/// <summary>
/// A deduplicated, optionally thread-safe collection of postal codes for batch lookup.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CodeBatch"/> is the recommended input type for
/// <see cref="ZipPostRegistry.GetBatch(CodeBatch)"/>. It guarantees:
/// <list type="bullet">
///   <item><description>Deduplication — duplicate codes are silently ignored on <see cref="Add"/></description></item>
///   <item><description>Optional concurrent building — pass <c>concurrent: true</c> when multiple threads add codes simultaneously</description></item>
///   <item><description>Immutability on use — sealed on first iteration so the registry always sees a consistent snapshot</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Single-threaded build (default — fastest):</b>
/// </para>
/// <code>
/// var batch = new CodeBatch()
///     .Add("10001")
///     .Add("90210")
///     .Add("10001")      // silently deduped
///     .AddRange(myList);
///
/// var results = registry.GetBatch(batch);
/// </code>
/// <para>
/// <b>Multi-threaded build:</b>
/// </para>
/// <code>
/// var batch = new CodeBatch(concurrent: true);
/// Parallel.ForEach(myData, item => batch.Add(item.ZipCode));
/// var results = registry.GetBatch(batch);
/// </code>
/// </remarks>
public sealed class CodeBatch : IEnumerable<string>
{
    // ── Internal storage — one of two implementations ─────────────────────────

    // Single-threaded path: plain HashSet (faster, no locking)
    private HashSet<string>? _set;

    // Multi-threaded path: ConcurrentDictionary used as a concurrent HashSet
    // (byte value is always 0 — only the key matters)
    private ConcurrentDictionary<string, byte>? _concurrent;

    private volatile bool _sealed;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an empty batch. Use <c>concurrent: true</c> when multiple threads
    /// will call <see cref="Add"/> simultaneously.
    /// </summary>
    /// <param name="concurrent">
    /// <c>true</c> to use a thread-safe backing store (slightly slower for single-threaded use).
    /// <c>false</c> (default) for the fast single-threaded path.
    /// </param>
    /// <param name="capacity">
    /// Optional initial capacity hint. Ignored when <paramref name="concurrent"/> is <c>true</c>.
    /// </param>
    public CodeBatch(bool concurrent = false, int capacity = 16)
    {
        if (concurrent)
        {
            _concurrent = new ConcurrentDictionary<string, byte>(
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Creates a batch pre-populated from an existing sequence of codes.
    /// Duplicates in <paramref name="codes"/> are silently ignored.
    /// </summary>
    /// <param name="codes">Initial codes to add.</param>
    /// <param name="concurrent">
    /// <c>true</c> to use a thread-safe backing store.
    /// </param>
    public CodeBatch(IEnumerable<string> codes, bool concurrent = false)
        : this(concurrent)
    {
        if (codes == null)
        {
            throw new ArgumentNullException(nameof(codes));
        }

        foreach (var code in codes)
        {
            AddInternal(code);
        }
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a postal code to the batch. Duplicates are silently ignored.
    /// Null or whitespace-only values are silently ignored.
    /// </summary>
    /// <param name="code">The postal code to add.</param>
    /// <returns>This batch, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the batch has already been passed to <see cref="ZipPostRegistry.GetBatch(CodeBatch)"/>
    /// and sealed.
    /// </exception>
    public CodeBatch Add(string code)
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                "This CodeBatch has already been used for a lookup and is sealed. " +
                "Create a new CodeBatch to perform another batch lookup.");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            AddInternal(code);
        }

        return this;
    }

    /// <summary>
    /// Adds multiple postal codes to the batch. Duplicates are silently ignored.
    /// Null or whitespace-only values are silently ignored.
    /// </summary>
    /// <param name="codes">The postal codes to add.</param>
    /// <returns>This batch, for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="codes"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the batch is sealed.</exception>
    public CodeBatch AddRange(IEnumerable<string> codes)
    {
        if (codes == null)
        {
            throw new ArgumentNullException(nameof(codes));
        }

        if (_sealed)
        {
            throw new InvalidOperationException(
                "This CodeBatch has already been used for a lookup and is sealed. " +
                "Create a new CodeBatch to perform another batch lookup.");
        }

        foreach (var code in codes)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                AddInternal(code);
            }
        }

        return this;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>
    /// The number of unique codes currently in the batch.
    /// </summary>
    public int Count =>
        _set != null ? _set.Count : _concurrent!.Count;

    /// <summary>
    /// <c>true</c> if the batch is empty.
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// <c>true</c> if the batch has been sealed after use in a lookup.
    /// A sealed batch cannot have codes added to it.
    /// </summary>
    public bool IsSealed => _sealed;

    // ── IEnumerable<string> ───────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all unique codes in the batch and seals it against further modification.
    /// </summary>
    public IEnumerator<string> GetEnumerator()
    {
        _sealed = true;

        if (_set != null)
        {
            return _set.GetEnumerator();
        }

        return _concurrent!.Keys.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddInternal(string code)
    {
        if (_set != null)
        {
            _set.Add(code);
        }
        else
        {
            _concurrent!.TryAdd(code, 0);
        }
    }

    /// <summary>
    /// Returns a string summarising the batch state, e.g.
    /// <c>"CodeBatch(42 codes)"</c> or <c>"CodeBatch(42 codes, sealed)"</c>.
    /// </summary>
    public override string ToString() =>
        _sealed
            ? $"CodeBatch({Count:N0} codes, sealed)"
            : $"CodeBatch({Count:N0} codes)";
}