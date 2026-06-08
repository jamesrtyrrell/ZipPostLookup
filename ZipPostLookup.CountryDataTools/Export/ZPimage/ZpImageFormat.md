# ZP Frozen Image Format (`.zpi` / `.zpi.br`)

A build-once, read-only binary representation of one country's postal data. Designed to be
decompressed once into a single `byte[]` and queried zero-copy, with no CSV parsing and no
per-entry object allocation at load time. Available on .NET 8+ only.

**Current format version: 4**

---

## Why this exists

The CSV-based `ZipPostRegistry` dominates startup time with two costs:

1. **Parsing** — reading and splitting every CSV row.
2. **Index construction** — building `FrozenDictionary` for every lookup dimension.

For Canada (~430k rows after FSA compression) that's ~15 seconds cold. The frozen image eliminates both:
the primary index (MPHF) is pre-computed and serialised; the data is laid out so fields are read
in-place from the raw bytes; and the two tiny enumerations (timezones, admin1 divisions) are the only
things decoded at load. Result: ~43 ms for CA — a 342× speedup.

The image is byte-consistent with the CSV it was built from: the same timezone/admin1 index ordering
is embedded in both, so the `OptimisedCsvSource` reader and the `ZpImageBuilder` produce identical
lookup results. `ZpImageParityTests` enforces this contract.

---

## File naming convention

The filename encodes the nameIndex width used in the Records section so the reader knows the record
stride without inspecting the header:

| Filename | nameIndex type | RecordBytes | When used |
|---|---|---|---|
| `{cc}.u16.zpi.br` | `u16` | 6 | Normal datasets (all current countries) |
| `{cc}.u32.zpi.br` | `u32` | 8 | Large datasets with > 65,535 unique names |
| `{cc}.zpi.br` | `u32` | 8 | Legacy (V1 images — rejected by V2 reader) |
| `{cc}.u16.zpi` | `u16` | 6 | Uncompressed variant (inspection / memory-mapping) |
| `{cc}.u32.zpi` | `u32` | 8 | Uncompressed large-dataset variant |

The builder auto-promotes to u32 when `NamePool.Count > 65,535` and logs a warning. `FromBuiltIn`
scans for `.u16.zpi.br` first, then `.u32.zpi.br`, then `.zpi.br` (legacy fallback). `FromFile`
detects the width from the path: `.u32.` → u32, otherwise → u16.

The csproj embeds `{cc}*.zpi.br` per country so both existing and future width variants are
automatically picked up without a csproj edit.

---

## On-disk layout (Brotli-compressed: `.zpi.br`)

```
Offset  Size  Field
──────────────────────────────────────────────────────────────────
[Brotli wrapper — .zpi.br files only]
  0       4   UncompressedSize  — u32 LE, size of the raw .zpi payload that follows.
              Read before decompressing to pre-allocate exactly the right buffer.
[Raw .zpi image — after Brotli decompression (or directly for .zpi files)]
  0       4   Magic        — ASCII "ZPIM"
  4       2   Version      — u16, CurrentVersion = 2
  6       2   Flags        — u16, reserved (always 0)
  8       4   Country      — ASCII, NUL-padded to 4 bytes (e.g. "US\0\0")
 12       4   ExactCount   — u32, distinct exact postal codes = MPHF slot count
 16       4   RecordCount  — u32, total records (exact + range rows combined)
 20       2   TzCount      — u16, distinct IANA timezones
 22       2   AdminCount   — u16, distinct admin1 divisions
 24       4   RangeCount   — u32, compressed range rows (CA FSA compression)
 28       2   SectionCount — u16
 30       2   Reserved     — u16, always 0
─── Section table (SectionCount × 12 bytes) ────────────────────
 32+      2   Kind         — u16, SectionKind enum value
          2   Reserved     — u16
          4   Offset       — u32, byte offset from start of raw .zpi image
          4   Length       — u32, payload length in bytes
─── Section payloads (each 8-byte aligned) ─────────────────────
          …   NamePool, TimezoneTable, AdminTable, Mphf,
              Directory, Records, SortedSlots,
              TimezonePostings, AdminPostings, RangeTable
```

Total header size = 32 + SectionCount × 12 bytes. Section payloads follow, each aligned to an
8-byte boundary (padding bytes between sections are unused zeros).

---

## Sections

### 1. NamePool (kind = 1)

A deduplicated, offset-indexed UTF-8 string pool. Every place name, range notation, FSA string, and
range boundary string lives here exactly once, referenced by integer index everywhere else.

```
count    u32             — number of strings
offsets  u32[count + 1]  — byte start of each string; offsets[count] = total bytes length
bytes    u8[…]           — UTF-8 content, strings concatenated with no separator
```

