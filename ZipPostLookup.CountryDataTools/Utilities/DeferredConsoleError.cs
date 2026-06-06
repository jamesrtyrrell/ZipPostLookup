namespace ZipPostLookup.CountryDataTools.Utilities;

/// <summary>
/// Redirects Console.Error to an in-memory buffer for the lifetime of this object.
/// On Dispose, restores the original error stream and flushes any buffered content
/// to it. Use this around Spectre Console Live blocks to prevent error writes from
/// corrupting the Live display's cursor-position tracking.
///
/// Usage:
///   using var deferred = new DeferredConsoleError();
///   await AnsiConsole.Live(...).StartAsync(...);
///   // deferred.Dispose() here — buffered errors appear after the Live block
/// </summary>
internal sealed class DeferredConsoleError : IDisposable
{
    private readonly TextWriter  _original;
    private readonly StringWriter _buffer = new();
    private bool _disposed;

    public DeferredConsoleError()
    {
        _original = Console.Error;
        Console.SetError(_buffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Console.SetError(_original);

        var content = _buffer.ToString();
        if (!string.IsNullOrWhiteSpace(content))
            _original.Write(content);

        _buffer.Dispose();
    }
}
