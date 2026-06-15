# ZipPostLookup

Fast, extensible in-memory postal code lookup for .NET.  
Built-in data for the **United States** (<!-- zpl:us:codes -->43,501<!-- /zpl:us:codes --> ZIP codes), **Canada** (~<!-- zpl:ca:codes -->901,482<!-- /zpl:ca:codes --> postal codes, <!-- zpl:ca:entries -->620k<!-- /zpl:ca:entries --> compressed entries via FSA range compression), and **Mexico** (<!-- zpl:mx:codes -->32,896<!-- /zpl:mx:codes --> postal codes). All three countries are fully curated ✅.

Code counts and status, last updated on: **(<!-- zpl:updated -->2026-06-15 08:07:27<!-- /zpl:updated -->)**

---

## Installation

```bash
dotnet add package ZipPostLookup
```
### Legacy Systems
Targets netstandard2.0, covering .NET Framework 4.6.1+ / .NET Core 2+ / .NET 5–7

```bash
dotnet add package ZipPostLookup.Legacy
```
---

## Quick Start

```csharp
using ZipPostLookup;

// US lookup — uses the built-in default registry (no setup required)
var entry = ZipPostLookup.GetByZip("10001");
// => 10001 New York, New York (America/New_York)

Console.WriteLine(entry.PlaceName);   // New York
Console.WriteLine(entry.Admin1Code);  // NY
Console.WriteLine(entry.Admin1);      // New York
Console.WriteLine(entry.Timezone);    // America/New_York
Console.WriteLine(entry.GetLocalTime());
```

---

## IZipPostLookup Methods

All static methods on `ZipPostLookup` target the built-in US registry. `GetByCode` / `GetByZip`, `TryGetByCode` / `TryGetByZip`, `GetAllByCode` / `GetAllByZip`, and `GetByName` / `GetByCity` are **identical overloads** — use whichever reads more naturally for your context.

| Method                          | Returns                                   | Description                                                                    |
|---------------------------------|-------------------------------------------|--------------------------------------------------------------------------------|
| `GetByCode(code)`               | `CodeEntry?`                              | Default entry for a postal code, or `null` if not found                        |
| `GetByZip(zip)`                 | `CodeEntry?`                              | Overload — identical to `GetByCode`                                            |
| `TryGetByCode(code, out entry)` | `bool`                                    | Try-Parse pattern — preferred for user-supplied input                          |
| `TryGetByZip(zip, out entry)`   | `bool`                                    | Overload — identical to `TryGetByCode`                                         |
| `GetAllByCode(code)`            | `IReadOnlyList<CodeEntry>`                | All entries for a postal code (some codes have multiple place names)           |
| `GetAllByZip(zip)`              | `IReadOnlyList<CodeEntry>`                | Overload — identical to `GetAllByCode`                                         |
| `GetClosest(zip)`               | `CodeEntry?`                              | Nearest ZIP code by numeric distance when no exact match exists                |
| `GetByName(name)`               | `IReadOnlyList<CodeEntry>`                | All entries for a place name (case-insensitive)                                |
| `GetByCity(city)`               | `IReadOnlyList<CodeEntry>`                | Overload — identical to `GetByName`                                            |
| `GetByState(code)`              | `IReadOnlyList<CodeEntry>`                | All entries for an admin level 1 code (e.g. `"NY"`, `"ON"`)                    |
| `GetByStateName(name)`          | `IReadOnlyList<CodeEntry>`                | All entries for an admin level 1 name                                          |
| `GetByAdmin(value, levelName)`  | `IReadOnlyList<CodeEntry>`                | All entries for a named admin level — resolves aliases from the country schema |
| `GetByTimeZone(tz)`             | `IReadOnlyList<CodeEntry>`                | All entries for an IANA timezone identifier                                    |
| `GetBatch(batch)`               | `IReadOnlyDictionary<string, CodeEntry?>` | Look up multiple codes at once; efficient when duplicates are expected         |
| `GetRandom()`                   | `CodeEntry`                               | Random entry using `Random.Shared`                                             |
| `GetRandom(random)`             | `CodeEntry`                               | Random entry using a provided `Random` instance                                |
| `GetAll()`                      | `IReadOnlyList<CodeEntry>`                | All entries sorted by postal code                                              |
| `GetCountryInfo(country)`       | `CountryInfo?`                            | Country metadata (code count, curation status, format rules)                   |
| `ThrowReasonExceptions(on)`     | `void`                                    | Opt in to exceptions explaining *why* a lookup missed (default off) — see below |