String `i` = `bytes[offsets[i] .. offsets[i+1]]`.

**Reader optimisation (v2):** The entire pool is decoded into a `string[]` array at construction time.
All subsequent `Name(idx)` calls are zero-allocation array lookups; strings are automatically
deduplicated in memory across codes that share the same place name.

### 2. TimezoneTable (kind = 2)

The timezone enumeration. At most 256 entries (enforced at build time — fits in a single byte index).

```
count  u16
entry  (len u16, utf8 u8[len]) × count
```

### 3. AdminTable (kind = 3)

The admin1 enumeration. At most 256 entries. Each entry has both a code (`"ON"`) and a full name
(`"Ontario"`).

```
count  u16
entry  (codeLen u16, code utf8[codeLen], nameLen u16, name utf8[nameLen]) × count
```

**Reader optimisation (v2):** One `AdminLevel[]` array is pre-built per admin division at construction.
`Materialize` references the pre-built array directly — no allocation per lookup.

### 4. Mphf (kind = 4)

The serialised minimal perfect hash function (MPHF) over all exact postal codes. Maps each packed
code to a unique slot in `0..ExactCount-1` in O(1) with no allocation.

```
n   u32      — slot count (= ExactCount)
g   i32[n]   — displacement array (see MPHF section below)
```

### 5. Directory (kind = 5)

One entry per MPHF slot. Maps a slot to the packed code key and either an inlined record (the common
single-record case) or the record span for that code. **16 bytes** (`DirectoryEntryBytes = 16`).

```
entry × ExactCount:
  packedCode   u64   — code packed by CodePacker (for miss-detection; MPHF maps unknowns to arbitrary slots)
  firstRecord  u32   — low 30 bits = first record index in the Records section
                       bit31 (DirInlineFlag)    = entry is inlined (single record, u16 images only)
                       bit30 (DirInlineDefault) = the inlined record is the default entry
  [12]         u8    — inline: tzIndex      | multi: unused
  [13]         u8    — inline: adminIndex   | multi: unused
  [14..16]     u16   — inline: nameIndex    | multi: record count
```

**Fused directory (v4).** For a single-record code (`count == 1`) in a u16 image, the record's
`nameIndex` / `tzIndex` / `adminIndex` / `IsDefault` are copied into the directory entry's spare
bytes (the slots a multi-record entry uses for `count` + reserved). `GetByCode` then materialises the
entry straight from the directory, skipping the second memory fetch into the Records section — the win
on the dominant single-record path (CA ~99%, US ~76% of codes). The record is **still written to the
Records section**, so the cache (keyed by record id), reverse-lookup postings, and `GetAll` are
unchanged; the directory copy is purely a read shortcut. u32 images (NamePool > 65,535) never inline —
their nameIndex doesn't fit the 2-byte slot — and fall back to `firstRecord` + `count`.

### 6. Records (kind = 6)

All records in a flat array. Each code's records are stored contiguously, default entry first.
Range records are appended after all exact records.

**v2 record layout — width depends on filename suffix:**

```
.u16.zpi.br  (RecordBytes = 6, nameIndex u16 — normal datasets):
  nameIndex     u16   — index into NamePool  [offset 0]
  tzIndex       u8    — index into TimezoneTable  [offset 2]
  adminIndex    u8    — index into AdminTable  [offset 3]
  flags         u8    — RecordFlags: bit 0 = IsDefault, bit 1 = IsRange  [offset 4]
  reserved      u8    [offset 5]

.u32.zpi.br  (RecordBytesU32 = 8, nameIndex u32 — large datasets > 65k unique names):
  nameIndex     u32   — index into NamePool  [offset 0]
  tzIndex       u8    — index into TimezoneTable  [offset 4]
  adminIndex    u8    — index into AdminTable  [offset 5]
  flags         u8    — RecordFlags  [offset 6]
  reserved      u8    [offset 7]
```

Named offset constants (`RecordNameOffset`, `RecordTzOffset`, `RecordAdminOffset`, `RecordFlagsOffset`)
are defined in `ZpImageFormat.cs` for the u16 layout. The reader branches on `_u32NameIndex` to read
the correct width.

### 7. SortedSlots (kind = 7)

Directory slot indices in ascending postal code order (by `CodePacker.Pack` value). Used for:
- `GetAll()` — returns all entries sorted by code.
- `GetClosest()` — builds a sorted `int[]` of numeric US ZIPs for binary search.

```
u32[ExactCount]   — slot indices, ascending code order
```

