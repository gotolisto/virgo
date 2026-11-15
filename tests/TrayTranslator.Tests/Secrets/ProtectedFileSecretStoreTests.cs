using TrayTranslator.Core.Secrets;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Secrets;

public sealed class ProtectedFileSecretStoreTests : IDisposable
{
    private const string SampleKey = "abc123-sample-key:fx";
    private readonly TempDirectory _dir = new();
    private readonly FakeProtector _protector = new();

    public void Dispose() => _dir.Dispose();

    private ProtectedFileSecretStore NewStore() => new(_dir.File("keys.dat"), _protector);

    [Fact]
    public void Missing_value_is_null()
    {
        Assert.Null(NewStore().Get(SecretNames.DeepLApiKey));
    }

    [Fact]
    public void Set_then_get_round_trips_across_instances()
    {
        NewStore().Set(SecretNames.DeepLApiKey, SampleKey);
        NewStore().Set(SecretNames.GoogleApiKey, "google-sample");

        Assert.Equal(SampleKey, NewStore().Get(SecretNames.DeepLApiKey));
        Assert.Equal("google-sample", NewStore().Get(SecretNames.GoogleApiKey));
    }

    [Fact]
    public void Value_is_trimmed()
    {
        var store = NewStore();
        store.Set(SecretNames.DeepLApiKey, "  " + SampleKey + "\n");
        Assert.Equal(SampleKey, store.Get(SecretNames.DeepLApiKey));
    }

    [Fact]
    public void Plaintext_never_reaches_the_file()
    {
        NewStore().Set(SecretNames.DeepLApiKey, SampleKey);

        var raw = File.ReadAllText(_dir.File("keys.dat"));
        Assert.DoesNotContain(SampleKey, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", raw, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_value_removes_the_secret(string? empty)
    {
        var store = NewStore();
        store.Set(SecretNames.DeepLApiKey, SampleKey);
        store.Set(SecretNames.DeepLApiKey, empty);

        Assert.Null(NewStore().Get(SecretNames.DeepLApiKey));
    }

    [Fact]
    public void Undecryptable_value_reads_as_missing()
    {
        NewStore().Set(SecretNames.DeepLApiKey, SampleKey);
        _protector.FailUnprotect = true;

        Assert.Null(NewStore().Get(SecretNames.DeepLApiKey));
    }

    [Fact]
    public void Damaged_file_reads_as_missing_and_can_be_overwritten()
    {
        File.WriteAllText(_dir.File("keys.dat"), "garbage");
        var store = NewStore();

        Assert.Null(store.Get(SecretNames.DeepLApiKey));
        store.Set(SecretNames.DeepLApiKey, SampleKey);
        Assert.Equal(SampleKey, store.Get(SecretNames.DeepLApiKey));
    }
}
