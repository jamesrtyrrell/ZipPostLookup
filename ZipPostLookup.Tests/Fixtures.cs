using ZipPostLookup.Core;

namespace ZipPostLookup.Tests;

/// <summary>
/// Loads the US registry once per test class — avoids re-parsing 56k rows per test.
/// </summary>
public sealed class UsRegistryFixture : IDisposable
{
    public ZipPostRegistry PostRegistry { get; } = new ZipPostRegistry(CountryCode.US);
    public void Dispose() { }
}

/// <summary>
/// Loads the CA registry once per test class — avoids re-parsing 891k rows per test.
/// </summary>
public sealed class CaRegistryFixture : IDisposable
{
    public ZipPostRegistry PostRegistry { get; } = new ZipPostRegistry(CountryCode.CA);
    public void Dispose() { }
}

/// <summary>
/// Loads the combined US + CA registry once per test class.
/// </summary>
public sealed class NorthAmericaRegistryFixture : IDisposable
{
    public ZipPostRegistry PostRegistry { get; } = new ZipPostRegistry(CountryCode.US, CountryCode.CA);
    public void Dispose() { }
}

/// <summary>
/// Loads the MX registry once per test class.
/// </summary>
public sealed class MxRegistryFixture : IDisposable
{
    public ZipPostRegistry PostRegistry { get; } = new ZipPostRegistry(CountryCode.MX);
    public void Dispose() { }
}