### 8. TimezonePostings (kind = 8) and AdminPostings (kind = 9)

Compressed Sparse Row (CSR) reverse indexes. Given a timezone or admin1 index, returns all record
IDs in that bucket in O(1). Used for `GetByTimeZone`, `GetByState`, `GetByStateName`, `GetByAdmin`.

```
count   u16           — number of buckets (= TzCount or AdminCount)
starts  u32[count+1]  — CSR row starts; bucket i spans ids[starts[i]..starts[i+1]]
ids     u32[RecordCount] — record IDs grouped by bucket, ascending within each bucket
```

### 10. RangeTable (kind = 10)

Compressed FSA range entries (used by Canada). A range row covers all LDU codes whose first four
characters fall within a prefix range (e.g. `A1E0**:A1E6**` covers `A1E0xx` through `A1E6xx`).

```
count   u32
entry × count:
  codeIndex   u32   — NamePool index of the range notation string (e.g. "A1E0**:A1E6**")
  fsaIndex    u32   — NamePool index of the 3-char FSA prefix (e.g. "A1E")
  start4Index u32   — NamePool index of the 4-char start boundary (e.g. "A1E0")
  end4Index   u32   — NamePool index of the 4-char end boundary (e.g. "A1E6")
  recordId    u32   — index into Records section for this range's single record
```

Total: 20 bytes per entry (`RangeEntryBytes = 20`). The reader validates this stride against the
header's `RangeCount` on load and throws an actionable error if the image was built with a different
stride — preventing silent data corruption from format-version drift.

---

## Key algorithms

### Minimal Perfect Hash (MPHF)

Uses the "hash-displace" scheme (CHD variant). Build:

1. Scatter all `n` packed codes into `n` buckets using `Hash(seed=0, key) % n`.
2. Process multi-key buckets largest-first. For each, find a seed `d` such that
   `Hash(d, key) % n` places every key in a currently-free slot.
3. Park single-key buckets directly into remaining free slots using negative encoding: `g[b] = -slot - 1`.

Lookup: `g = G[Hash(0, key) % n]`. If `g < 0`, slot = `-g - 1`. Else slot = `Hash(g, key) % n`.

Because an MPHF maps unknown keys to arbitrary slots, the reader always verifies the stored
`packedCode` at the resolved slot matches before returning a result. This is how misses are detected.

Hash function: seeded FNV-1a over the 8 bytes of the packed `ulong` key.

### CodePacker

Postal codes are packed into a `ulong` for compact storage and fast hashing. Each character is
encoded in 6 bits: digits `0–9` map to 1–10, letters `A–Z` to 11–36. Up to 10 characters fit in
60 bits. The packed form is what the MPHF is built over and what the Directory stores.

### Range lookup

For a code that has no exact entry (e.g. a full LDU like `A1E3H0`):

1. Extract the 3-char FSA prefix (`A1E`) and 4-char boundary key (`A1E3`).
2. Look up the FSA in the lazily-built `_rangeByFsa` dictionary (built once from the RangeTable section).
3. For each range entry under that FSA, compare the 4-char key against `[start4, end4]` (ordinal,
   case-insensitive). If it falls within the range, return the associated record.

The returned `ZpCode` is the original range notation string (`A1E0**:A1E6**`) so callers see the
compressed representation rather than the LDU they queried.

---

## Build process (`ZpImageBuilder`)

1. Dictionary-encode timezones and admin1 to `byte` indices. Reuse `ExportMeta`'s index tables when
   available (so ZPI and CSV use identical orderings).
2. Split rows into exact rows (no `:` in ZpCode) and range rows (contain `:`).
3. Group exact rows by code into a `SortedDictionary`. Within each group, sort default entry first.
4. Build a `BuiltRecord` for each row: intern the place name into `StringPool`, map tz/admin to indices.
5. Parse each range row into (fsa, start4, end4), intern all strings, append a `BuiltRecord` with `IsRange` flag.
6. **Auto-detect nameIndex width:** if `NamePool.Count > 65,535`, set `u32NameIndex = true` and log a
   warning. The filename will be `{cc}.u32.zpi.br` instead of `{cc}.u16.zpi.br`.
7. Build the MPHF over packed codes. Compute the Directory from MPHF slot assignments.
8. Serialise all 10 sections (Records section uses u16 or u32 nameIndex per step 6), assemble with the header.
9. **Self-verify** in memory before returning: every code is re-resolved through the MPHF, the packed
   key at the resolved slot is checked, and the record span bounds are validated. A corrupt image
   cannot be written.

