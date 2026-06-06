# ZipPostLookup

> **⚠ Beta — v0.9.5-beta**  
> This is the first public release. The API and data formats are stabilising but may change before 1.0.0.  
> See [Road to 1.0.0](#road-to-100) for what still needs to land before the stable release.  
> Feedback, bug reports, and data contributions are very welcome.

Fast, extensible in-memory postal code lookup for .NET.  
Built-in data for the **United States** (<!-- zpl:us:codes -->43,178<!-- /zpl:us:codes --> ZIP codes), **Canada** (~<!-- zpl:ca:codes -->900,686<!-- /zpl:ca:codes --> postal codes, <!-- zpl:ca:entries -->431k<!-- /zpl:ca:entries --> compressed entries via FSA range compression), and **Mexico** (<!-- zpl:mx:codes -->32,629<!-- /zpl:mx:codes --> postal codes). All three countries are fully curated ✅.

Code Counts and status, last updated on: **(<!-- zp:updated -->2026-06-05<!-- /zp:updated -->)**

---

## Installation

```bash
dotnet add package ZipPostLookup --prerelease
```

> Once 1.0.0 ships, `--prerelease` will no longer be needed.

---

## Quick Start

```csharp
using ZipPostLookup;

// US lookup — uses the built-in default registry (no setup required)
var entry = ZipPostLookup.GetByZip("10001");
// => 10001 New York, New York (America/New_York)

Console.WriteLine(entry.Name);       // New York
Console.WriteLine(entry.Admin1Code); // NY
Console.WriteLine(entry.Admin1);     // New York
Console.WriteLine(entry.Timezone);   // America/New_York
Console.WriteLine(entry.GetLocalTime());
```

---

## IZipLookup Methods

All static methods on `ZipPostLookup` target the built-in US registry. `GetByCode` / `GetByZip`, `TryGetByCode` / `TryGetByZip`, `GetAllByCode` / `GetAllByZip`, and `GetByName` / `GetByCity` are **identical overloads** — use whichever reads more naturally for your context.

| Method                          | Returns                                  | Description                                                                |
|---------------------------------|------------------------------------------|----------------------------------------------------------------------------|
| `GetByCode(code)`               | `ZipEntry?`                              | Default entry for a postal code, or `null` if not found                    |
| `GetByZip(zip)`                 | `ZipEntry?`                              | Overload — identical to `GetByCode`                                        |
| `TryGetByCode(code, out entry)` | `bool`                                   | Try-Parse pattern — preferred for user-supplied input                      |
| `TryGetByZip(zip, out entry)`   | `bool`                                   | Overload — identical to `TryGetByCode`                                     |
| `GetAllByCode(code)`            | `IReadOnlyList<ZipEntry>`                | All entries for a postal code (some codes have multiple place names)       |
| `GetAllByZip(zip)`              | `IReadOnlyList<ZipEntry>`                | Overload — identical to `GetAllByCode`                                     |
| `GetClosest(zip)`               | `ZipEntry?`                              | Nearest zip by numeric distance when no exact match exists                 |
| `GetByName(name)`               | `IReadOnlyList<ZipEntry>`                | All entries for a place name (case-insensitive)                            |
| `GetByCity(city)`               | `IReadOnlyList<ZipEntry>`                | Overload — identical to `GetByName`                                        |
| `GetByState(code)`              | `IReadOnlyList<ZipEntry>`                | All entries for an admin level 1 code (e.g. `"NY"`, `"ON"`)                |
| `GetByStateName(name)`          | `IReadOnlyList<ZipEntry>`                | All entries for an admin level 1 name                                      |
| `GetByAdmin(value, levelName)`  | `IReadOnlyList<ZipEntry>`                | All entries for a named admin level — resolves aliases from country schema |
| `GetByTimeZone(tz)`             | `IReadOnlyList<ZipEntry>`                | All entries for an IANA timezone identifier                                |
| `GetBatch(batch)`               | `IReadOnlyDictionary<string, ZipEntry?>` | Look up multiple codes at once; efficient when duplicates are expected     |
| `GetRandom()`                   | `ZipEntry`                               | Random entry using `Random.Shared`                                         |
| `GetRandom(random)`             | `ZipEntry`                               | Random entry using a provided `Random` instance                            |
| `GetAll()`                      | `IReadOnlyList<ZipEntry>`                | All entries sorted by postal code                                          |

---

## ZipEntry Properties

| Property              | Type             | Description                                                        |
|-----------------------|------------------|--------------------------------------------------------------------|
| `Code`                | `string`         | Postal code — e.g. `"10001"` (US), `"M5V3L9"` (CA), `"06600"` (MX) |
| `Name`                | `string`         | Primary place name — city, locality, colonia, or community         |
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

## Road to 1.0.0

### Data quality

| Country |                                    Distinct Codes* | Status        | Notes                                                                                                                                                                                                                                                                                                                                       |
|---------|---------------------------------------------------:|---------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| US      |  <!-- zpl:us:codes -->43,178<!-- /zpl:us:codes --> | ✅ **Curated** | All timezones and place names independently verified. Includes territories (PR, VI, GU, AS, MP), Armed Forces (AA/AE/AP), and Parcel Return Service (PRS) zips.                                                                                                                                                                             |
| CA      | <!-- zpl:ca:codes -->900,686<!-- /zpl:ca:codes --> | ✅ **Curated** | <!-- zpl:ca:tz -->21<!-- /zpl:ca:tz --> IANA timezones, <!-- zpl:ca:divs -->14<!-- /zpl:ca:divs --> provinces/territories. FSA compression — homogeneous FSAs stored as ranges; all ~<!-- zpl:ca:codes:k -->~900k<!-- /zpl:ca:codes:k --> LDU codes resolve transparently from <!-- zpl:ca:entries -->431k<!-- /zpl:ca:entries --> entries. |
| MX      |  <!-- zpl:mx:codes -->32,629<!-- /zpl:mx:codes --> | ✅ **Curated** | Colonia-level data. <!-- zpl:mx:tz -->10<!-- /zpl:mx:tz --> IANA timezones across <!-- zpl:mx:divs -->48<!-- /zpl:mx:divs --> estados.                                                                                                                                                                                                      |

* Distinct codes are the number of unique Zip/Postal codes in the country's set.

Code Counts and status, last updated on: **(<!-- zpl:updated -->2026-06-06 20:02:52<!-- /zpl:updated -->)**

### Country-specific input rules

- ~~**`ICountryCodeRules` interface** — per-country normalisation and validation~~
- ~~**US ZIP+4 handling** — `12345-6789`, `123456789`, and `12345 6789` all resolve to `12345`~~
- ~~**Mexico** — 5-digit numeric, leading-zero padding applied~~
- ~~**Canada** — 6-character alphanumeric (`A1A1A1` pattern), case-insensitive~~

### ~~Project structure~~

~~Source files are organised into logical subfolders (`Core/`, `Normalizers/`, `Sources/`). `BuiltInCsvDataSource.cs` (legacy US-only parser) has been removed — replaced by `BuiltInDataSource.cs`.~~

### ~~ZipPostLookup.CountryDataTools~~

~~A companion developer CLI for preparing and maintaining country data files. Covers the full pipeline from raw GeoNames/OSM source files through to the optimised CSV and ZPI binary artifacts that the library embeds. `export --all` regenerates all three countries' CSV and ZPI artifacts in one command. See [ZipPostLookup.CountryDataTools/README.MD](ZipPostLookup.CountryDataTools/README.MD).~~

### ZPI Files

~~The library ships a pre-built binary frozen image (`.zpi.br`) alongside each country's CSV. Rather than parsing CSV rows and constructing `FrozenDictionary` indexes at startup, the image pre-computes a minimal perfect hash function (MPHF) over all postal codes and serialises the entire lookup structure into a single Brotli-compressed buffer — loading Canada's ~900k entries in 43 ms instead of ~15 seconds, using 7–10× less startup memory. The format is versioned, self-describing, and byte-consistent with the CSV it was built from; `ZpImageParityTests` enforces that both backends return identical results. The ZP image is available on .NET 8+ and is the recommended loading strategy for cold-start-sensitive deployments. See [ZpImageFormat.md](ZipPostLookup.CountryDataTools/Export/ZPimage/ZpImageFormat.md) for the full format specification.~~

### Curating OpenStreetMap data

OSM postal data is structurally messier than GeoNames — codes appear as node tags, way tags, and relation members, often duplicated, inconsistently formatted, or missing timezone and admin assignments entirely. The `CountryDataTools` pipeline handles this by ingesting a pre-filtered OSM export as a candidate CSV, running `validate` / `fix` / `extractissues` to normalise formatting and remove structurally broken rows, then comparing each record against curated reference data to surface timezone conflicts and unrecognised place names as discrepancies. Outstanding discrepancies are resolved via coordinate-based timezone lookup (GeoTimeZone) or the Zippopotam.us API, after which the country is flagged as curated and the export pipeline produces the final CSV and ZPI artifacts. See [ZipPostLookup.CountryDataTools/README.MD](ZipPostLookup.CountryDataTools/README.MD) for the full pipeline.

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
ZipEntry? entry   = ZipPostLookup.GetByCode("90210");
ZipEntry? same    = ZipPostLookup.GetByZip("90210");     // identical overload

if (ZipPostLookup.TryGetByCode(userInput, out var result))
    Console.WriteLine(result.Name);

IReadOnlyList<ZipEntry> all     = ZipPostLookup.GetAllByCode("00603"); // Aguadilla + Ramey
ZipEntry?               closest = ZipPostLookup.GetClosest("00001");
IReadOnlyList<ZipEntry> byName  = ZipPostLookup.GetByName("New York");
IReadOnlyList<ZipEntry> byState = ZipPostLookup.GetByState("CA");
IReadOnlyList<ZipEntry> byTz    = ZipPostLookup.GetByTimeZone("America/New_York");
ZipEntry random = ZipPostLookup.GetRandom();
ZipEntry seeded = ZipPostLookup.GetRandom(new Random(42));

// Canada
ZipRegistry ca      = ZipPostLookup.CreateRegistry(CountryCode.CA);
ZipEntry?   toronto = ca.GetByCode("M5V3L9");   // exact LDU
ZipEntry?   anyLdu  = ca.GetByCode("M5V5Z9");   // resolves via FSA range compression
IReadOnlyList<ZipEntry> ontario = ca.GetByState("ON");
IReadOnlyList<ZipEntry> bc      = ca.GetByStateName("British Columbia");

// Mexico
ZipRegistry mx  = ZipPostLookup.CreateRegistry(CountryCode.MX);
ZipEntry?   ags = mx.GetByCode("20000");   // Aguascalientes

// Multi-country (US + Canada in one registry)
ZipRegistry na       = ZipPostLookup.CreateRegistry(CountryCode.US, CountryCode.CA);
ZipEntry?   newYork  = na.GetByCode("10001");
ZipEntry?   montreal = na.GetByCode("H2X1Y6");

// Implicit string conversion
ZipRegistry na2 = ZipPostLookup.CreateRegistry("us", "ca");
```

---

## Dependency Injection

```csharp
// Program.cs
services.AddSingleton<IZipLookup>(
    _ => ZipPostLookup.CreateRegistry(CountryCode.US));

// Your service
public class AddressService
{
    private readonly IZipLookup _lookup;
    public AddressService(IZipLookup lookup) => _lookup = lookup;

    public string? GetTimezone(string postalCode) =>
        _lookup.GetByCode(postalCode)?.Timezone;
}
```

---

## Custom Data Sources

```csharp
public class MilitaryZipSource : IZipDataSource
{
    public string SourceName => "military-apo";

    public IEnumerable<ZipEntry> GetEntries()
    {
        yield return new ZipEntry(
            Code:      "09001",
            Name:      "APO",
            Timezone:  "Europe/Berlin",
            IsDefault: true,
            Admins:    new[] { new AdminLevel("Armed Forces Europe", "AE", "Territory") });
    }
}

var registry = ZipPostLookup.CreateCustomRegistry(new MilitaryZipSource());
var entry    = registry.GetByCode("09001");
```

---

## Performance

On .NET 8+, all indexes use `FrozenDictionary`. The `.zpi.br` frozen image loads 19–432x faster than the CSV registry at startup with 7–10x less memory:

| Country | CSV registry build | ZP image load | Speedup |
|---------|-------------------:|--------------:|--------:|
| US      |             189 ms |     **10 ms** |     19x |
| MX      |             654 ms |     **22 ms** |     30x |
| CA      |          17,844 ms |     **41 ms** |    432x |

`GetByCode` runs at ~6.2 ns (hit) / ~35 ns (miss) on .NET 8+ — 4.9x faster than Zip2City on hot-path lookups with zero allocations on hits. `GetBatch` is more efficient than a sequential loop when duplicate codes are expected; see [ZipPostLookup.Benchmarks/README.MD](ZipPostLookup.Benchmarks/README.MD) for full results including batch lookup guidance, international lookups, and ZPI vs CSV comparisons.

---

## Data

| Country |                                        Distinct Codes* | Admin Levels |                Timezones                | Status     |
|---------|-------------------------------------------------------:|:------------:|:---------------------------------------:|------------|
| US      |  **<!-- zpl:us:codes -->43,178<!-- /zpl:us:codes -->** |    State     | <!-- zpl:us:tz -->53<!-- /zpl:us:tz --> | ✅ Curated  |
| CA      | **<!-- zpl:ca:codes -->900,686<!-- /zpl:ca:codes -->** |   Province   | <!-- zpl:ca:tz -->21<!-- /zpl:ca:tz --> | ✅ Curated  |
| MX      |  **<!-- zpl:mx:codes -->32,629<!-- /zpl:mx:codes -->** |    Estado    | <!-- zpl:mx:tz -->10<!-- /zpl:mx:tz --> | ✅ Curated  |

\* Distinct codes are the number of unique Zip/Postal codes in the country's set. 

Code Counts and status, last updated on: **(<!-- zpl:updated -->2026-06-06 20:02:52<!-- /zpl:updated -->)**


---

## Contributing Data

New country data is prepared using the companion [ZipPostLookup.CountryDataTools](ZipPostLookup.CountryDataTools/README.MD) CLI. The general flow: obtain a GeoNames or OSM source file → validate and fix with `validate` / `fix` → import with `ingest candidate` → enrich timezone discrepancies → export with `export --all`. Run `snapshot` to regenerate all three countries' CSV and ZPI artifacts in one step. Mark exported files as `EmbeddedResource` in `ZipPostLookup.csproj` and add tests covering at minimum: a known code lookup, an admin/state lookup, a timezone lookup, and a `GetAll` count assertion.

---

## License

MIT
