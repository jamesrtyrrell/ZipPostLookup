namespace ZipPostLookup.CountryDataTools.Export.ZPimage;

/// <summary>
/// A minimal perfect hash (MPHF) over a set of <see cref="ulong"/> keys: it maps the
/// <c>n</c> input keys bijectively onto the slots <c>0..n-1</c> with no gaps and no
/// collisions, and evaluates in O(1) with no allocation.
///
/// <para>Built with the "hash, displace" scheme (Hanov / a simplified CHD): keys are
/// scattered into <c>n</c> buckets by a first hash; buckets are processed largest-first,
/// and for each multi-key bucket a small seed <c>d</c> is searched such that re-hashing its
/// keys with <c>d</c> lands them all on currently-free slots. Single-key buckets are then
/// dropped straight into the remaining free slots. The per-bucket displacement array
/// <c>g</c> is the only thing that needs to be stored.</para>
///
/// <para>Lookup (v3): compute one seed-independent <c>hb = HashBase(key)</c>, then
/// <c>g = G[Reduce(hb, 0)]</c>. If <c>g &lt; 0</c> the slot is <c>-g - 1</c> (a parked single-key
/// bucket); otherwise the slot is <c>Reduce(hb, g)</c>, where <c>Reduce</c> folds in the seed and
/// maps into <c>0..n-1</c> by fastrange (multiply-shift) rather than a modulo. Because an MPHF maps
/// <i>unknown</i> keys to arbitrary slots, callers must still verify the stored key at the resolved
/// slot to detect a miss.</para>
///
/// <para>This is a build-time construction; it is deterministic for a given key set and
/// self-checks via <see cref="ZpImageBuilder"/>. If a pathological key set cannot be placed
/// within <see cref="MaxSeed"/> attempts it throws rather than emitting a broken table.</para>
/// </summary>
internal sealed class MinimalPerfectHash
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime       = 1099511628211UL;
    private const int   MaxSeed        = 10_000_000;

    private readonly int[] _g;

    private MinimalPerfectHash(int slotCount, int[] g)
    {
        SlotCount = slotCount;
        _g        = g;
    }

    /// <summary>Number of slots = number of keys (the table is minimal).</summary>
    public int SlotCount { get; }

    /// <summary>The displacement array, serialised into the <c>Mphf</c> section.</summary>
    public IReadOnlyList<int> Displacements => _g;

    /// <summary>Resolves <paramref name="key"/> to its slot in <c>0..SlotCount-1</c>.</summary>
    public int Evaluate(ulong key)
    {
        if (SlotCount == 0)
        {
            return 0;
        }

        // v3: one FNV byte-pass (HashBase), reused for both the bucket and the displaced slot;
        // the modulo is replaced by branch-free fastrange (Reduce). Must stay byte-identical to
        // the reader's ZpHash.HashBase / ZpHash.Reduce.
        var hb = HashBase(key);
        var g  = _g[Reduce(hb, 0, SlotCount)];
        return g < 0
            ? -g - 1
            : Reduce(hb, (uint)g, SlotCount);
    }

    /// <summary>Builds an MPHF over <paramref name="keys"/>, which must be distinct.</summary>
    public static MinimalPerfectHash Build(IReadOnlyList<ulong> keys)
    {
        var n = keys.Count;
        if (n == 0)
        {
            return new MinimalPerfectHash(0, Array.Empty<int>());
        }

        // Precompute the seed-independent base hash for each key once (reused for the bucket
        // scatter, every seed attempt below, and avoids re-walking the key bytes repeatedly).
        var hbase = new ulong[n];
        for (var i = 0; i < n; i++)
        {
            hbase[i] = HashBase(keys[i]);
        }

        // Scatter keys into n buckets by the seed-0 reduction.
        var buckets = new List<int>?[n];
        for (var i = 0; i < n; i++)
        {
            var b = Reduce(hbase[i], 0, n);
            (buckets[b] ??= new List<int>()).Add(i);
        }

        // Process the most-collided buckets first; ties keep ascending bucket index
        // (OrderByDescending is stable) so the build is fully deterministic.
        var order = Enumerable.Range(0, n)
            .Where(b => buckets[b] is { Count: > 1 })
            .OrderByDescending(b => buckets[b]!.Count)
            .ToList();

        var g        = new int[n];
        var slotTaken = new bool[n];

        // Phase 1 — multi-key buckets: find a seed that places every key on a free slot.
        var candidate = new List<int>();
        foreach (var b in order)
        {
            var bucket = buckets[b]!;
            var seed   = 1;

            while (true)
            {
                if (seed > MaxSeed)
                {
                    throw new InvalidOperationException(
                        $"MPHF construction failed: bucket of {bucket.Count} keys could not be " +
                        $"placed within {MaxSeed} seeds (likely duplicate keys).");
                }

                candidate.Clear();
                var ok = true;

                foreach (var keyIndex in bucket)
                {
                    var slot = Reduce(hbase[keyIndex], (uint)seed, n);
                    if (slotTaken[slot] || candidate.Contains(slot))
                    {
                        ok = false;
                        break;
                    }

                    candidate.Add(slot);
                }

                if (ok)
                {
                    break;
                }

                seed++;
            }

            g[b] = seed;
            foreach (var slot in candidate)
            {
                slotTaken[slot] = true;
            }
        }

        // Phase 2 — single-key buckets: park each in the next free slot (negative encoding).
        var freeSlots = new Queue<int>();
        for (var s = 0; s < n; s++)
        {
            if (!slotTaken[s])
            {
                freeSlots.Enqueue(s);
            }
        }

        for (var b = 0; b < n; b++)
        {
            if (buckets[b] is { Count: 1 } single)
            {
                var slot = freeSlots.Dequeue();
                slotTaken[slot] = true;
                g[b] = -slot - 1;
                _ = single; // bucket key resolves to this slot via Evaluate
            }
        }

        return new MinimalPerfectHash(n, g);
    }

    /// <summary>
    /// Seed-independent FNV-1a over the eight little-endian bytes of <paramref name="key"/>. The
    /// seed is folded in separately by <see cref="Reduce"/>, so this byte-walk runs once per key
    /// and is shared between the bucket and slot reductions. Unrolled; keep byte-identical to the
    /// reader's <c>ZpHash.HashBase</c>.
    /// </summary>
    private static ulong HashBase(ulong key)
    {
        var h = FnvOffsetBasis;
        h = (h ^ (key & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 8) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 16) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 24) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 32) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 40) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 48) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 56) & 0xFF)) * FnvPrime;
        return h;
    }

    /// <summary>
    /// Folds <paramref name="seed"/> into the base hash and maps the result into <c>[0, n)</c> via
    /// Lemire's fastrange (a multiply-shift), replacing a 64-bit modulo. Keep byte-identical to the
    /// reader's <c>ZpHash.Reduce</c>.
    /// </summary>
    private static int Reduce(ulong hashBase, uint seed, int n)
    {
        var h = (hashBase ^ seed) * FnvPrime;
        return (int)(((h >> 32) * (ulong)n) >> 32);
    }
}