`ZpImageWriter` wraps the raw image in Brotli (prepending the 4-byte uncompressed size) and writes
to `{basePath}.{u16|u32}.zpi.br`. The actual output path is returned in `ZpImageBuildResult.OutputPath`.

---

## Read process (`ZpImageReader`)

Construction:

1. Validate magic (`ZPIM`) and version (rejects V1 images with a re-export hint).
2. Parse the section table into `_sectionOffset[]` / `_sectionLength[]`.
3. **Range stride check**: validate `_sectionLength[RangeTable] == 4 + RangeCount × RangeEntryBytes`.
4. Set `_recordBytes` from the `u32NameIndex` flag passed by the caller (6 for u16, 8 for u32).
5. Decode `TimezoneTable` and `AdminTable` into arrays.
6. **Decode entire NamePool** into `string[]` — all `Name(idx)` calls become zero-alloc array lookups.
7. **Pre-build `AdminLevel[][]`** — one array per admin division, shared across all `Materialize` calls.
8. Allocate `_entryCache` via `GC.AllocateUninitializedArray<CodeEntry?>` — skips zeroing ~3.4 MB for CA.

Hot path (`GetByCode`):

1. `CodePacker.TryPack` the input string → `ulong`.
2. Evaluate the MPHF → slot (using `Unsafe.ReadUnaligned<int>` on the displacement array).
3. Read `packedCode` from the Directory (`Unsafe.ReadUnaligned<ulong>`); compare to detect a miss.
4. Read `firstRecord` (with the inline flag bits) from the Directory.
5. Return `_entryCache[recordId]` if already materialised. Otherwise:
   - **Inline entry (v4, u16 single-record):** read `nameIndex`/`tzIndex`/`adminIndex`/`IsDefault`
     directly from the **same directory entry** and build the `CodeEntry` — the Records section is
     never touched. This removes one random memory access on the dominant path.
   - **Multi-record / u32 entry:** read `count` and call `Materialize(firstRecord, …)`, which reads
     the record fields from the Records section as before.

`Materialize` (first call per non-inlined code):
- Reads nameIndex (`u16` or `u32` depending on `_u32NameIndex`) → `_namePool[nameIdx]` (zero-alloc).
- Reads tzIndex, adminIndex, flags as single-byte array reads.
- Returns `_adminLevels[adminIdx]` (pre-built, zero-alloc).
- Total allocations: **1** (`new CodeEntry(...)`).

Both the inline and `Materialize` paths cache the built `CodeEntry` under the same record id, so a
later reverse-lookup hit on the same record returns the identical instance. Range fallback and reverse
lookups are unchanged; all hot-path reads use `Unsafe.ReadUnaligned` with `[AggressiveInlining]`.

---

## Design invariants

These must be kept in sync between builder and reader:

| Invariant | Where |
|---|---|
| `RecordBytes = 6` (u16) / `RecordBytesU32 = 8` (u32) | Encoded in filename suffix; reader sets `_recordBytes` from caller flag |
| Record field offsets: `RecordNameOffset=0`, `RecordTzOffset=2`, `RecordAdminOffset=3`, `RecordFlagsOffset=4` | u16 layout; u32 layout shifts tz/admin/flags by 2 |
| `DirectoryEntryBytes = 16` | v4 packing: packedCode u64; firstRecord u32 (bit31 Inline, bit30 InlineDefault, low 30 record id); then inline tz u8 @12 / admin u8 @13 / nameIndex u16 @14 — or, for multi/u32, unused @12–13 and count u16 @14 |
| Inline only for u16 single-record codes | Builder sets the inline bit iff `!u32NameIndex && count == 1`; reader gates the inline read on `!_u32NameIndex` too |
| Inlined record also present in Records | Directory inline is a read shortcut; the record stays in Records so postings / cache / GetAll are unaffected |
| `RangeEntryBytes = 20` | Field order: codeIndex u32, fsaIndex u32, start4Index u32, end4Index u32, recordId u32 |
| `SectionEntryBytes = 12` | Field order: kind u16, reserved u16, offset u32, length u32 |
| `HeaderBytes = 32` | See byte map above |
| `SectionAlignment = 8` | All section payloads start on an 8-byte boundary |
| `CurrentVersion = 4` | Reader throws on version mismatch |
| 4-byte size prefix in `.zpi.br` | Written by `ZpImageWriter`; read before decompression in `LoadCompressed` |
| Filename suffix encodes nameIndex width | `.u16.` → u16 (RecordBytes=6), `.u32.` → u32 (RecordBytesU32=8) |
| Range stride check | Reader validates total RangeTable length vs `RangeCount × RangeEntryBytes` |
| Default record first | Builder sorts default entry to index 0 within each code group |
| Range records after exact | Builder appends range records after all exact code records |
| MPHF uses packed codes | Both builder and reader use `CodePacker.Pack` — never raw strings |
| Timezone/admin index order | `ExportMeta` tables shared between CSV and ZPI exports for byte-consistency |
| AltNameOf rows excluded from export | `ExportReferenceData` filters `AltNameOf IS NULL`; including them breaks FSA homogeneity |

