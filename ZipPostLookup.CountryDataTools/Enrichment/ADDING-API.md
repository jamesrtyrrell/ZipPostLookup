# Adding a New Enrichment API

Reference guide for implementing a new enrichment data source in `ZipPostLookup.CountryDataTools`.
Follow these steps in order. Each section also includes an **AI prompt** you can paste directly
into a Claude Code session to complete that step.

---

## 1. Understand what the API returns

Before writing any code, answer these questions:

| Question | Why it matters |
|---|---|
| What countries does it support? | Sets `SupportedCountries` |
| Does it return a **place name** (city)? | Drives `PlaceName` in `ApiLookupResult` |
| Does it return **admin1** (state/province)? | Drives `Admin1Code` / `Admin1Name` |
| Does it return an **IANA timezone**? | Drives `Timezone`; if not, can we get lat/lon and derive it? |
| Does it return **coordinates** (lat/lon)? | Needed for offline GeoTimeZone resolution |
| **Key required?** | Determines how it's registered |
| **Limit type**: daily or monthly? | Sets `DailyLimit` vs `MonthlyLimit` |
| **Response format**: JSON or XML? | JSON → `ReadFromJsonAsync<JsonElement>`; XML → `XDocument.Parse` |
| What HTTP status signals rate-limit? | 429, or an in-body error code |
| What signals not-found? | 404, 200+empty, or in-body error code |

**AI prompt — research step:**
```
I'm adding a new enrichment API called {ApiName} to ZipPostLookup.CountryDataTools.
Read the HTML/docs I provide and extract:
- Endpoint URL and query parameters for a postal-code lookup
- Response shape (JSON or XML, field names for city, state, timezone, lat, lon)
- HTTP error codes and any in-body error codes that mean: rate-limited, not found, bad key
- Daily or monthly limit, and what the free-tier cap is
Report back in a concise table. Do not write any code yet.
```

---

## 2. Create the API class

**File**: `ZipPostLookup.CountryDataTools/Enrichment/Api/{Name}Api.cs`

### Required interface members

```csharp
internal sealed class {Name}Api : IEnrichmentApi
{
    public string               Name               => "{DisplayName}";
    public IReadOnlySet<string> SupportedCountries => _countries;  // e.g. { "US", "CA", "MX" }
    public int?                 DailyLimit         => _dailyLimit;  // null if no daily cap
    public int?                 MonthlyLimit       => null;         // or _monthlyLimit if monthly
}
```

### `LookupAsync` return values

| Situation | Return |
|---|---|
| Success — data found | `(result, FetchOutcome.Found)` |
| Postal code unknown to API | `(null, FetchOutcome.NotFound)` |
| 429 / quota error / bad key | `(null, FetchOutcome.RateLimited)` — router drops API from rotation |
| Network error / parse error / server 5xx | `(null, FetchOutcome.TransientError)` — code retried next run |

### `ApiLookupResult` fields

```csharp
new ApiLookupResult
{
    PlaceName  = "",        // city name, or "" to skip name update
    Admin1Code = "",        // state/province abbreviation, or "" to skip
    Admin1Name = "",        // full state/province name, or "" to skip
    Timezone   = iana,      // IANA string e.g. "America/New_York", or null to skip
    Lat        = lat,       // 0 if unavailable
    Lon        = lon,       // 0 if unavailable
}
```

Empty `PlaceName` and `Admin1Code` are handled by `UpdateReferenceAsync`:
the name and admin fields are skipped, only timezone and coordinates are written.

### JSON response (most APIs)

```csharp
using System.Net.Http.Json;
using System.Text.Json;

var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
var city = json.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
```

### XML response (e.g. Geocoder.ca)

```csharp
using System.Xml.Linq;

var xml  = await response.Content.ReadAsStringAsync(ct);
var root = XDocument.Parse(xml).Root; // root element e.g. <geodata>
var lat  = root?.Element("latt")?.Value;
```

### Catch block rule — MANDATORY

Every `try` block **must** end with two catches: one filtered, one generic fallback:

```csharp
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
{
    return (null, FetchOutcome.TransientError);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"[{Name}] Unexpected error for {code}: {ex.Message}");
    return (null, FetchOutcome.TransientError);
}
```

For XML APIs replace `JsonException` with `System.Xml.XmlException`.

### Timezone derivation from coordinates

If the API returns coordinates but no IANA timezone:

```csharp
using GeoTimeZone;

string? iana = null;
var tzResult = TimeZoneLookup.GetTimeZone(lat, lon).Result;
if (!string.IsNullOrWhiteSpace(tzResult) &&
    tzResult.Contains('/') &&
    tzResult != "Etc/Unknown")
    iana = tzResult;
```

### State/province resolution for US

