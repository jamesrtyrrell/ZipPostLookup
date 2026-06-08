# ZipPostLookup

Fast, extensible in-memory postal code lookup for .NET.  
Built-in data for the **United States** (<!-- zpl:us:codes -->43,498<!-- /zpl:us:codes --> ZIP codes), **Canada** (~<!-- zpl:ca:codes -->900,686<!-- /zpl:ca:codes --> postal codes, <!-- zpl:ca:entries -->431k<!-- /zpl:ca:entries --> compressed entries via FSA range compression), and **Mexico** (<!-- zpl:mx:codes -->32,893<!-- /zpl:mx:codes --> postal codes). All three countries are fully curated ✅.

Code Counts and status, last updated on: **(<!-- zpl:updated -->2026-06-08 02:24:17<!-- /zpl:updated -->)**

---

## Installation

```bash
dotnet add package ZipPostLookup
```
### Legacy Systems 
For netstandard2.0: Covering .NET Framework 4.6.1+ / .NET Core 2+ / .NET 5–7 

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

| Method                          | Returns                                    | Description                                                                |
|---------------------------------|--------------------------------------------|----------------------------------------------------------------------------|
| `GetByCode(code)`               | `CodeEntry?`                               | Default entry for a postal code, or `null` if not found                    |
| `GetByZip(zip)`                 | `CodeEntry?`                               | Overload — identical to `GetByCode`                                        |
| `TryGetByCode(code, out entry)` | `bool`                                     | Try-Parse pattern — preferred for user-supplied input                      |
| `TryGetByZip(zip, out entry)`   | `bool`                                     | Overload — identical to `TryGetByCode`                                     |
| `GetAllByCode(code)`            | `IReadOnlyList<CodeEntry>`                 | All entries for a postal code (some codes have multiple place names)       |
| `GetAllByZip(zip)`              | `IReadOnlyList<CodeEntry>`                 | Overload — identical to `GetAllByCode`                                     |
| `GetClosest(zip)`               | `CodeEntry?`                               | Nearest zip by numeric distance when no exact match exists                 |
| `GetByName(name)`               | `IReadOnlyList<CodeEntry>`                 | All entries for a place name (case-insensitive)                            |
| `GetByCity(city)`               | `IReadOnlyList<CodeEntry>`                 | Overload — identical to `GetByName`                                        |
| `GetByState(code)`              | `IReadOnlyList<CodeEntry>`                 | All entries for an admin level 1 code (e.g. `"NY"`, `"ON"`)               |
| `GetByStateName(name)`          | `IReadOnlyList<CodeEntry>`                 | All entries for an admin level 1 name                                      |
| `GetByAdmin(value, levelName)`  | `IReadOnlyList<CodeEntry>`                 | All entries for a named admin level — resolves aliases from country schema |
| `GetByTimeZone(tz)`             | `IReadOnlyList<CodeEntry>`                 | All entries for an IANA timezone identifier                                |
| `GetBatch(batch)`               | `IReadOnlyDictionary<string, CodeEntry?>` | Look up multiple codes at once; efficient when duplicates are expected     |
| `GetRandom()`                   | `CodeEntry`                                | Random entry using `Random.Shared`                                         |
| `GetRandom(random)`             | `CodeEntry`                                | Random entry using a provided `Random` instance                            |
| `GetAll()`                      | `IReadOnlyList<CodeEntry>`                 | All entries sorted by postal code                                          |
| `GetCountryInfo(country)`       | `CountryInfo?`                             | Country metadata (code count, curation status, format rules)               |

---

## CodeEntry Properties

| Property              | Type             | Description                                                          |
|-----------------------|------------------|----------------------------------------------------------------------|
| `ZpCode`              | `string`         | Postal code — e.g. `"10001"` (US), `"M5V3L9"` (CA), `"06600"` (MX) |
| `PlaceName`           | `string`         | Primary place name — city, locality, colonia, or community           |
| `Timezone`            | `string`         | IANA timezone identifier                                             |
| `IsDefault`           | `bool`           | `true` if primary entry when multiple place names share a code       |
| `Admins`              | `AdminLevel[]`   | Administrative subdivision levels — ordered largest to smallest      |
| `Admin1`              | `string?`        | Value of the first admin level (state/province/estado name)          |
| `Admin1Code`          | `string?`        | Code of the first admin level (`"NY"`, `"ON"`, `"JAL"`)              |
| `Admin2`              | `string?`        | Value of the second admin level (county, municipio, etc.)            |
| `Admin2Code`          | `string?`        | Code of the second admin level                                       |
| `Admin3`              | `string?`        | Value of the third admin level                                       |
| `Admin3Code`          | `string?`        | Code of the third admin level                                        |
| `GetAdmin(levelName)` | `AdminLevel?`    | Lookup by level name or alias, e.g. `GetAdmin("Province")`           |
| `GetLocalTime()`      | `DateTimeOffset` | Current local time in this entry's timezone                          |

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

## Binary Image Lookup (net8.0+)

On .NET 8 and later, `ZpImageLookup` loads a pre-built Brotli-compressed binary image instead of parsing CSV rows. It implements `IZipPostLookup` and is a drop-in replacement with 19–432× faster startup and 7–10× less startup memory.

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
Console.WriteLine(us?.CodeCount);       // 43265
Console.WriteLine(us?.CurationStatus); // Curated

// All countries in the embedded data
foreach (var (id, info) in CountryInfoRegistry.All)
    Console.WriteLine(info); // e.g. "US — United States (43,265 codes, curated)"
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
| US      |  **<!-- zpl:us:codes -->43,498<!-- /zpl:us:codes -->** |    State     | <!-- zpl:us:tz -->53<!-- /zpl:us:tz --> | ✅ Curated  |
| CA      | **<!-- zpl:ca:codes -->900,686<!-- /zpl:ca:codes -->** |   Province   | <!-- zpl:ca:tz -->20<!-- /zpl:ca:tz --> | ✅ Curated  |
| MX      |  **<!-- zpl:mx:codes -->32,893<!-- /zpl:mx:codes -->** |    Estado    | <!-- zpl:mx:tz -->17<!-- /zpl:mx:tz --> | ✅ Curated  |

\* Distinct codes are the number of unique Zip/Postal codes in the country's set.

Code Counts and status, last updated on: **(<!-- zpl:updated -->2026-06-08 02:24:17<!-- /zpl:updated -->)**

---

## Contributing Data

New country data is prepared using the companion [ZipPostLookup.CountryDataTools](ZipPostLookup.CountryDataTools/README.MD) CLI. The general flow: obtain a GeoNames or OSM source file → validate and fix with `validate` / `fix` → import with `ingest candidate` → enrich timezone discrepancies → export with `export --all`. Run `snapshot` to regenerate all three countries' CSV and ZPI artifacts in one step. Mark exported files as `EmbeddedResource` in `ZipPostLookup.csproj` and add tests covering at minimum: a known code lookup, an admin/state lookup, a timezone lookup, and a `GetAll` count assertion.

---

## License

MIT