Changing any field size in Records, Directory, or RangeTable **requires** bumping `CurrentVersion`.
Changing the nameIndex width only requires using the appropriate filename suffix — no version bump.

---

## File naming

```
countrydatatools export --country CA --target zpi
```
Outputs `ZipPostLookup/Data/ca/ca.u16.zpi.br` (or `.u32.zpi.br` for large datasets).

Rebuild from the committed CSV without a database:
```
countrydatatools export --country CA --target zpi --from-csv
```

---

## Version history

| Version | RecordBytes | Size prefix | nameIndex | MPHF eval | Directory |
|---|---|---|---|---|---|
| 1 (legacy) | 8 | No | u32 (fixed) | modulo, two FNV passes | span only |
| 2 | 6 or 8 | Yes (4 bytes) | u16 or u32 (from filename) | modulo, two FNV passes | span only |
| 3 | 6 or 8 | Yes (4 bytes) | u16 or u32 (from filename) | single FNV + fastrange | span only |
| 4 (current) | 6 or 8 | Yes (4 bytes) | u16 or u32 (from filename) | single FNV + fastrange | **fused** (single-record codes inlined, u16) |

Each reader rejects images whose version it does not recognise, with a re-export hint. The Records
section layout is unchanged across v2–v4; v3 changed only the MPHF evaluation, and v4 only the
Directory entry packing — but both alter lookup results for an old reader, so each bumps the version.

---

## For AI: quick reference

**What it is:** A custom binary format for postal code lookup. One file per country. Brotli-compressed.
`.NET 8+ only`. Built offline by `ZpImageBuilder`, read at runtime by `ZpImageReader`.

**Current version: 4.** Older-version images are rejected at load with a re-export hint.

**Filename convention:** `{cc}.u16.zpi.br` (nameIndex u16, RecordBytes=6, normal) or `{cc}.u32.zpi.br`
(nameIndex u32, RecordBytesU32=8, auto-promoted when NamePool > 65k entries). The suffix is the
authoritative source of the record stride — not the header version. `FromBuiltIn` prefers `.u16.`,
then `.u32.`, then legacy `.zpi.br`. `FromFile` detects from path.

**`.zpi.br` wrapper:** First 4 bytes = uncompressed size (u32 LE). Reader uses this to pre-allocate
an exact buffer via `GC.AllocateUninitializedArray<byte>` before decompressing. No `MemoryStream`.

**Primary lookup:** MPHF maps packed `ulong` code → directory slot. O(1), `Unsafe.ReadUnaligned` on
all hot-path reads. In v4 a single-record code (u16 image) carries its record **inline in the directory
entry**, so the default lookup never reads the Records section; multi-record and u32 entries follow the
`firstRecord` + `count` span into Records.

**First-call Materialize: 1 allocation** (just `CodeEntry`). NamePool decoded into `string[]` at
load (zero-alloc name reads). `AdminLevel[]` pre-built per division at load (zero-alloc admin reads).
`_entryCache` allocated uninitialized (skips zeroing).

**Sections in order:** NamePool · TimezoneTable · AdminTable · Mphf · Directory · Records ·
SortedSlots · TimezonePostings · AdminPostings · RangeTable. Each identified by `u16` kind — unknown
kinds ignored by older readers.

**Range table (CA only):** FSA compression. Missing exact lookup falls through to FSA-keyed scan.
`AltNameOf IS NULL` filter on export query prevents homogeneity breaks. After export, `TestCodes.cs`
is regenerated with a probe code that falls within a current range entry.

**Reverse lookups:** CSR posting lists for timezone and admin1. Name index lazily built once.
All results memoised. `_recordToSlot` map avoids O(n²) materialisation for large CA reverse sets.

**Writer:** `ZipPostLookup.CountryDataTools/Export/ZPimage/ZpImageBuilder.cs` + `ZpImageWriter.cs`.
**Reader:** `ZipPostLookup/ZPImage/ZpImageReader.cs`.
**Shared constants:** `ZpImageFormat.cs` exists in **both** projects — keep them in sync.
**Parity tests:** `ZipPostLookup.Tests/ZpImageParityTests.cs` — probe codes live in `TestCodes.cs`
(auto-generated on export; do not edit manually).