---

## CodeEntry Properties

| Property              | Type             | Description                                                        |
|-----------------------|------------------|--------------------------------------------------------------------|
| `ZpCode`              | `string`         | Postal code — e.g. `"10001"` (US), `"M5V3L9"` (CA), `"06600"` (MX) |
| `PlaceName`           | `string`         | Primary place name — city, locality, colonia, or community         |
| `Timezone`            | `string`         | IANA timezone identifier                                           |
| `IsDefault`           | `bool`           | `true` if primary entry when multiple place names share a code     |
| `Admins`              | `AdminLevel[]`   | Administrative subdivision levels — ordered largest to smallest    |
| `Admin1`              | `string?`        | Value of the first admin level (state/province/estado name)        |
| `Admin1Code`          | `string?`        | Code of the first admin level (`"NY"`, `"ON"`, `"JAL"`)            |
| `Admin2`              | `string?`        | Value of the second admin level (county, municipio, etc.)          |
| `Admin2Code`          | `string?`        | Code of the second admin level                                     |
| `Admin3`              | `string?`        | Value of the third admin level                                     |
| `Admin3Code`          | `string?`        | Code of the third admin level                                      |
| `GetAdmin(levelName)` | `AdminLevel?`    | Lookup by level name or alias, e.g. `GetAdmin("Province")`         |
| `GetLocalTime()`      | `DateTimeOffset` | Current local time in this entry's timezone                        |

Administrative subdivisions are stored as an `AdminLevel[]` array (ordered largest to smallest), each with `Value`, `Code`, and `LevelName` (`"State"`, `"Province"`, `"Estado"`). `GetByAdmin` and `GetAdmin` resolve level name aliases from the country schema.

```csharp
var entry = ZipPostLookup.GetByCode("90210");
Console.WriteLine(entry.Admin1);              // California
Console.WriteLine(entry.Admin1Code);          // CA
Console.WriteLine(entry.Admins[0].LevelName); // State

var ca = canadaRegistry.GetByCode("T0A0A0");
Console.WriteLine(ca.Admin1);              // Alberta
Console.WriteLine(ca.Admin1Code);          // AB
Console.WriteLine(ca.Admins[0].LevelName); // Province
```

---

## Targets

| Target              | Minimum version |
|---------------------|-----------------|
| .NET                | 5+              |
| .NET Core           | 2.0+            |
| .NET Framework      | 4.6.1+          |
| .NET Standard       | 2.0+            |
| Mono                | 5.4+            |
| Unity               | 2018.1+         |

---

## Lookups

