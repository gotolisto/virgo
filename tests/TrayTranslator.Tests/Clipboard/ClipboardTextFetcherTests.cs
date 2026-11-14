using TrayTranslator.Core.Clipboard;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.Tests.Clipboard;

public class ClipboardTextFetcherTests
{
    private sealed class ScriptedClipboard(params ClipboardReadResult[] results) : IClipboardReader
    {
        private int _next;

        public int Attempts => _next;

        public Task<ClipboardReadResult> TryReadTextAsync(CancellationToken cancellationToken) =>
            Task.FromResult(results[Math.Min(_next++, results.Length - 1)]);
    }

    private static ClipboardTextFetcher Fetcher(IClipboardReader reader) =>
        new(reader) { InitialDelay = TimeSpan.Zero, RetryDelay = TimeSpan.Zero };

    [Fact]
    public async Task Returns_text()
    {
        var clipboard = new ScriptedClipboard(ClipboardReadResult.FromText("hola"));
        Assert.Equal("hola", await Fetcher(clipboard).FetchAsync(CancellationToken.None));
        Assert.Equal(1, clipboard.Attempts);
    }

    [Fact]
    public async Task No_text_gives_null_without_retrying()
    {
        var clipboard = new ScriptedClipboard(ClipboardReadResult.NoText);
        Assert.Null(await Fetcher(clipboard).FetchAsync(CancellationToken.None));
        Assert.Equal(1, clipboard.Attempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    public async Task Whitespace_only_counts_as_no_text(string text)
    {
        Assert.Null(await Fetcher(new ScriptedClipboard(ClipboardReadResult.FromText(text))).FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Retries_while_locked()
    {
        var clipboard = new ScriptedClipboard(
            ClipboardReadResult.Locked, ClipboardReadResult.Locked, ClipboardReadResult.FromText("hola"));

        Assert.Equal("hola", await Fetcher(clipboard).FetchAsync(CancellationToken.None));
        Assert.Equal(3, clipboard.Attempts);
    }

    [Fact]
    public async Task Gives_up_after_five_retries()
    {
        var clipboard = new ScriptedClipboard(ClipboardReadResult.Locked);

        Assert.Null(await Fetcher(clipboard).FetchAsync(CancellationToken.None));
        Assert.Equal(1 + ClipboardTextFetcher.DefaultMaxRetries, clipboard.Attempts);
    }

    [Fact]
    public async Task Succeeds_on_the_last_retry()
    {
        var clipboard = new ScriptedClipboard(
            ClipboardReadResult.Locked, ClipboardReadResult.Locked, ClipboardReadResult.Locked,
            ClipboardReadResult.Locked, ClipboardReadResult.Locked, ClipboardReadResult.FromText("ok"));

        Assert.Equal("ok", await Fetcher(clipboard).FetchAsync(CancellationToken.None));
    }

    [Fact]
    public void Defaults_match_the_spec()
    {
        var fetcher = new ClipboardTextFetcher(new ScriptedClipboard(ClipboardReadResult.NoText));
        Assert.Equal(TimeSpan.FromMilliseconds(50), fetcher.InitialDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(30), fetcher.RetryDelay);
        Assert.Equal(5, fetcher.MaxRetries);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var fetcher = new ClipboardTextFetcher(new ScriptedClipboard(ClipboardReadResult.FromText("x")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync(cts.Token));
    }
}