```csharp
using ZipPostLookup.CountryDataTools.Pipeline;

var match = StateResolver.Resolve(rawStateCode) ?? StateResolver.Resolve(rawStateName);
var admin1Code = match?.StateCode ?? rawStateCode.ToUpperInvariant();
var admin1Name = match?.StateName ?? rawStateName;
```

**AI prompt — write the class:**
```
Add a new enrichment API called {Name} to ZipPostLookup.CountryDataTools.
Read the ADDING-API.md in ZipPostLookup.CountryDataTools/Enrichment/ for the implementation pattern.
The API details are:
- Endpoint: {url}
- Response format: {JSON|XML}
- Relevant fields: {field list}
- Countries: {US|CA|MX}
- Key required: {yes/no} — key name in apikeys.json: "{keyName}"
- Limit: {daily N | monthly N | none}
- Rate-limit signal: {HTTP 429 | body error code X}
- Not-found signal: {HTTP 404 | body error code Y | empty results array}
Create the file at Enrichment/Api/{Name}Api.cs following the pattern in OpenCageApi.cs.
```

---

## 3. Register in `EnrichmentApiFactory`

**File**: `ZipPostLookup.CountryDataTools/Enrichment/Api/EnrichmentApiFactory.cs`

### No key required

```csharp
var {instanceName} = new {Name}Api(http);
if ({instanceName}.SupportedCountries.Contains(country))
    result.Add({instanceName});
```

### Key required — daily limit

```csharp
var {entryName}Entry = apiKeys?.TryGetEntry("{keyJsonName}");
if ({entryName}Entry != null)
{
    if (!{entryName}Entry.IsConfigured)
        Console.Error.WriteLine("  ⚠  apikeys.json: {keyJsonName} key is not configured — replace the placeholder value.");
    else
    {
        var {instanceName} = new {Name}Api(http, {entryName}Entry.Key, {entryName}Entry.DailyLimit);
        if ({instanceName}.SupportedCountries.Contains(country))
            result.Add({instanceName});
    }
}
```

### Key required — monthly limit

Same as above but use `{entryName}Entry.MonthlyLimit` and pass as the `monthlyLimit` constructor arg.

**AI prompt:**
```
Register the new {Name}Api in EnrichmentApiFactory.GetApisForCountry.
It {does|does not} require an API key. Key name in apikeys.json: "{keyName}".
Limit type: {daily|monthly|none}.
Follow the pattern of the existing entries in that file.
```

---

## 4. Add the key to `apikeys.json` (key-required APIs only)

**File**: `apikeys.json` (solution root, gitignored)

```json
{
    "name": "{keyJsonName}",
    "dailyLimit": 2500,
    "key": "YOUR_{NAME}_KEY_HERE"
}
```

For monthly-limit APIs use `"monthlyLimit"` instead of `"dailyLimit"`.

Also add `MonthlyLimit` or `DailyLimit` to `ApiKeyEntry` in `ApiKeysConfig.cs` if a new field name is introduced — both properties already exist as of 2026-06-06.

---

## 5. Build and verify

```bash
dotnet build ZipPostLookup.CountryDataTools/ZipPostLookup.CountryDataTools.csproj -c Release --no-restore
```

Zero warnings, zero errors.

**AI prompt:**
```
Build ZipPostLookup.CountryDataTools and fix any compile errors introduced by the new API.
```

---

## 6. Update `.claude/PROJECTS-ONGOING.md`

Move the API from **APIs confirmed, pending research** (or Rejected) into the **Completed APIs** table.
Add the new row: `| {Name} | {Countries} | {key/no key} — {what it provides} |`

Also remove it from the **Rejected APIs** section if it was listed there.

---

## 7. Quick-smoke test (optional but recommended)

Run enrichment dry-run to confirm the API loads without crashing:

```bash
dotnet run --project ZipPostLookup.CountryDataTools -- enrichcandidates --country CA --limit 3 --dry-run
```

Then a live run with a tiny limit to check the actual HTTP call and response:

```bash
dotnet run --project ZipPostLookup.CountryDataTools -- enrichcandidates --country CA --limit 5
```

Inspect the "Period / Limit" column in the results table to confirm the new API name appears and
its call counter increments.

---

## Checklist

- [ ] API class created at `Enrichment/Api/{Name}Api.cs`
- [ ] Implements all 4 `IEnrichmentApi` members: `Name`, `SupportedCountries`, `DailyLimit`, `MonthlyLimit`
- [ ] `catch` block ends with a generic `catch (Exception ex)` fallback that logs and returns `TransientError`
- [ ] Rate-limit signal returns `FetchOutcome.RateLimited`
- [ ] Not-found signal returns `FetchOutcome.NotFound`
- [ ] Registered in `EnrichmentApiFactory.GetApisForCountry`
- [ ] Key added to `apikeys.json` (if applicable)
- [ ] Build passes with 0 warnings
- [ ] `.claude/PROJECTS-ONGOING.md` Completed APIs table updated