```csharp
// US
CodeEntry? entry   = ZipPostLookup.GetByCode("90210");
CodeEntry? same    = ZipPostLookup.GetByZip("90210");     // identical overload

if (ZipPostLookup.TryGetByCode(userInput, out var result))
    Console.WriteLine(result.PlaceName);

IReadOnlyList<CodeEntry> all     = ZipPostLookup.GetAllByCode("00603"); // Aguadilla + Ramey
CodeEntry?               closest = ZipPostLookup.GetClosest("00001");
IReadOnlyList<CodeEntry> byName  = ZipPostLookup.GetByName("New York");
IReadOnlyList<CodeEntry> byState = ZipPostLookup.GetByState("CA");
IReadOnlyList<CodeEntry> byTz    = ZipPostLookup.GetByTimeZone("America/New_York");
CodeEntry random = ZipPostLookup.GetRandom();
CodeEntry seeded = ZipPostLookup.GetRandom(new Random(42));

// Canada
ZipPostRegistry ca      = ZipPostLookup.CreateRegistry(CountryCode.CA);
CodeEntry?      toronto = ca.GetByCode("M5V3L9");   // exact LDU
CodeEntry?      anyLdu  = ca.GetByCode("M5V5Z9");   // resolves via FSA range compression
IReadOnlyList<CodeEntry> ontario = ca.GetByState("ON");
IReadOnlyList<CodeEntry> bc      = ca.GetByStateName("British Columbia");

// Mexico
ZipPostRegistry mx  = ZipPostLookup.CreateRegistry(CountryCode.MX);
CodeEntry?      ags = mx.GetByCode("20000");   // Aguascalientes

// Multi-country (US + Canada in one registry)
ZipPostRegistry na       = ZipPostLookup.CreateRegistry(CountryCode.US, CountryCode.CA);
CodeEntry?      newYork  = na.GetByCode("10001");
CodeEntry?      montreal = na.GetByCode("H2X1Y6");

// Implicit string conversion
ZipPostRegistry na2 = ZipPostLookup.CreateRegistry("us", "ca");
```

---

## Lookup Exceptions (Opt-in)

By default a lookup that finds nothing returns `null` (or an empty list) — a silent miss. If you'd
rather be **told why** a lookup yielded nothing, opt in:

```csharp
ZipPostLookup.ThrowReasonExceptions(true);   // default is false — behaviour is unchanged unless you call this

ZipPostLookup.GetByCode("NOTAZIP");          // throws BadlyFormattedCodeException
ZipPostLookup.GetByCode("X0X0X0");           // still null — a valid format that simply isn't present
ZipPostLookup.GetByCode("10001");            // returns the entry as normal
```

When the toggle is on, `GetByCode` / `GetByZip` / `GetAllByCode` throw
`BadlyFormattedCodeException` for input that isn't a valid postal-code format for **any** supported
country (US, CA, MX) — the format is checked against the same per-country normalizers used for
lookups, so **no data is needed**. A well-formed code that just isn't in the data still returns
`null`/empty, and `null` input still returns `null`.

```csharp
try
{
    var entry = ZipPostLookup.GetByCode(userInput);
    // ... use entry, or handle a null (well-formed but unknown) code
}
catch (BadlyFormattedCodeException ex)
{
    Console.WriteLine($"'{ex.Code}' is not a valid US/CA/MX postal-code format.");
}
```

The toggle is **per-registry** — `ThrowReasonExceptions` on the static facade affects the default US
registry; registries from `CreateRegistry` / `CreateCustomRegistry` and `ZpImageLookup` instances each
carry their own setting. All exceptions derive from `CodeReasonException`, which exposes the offending
`Code`.

> Roadmap: a future release adds per-code reason flags so a *found* code can also report that it is
> **obsolete** or **known fake data** (via `ObsoleteCodeException` / `CommonFakeDataException`, already
> present as types). Today only the format-based `BadlyFormattedCodeException` is thrown.

---

## ZipPostLookup Data Quality & Curation

ZipPostLookup is a fast, extensible, in-memory postal-code lookup library for .NET — but its real focus is **data quality, integrity, and long-term maintainability**.

