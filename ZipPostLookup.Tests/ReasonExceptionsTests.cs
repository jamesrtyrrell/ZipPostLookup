using Xunit;
using ZipPostLookup.Core;
using ZipPostLookup.ZPImage;
using Facade = ZipPostLookup.ZipPostLookup;

namespace ZipPostLookup.Tests;

/// <summary>
/// Phase 1 of "Code Reason Flags &amp; Lookup Exceptions" — the opt-in
/// <see cref="BadlyFormattedCodeException"/> on the code-lookup path. Verifies the toggle is
/// off by default (behaviour unchanged) and that, when on, malformed input throws while a
/// well-formed-but-missing code still returns a silent miss.
/// </summary>
public sealed class ReasonExceptionsTests : IClassFixture<UsRegistryFixture>
{
    private readonly ZipPostRegistry _registry;

    // Not a valid format for US (5 digits), CA (A1A1A1), or MX (5 digits) → "badly formatted".
    private const string Malformed = "NOTAZIP";
    // Valid CA format (A1A1A1) but absent from the US-only registry → well-formed miss.
    private const string WellFormedMissing = "X0X0X0";

    public ReasonExceptionsTests(UsRegistryFixture fixture)
    {
        _registry = fixture.PostRegistry;
    }

    // ── Default (toggle off) — behaviour unchanged ──────────────────────────────

    [Fact]
    public void Default_MalformedCode_ReturnsNull_DoesNotThrow()
    {
        Assert.Null(_registry.GetByCode(Malformed));
        Assert.Empty(_registry.GetAllByCode(Malformed));
    }

    // ── Toggle on — malformed input throws ──────────────────────────────────────

    [Fact]
    public void ThrowEnabled_GetByCode_MalformedCode_Throws()
    {
        try
        {
            _registry.ThrowReasonExceptions(true);

            var ex = Assert.Throws<BadlyFormattedCodeException>(() => _registry.GetByCode(Malformed));
            Assert.Equal(Malformed, ex.Code);
            Assert.IsAssignableFrom<CodeReasonException>(ex);
        }
        finally
        {
            _registry.ThrowReasonExceptions(false);
        }
    }

    [Fact]
    public void ThrowEnabled_GetAllByCode_MalformedCode_Throws()
    {
        try
        {
            _registry.ThrowReasonExceptions(true);
            Assert.Throws<BadlyFormattedCodeException>(() => _registry.GetAllByCode(Malformed));
        }
        finally
        {
            _registry.ThrowReasonExceptions(false);
        }
    }

    [Fact]
    public void ThrowEnabled_GetByZipAlias_MalformedCode_Throws()
    {
        try
        {
            _registry.ThrowReasonExceptions(true);
            Assert.Throws<BadlyFormattedCodeException>(() => _registry.GetByZip(Malformed));
        }
        finally
        {
            _registry.ThrowReasonExceptions(false);
        }
    }

    // ── Toggle on — well-formed input never throws ──────────────────────────────

    [Fact]
    public void ThrowEnabled_WellFormedButMissing_ReturnsNull_NoThrow()
    {
        try
        {
            _registry.ThrowReasonExceptions(true);
            Assert.Null(_registry.GetByCode(WellFormedMissing));
            Assert.Empty(_registry.GetAllByCode(WellFormedMissing));
        }
        finally
        {
            _registry.ThrowReasonExceptions(false);
        }
    }

    [Fact]
    public void ThrowEnabled_FoundCode_ReturnsEntry_NoThrow()
    {
        try
        {
            _registry.ThrowReasonExceptions(true);
            var entry = _registry.GetByCode("10001");
            Assert.NotNull(entry);
            Assert.Equal("10001", entry!.ZpCode);
        }
        finally
        {
            _registry.ThrowReasonExceptions(false);
        }
    }

    [Fact]
    public void ThrowEnabled_NullCode_ReturnsNull_NoThrow()
    {
        try
        {
            _registry.ThrowReasonExceptions(true);
            Assert.Null(_registry.GetByCode(null));
        }
        finally
        {
            _registry.ThrowReasonExceptions(false);
        }
    }

    [Fact]
    public void Toggle_CanBeTurnedBackOff()
    {
        _registry.ThrowReasonExceptions(true);
        _registry.ThrowReasonExceptions(false);

        // Back to silent-miss behaviour.
        Assert.Null(_registry.GetByCode(Malformed));
    }

    // ── ZP image parity — same toggle semantics ─────────────────────────────────

    [Fact]
    public void Image_ThrowEnabled_MatchesRegistrySemantics()
    {
        var image = ZpImageLookup.FromBuiltIn(CountryCode.US);
        image.ThrowReasonExceptions(true);

        Assert.Throws<BadlyFormattedCodeException>(() => image.GetByCode(Malformed));
        Assert.Throws<BadlyFormattedCodeException>(() => image.GetAllByCode(Malformed));
        Assert.Null(image.GetByCode(WellFormedMissing));   // well-formed miss
        Assert.NotNull(image.GetByCode("10001"));           // found
    }

    [Fact]
    public void Image_Default_MalformedCode_ReturnsNull()
    {
        var image = ZpImageLookup.FromBuiltIn(CountryCode.US);
        Assert.Null(image.GetByCode(Malformed));
    }

    // ── Static facade delegates to the default registry ─────────────────────────

    [Fact]
    public void Facade_ThrowEnabled_MalformedCode_Throws()
    {
        try
        {
            Facade.ThrowReasonExceptions(true);
            var ex = Assert.Throws<BadlyFormattedCodeException>(() => Facade.GetByCode(Malformed));
            Assert.Equal(Malformed, ex.Code);
        }
        finally
        {
            Facade.ThrowReasonExceptions(false);
        }
    }

    // ── The Phase-2 reason exception types exist and carry the code ──────────────

    [Fact]
    public void Phase2_ReasonExceptionTypes_ExistAndCarryCode()
    {
        var obsolete = new ObsoleteCodeException("12345");
        var fake = new CommonFakeDataException("90210");

        Assert.Equal("12345", obsolete.Code);
        Assert.Equal("90210", fake.Code);
        Assert.IsAssignableFrom<CodeReasonException>(obsolete);
        Assert.IsAssignableFrom<CodeReasonException>(fake);
    }
}