→ [Data Quality & Curation](https://github.com/jamesrtyrrell/ZipPostLookup/blob/main/ZipPostLookup-Data-Curation.md) — the safeguards, data validation checks, and Gold Standard behind every release.

---

## Roadmap to Version 2

Planned work toward the 2.0 release. The runtime library's public API is stable; most of this is data coverage, tooling, and ports.

| Item | Description |
|------|-------------|
| **Audit CDT** | Harden the `CountryDataTools` data-curation CLI — route all DB access through its service layer and centralise every SQL string, so the pipeline is consistent and maintainable. |
| **AI Import Helper** | An `import auto` command that ingests any CSV/TSV/JSON, auto-detects the postal-code column, and maps the remaining fields — using the built-in data as a ground-truth oracle — so new sources need no manual format description. |
| **Code Reason Flags** | Mark codes as *obsolete* or *known fake data* and let lookups throw to explain a miss. The opt-in format check (`BadlyFormattedCodeException` via `ThrowReasonExceptions`) has shipped; per-code obsolete/fake flags are the next phase. |
| **More countries** | Expand built-in data beyond US, CA, and MX. The embedded CSV format is the contract; each new country ships as an embedded resource. |
| **Unit Tests on CDT** | Add automated test coverage to the `CountryDataTools` pipeline (currently covered only by the library's own test suite). |
| **Python Port** | A PyPI package mirroring the lookup API (`get_by_code`, `get_by_name`, `get_by_state`, …), backed by the same embedded CSV data. |

---

## Why No Async Methods?

All lookup methods (`GetByCode`, `GetByName`, `GetByState`, etc.) are synchronous by design. Every registry is fully in-memory — `GetByCode` does a `FrozenDictionary` lookup that completes in ~6 ns. There is no I/O, no network, no disk access at query time.

Async is the right tool for I/O-bound work. Wrapping an in-memory lookup in `Task` or `ValueTask` would:

- Allocate a `Task<T>` object on every call, adding GC pressure on the hot path
- Signal to callers that the operation might be slow or involve I/O — which is false
- Add async state-machine overhead with zero benefit

The standard .NET libraries take the same position: `FrozenDictionary`, `Dictionary`, `IMemoryCache.TryGetValue`, and `MemoryStream.Read` are all synchronous and are called from async code constantly without issue.

**If you need to run a lookup off the calling thread**, use `Task.Run` — but given the nanosecond completion time this is rarely warranted:

```csharp
// Synchronous — fine in async methods, completes in nanoseconds
var entry = registry.GetByCode("M5V3L9");

// Thread-pool dispatch if you genuinely need it
var entry = await Task.Run(() => registry.GetByCode("M5V3L9"));
```

**The one async story on the roadmap** is `ICodeDataSource` — if a future implementation fetches data from a remote source at construction time, a `CreateRegistryAsync` factory would be the appropriate addition. Lookup will remain synchronous regardless, since once loaded the data is entirely in memory.

---

## Binary Image Lookup (net8.0+)

On .NET 8 and later, `ZpImageLookup` loads a pre-built Brotli-compressed binary image instead of parsing CSV rows. It implements `IZipPostLookup` and is a drop-in replacement with 17–26× faster startup and ~30× less memory at startup.

```csharp
using ZipPostLookup.ZPImage;

// Load the built-in image
IZipPostLookup us = ZpImageLookup.FromBuiltIn(CountryCode.US);
IZipPostLookup ca = ZpImageLookup.FromBuiltIn(CountryCode.CA);
IZipPostLookup mx = ZpImageLookup.FromBuiltIn(CountryCode.MX);

// From a file on disk (e.g. a custom country image)
IZipPostLookup img = ZpImageLookup.FromFile("path/to/us.u16.zpi.br");

// All IZipPostLookup methods work identically
CodeEntry? entry = us.GetByCode("90210");
```

See the [Performance](#performance) section for load-time benchmarks.

---

## Dependency Injection

```csharp
// Program.cs
services.AddSingleton<IZipPostLookup>(
    _ => ZipPostLookup.CreateRegistry(CountryCode.US));

// Your service
public class AddressService
{
    private readonly IZipPostLookup _lookup;
    public AddressService(IZipPostLookup lookup) => _lookup = lookup;

    public string? GetTimezone(string postalCode) =>
        _lookup.GetByCode(postalCode)?.Timezone;
}
```

---

## Custom Data Sources

```csharp
public class MilitaryZipSource : ICodeDataSource
{
    public string SourceName => "military-apo";

    public IEnumerable<CodeEntry> GetEntries()
    {
        yield return new CodeEntry(
            ZpCode:    "09001",
            PlaceName: "APO",
            Timezone:  "Europe/Berlin",
            IsDefault: true,
            Admins:    new[] { new AdminLevel("Armed Forces Europe", "AE", "Territory") });
    }
}

var registry = ZipPostLookup.CreateCustomRegistry(new MilitaryZipSource());
var entry    = registry.GetByCode("09001");
```

---

## Country Metadata

```csharp
// Via the static facade
CountryInfo? us = ZipPostLookup.GetCountryInfo(CountryCode.US);
Console.WriteLine(us?.CountryName);     // United States
Console.WriteLine(us?.CodeCount);       // 43,498
Console.WriteLine(us?.CurationStatus); // Curated

// All countries in the embedded data
foreach (var (id, info) in CountryInfoRegistry.All)
    Console.WriteLine(info); // e.g. "US — United States (43,265 codes, curated)"
```

---

## Performance

On .NET 8+, all indexes use `FrozenDictionary`. The `.zpi.br` frozen image loads 17–26x faster than the CSV registry at startup with ~30x less memory:

| Country | CSV registry build | ZP image load | Speedup |
|---------|-------------------:|--------------:|--------:|
| US      |             136 ms |    **7.8 ms** |     17x |
| MX      |             375 ms |   **14.3 ms** |     26x |
| CA      |           1,373 ms |   **55.2 ms** |     25x |

> The CA build was ~33 s before the 2026-06-12 indexing fix (a quadratic insert in registry
> construction); it is now ~1.4 s, which is why the image's cold-start lead over CSV is far
> smaller than it once was. The image still wins decisively on **startup memory** (~30× less)
> and remains the better default for cold-start-sensitive deployments.

`GetByCode` runs at ~5.8 ns (hit) / ~31 ns (miss) on .NET 8+ — 5.6x faster than Zip2City on hot-path lookups with zero allocations on hits. `GetBatch` is more efficient than a sequential loop when duplicate codes are expected; see [ZipPostLookup.Benchmarks/README.MD](https://github.com/jamesrtyrrell/ZipPostLookup/blob/main/ZipPostLookup.Benchmarks/README.MD) for full results including batch lookup guidance, international lookups, and ZPI vs CSV comparisons.

---

## Data

| Country |                                        Distinct Codes* | Admin Levels |                Timezones                | Status     |
|---------|-------------------------------------------------------:|:------------:|:---------------------------------------:|------------|
| US      |  **<!-- zpl:us:codes -->43,501<!-- /zpl:us:codes -->** |    State     | <!-- zpl:us:tz -->56<!-- /zpl:us:tz --> | ✅ Curated  |
| CA      | **<!-- zpl:ca:codes -->901,482<!-- /zpl:ca:codes -->** |   Province   | <!-- zpl:ca:tz -->28<!-- /zpl:ca:tz --> | ✅ Curated  |
| MX      |  **<!-- zpl:mx:codes -->32,896<!-- /zpl:mx:codes -->** |    Estado    | <!-- zpl:mx:tz -->20<!-- /zpl:mx:tz --> | ✅ Curated  |

\* Distinct codes are the number of unique Zip/Postal codes in the country's set.

Code counts and status, last updated on: **(<!-- zpl:updated -->2026-06-15 08:07:27<!-- /zpl:updated -->)**

---

## Contributing Data

New country data is prepared using the companion [ZipPostLookup.CountryDataTools](https://github.com/jamesrtyrrell/ZipPostLookup/blob/main/ZipPostLookup.CountryDataTools/README.MD) CLI. The general flow: obtain a GeoNames or OSM source file → validate and fix with `validate` / `fix` → import with `ingest candidate` → resolve timezone discrepancies → export with `export --all`. Run `snapshot` to regenerate all three countries' CSV and ZPI artifacts in one step. Mark exported files as `EmbeddedResource` in `ZipPostLookup.csproj` and add tests covering at minimum: a known code lookup, an admin/state lookup, a timezone lookup, and a `GetAll` count assertion.

---

## License

[MIT](https://github.com/jamesrtyrrell/LICENSE.md)