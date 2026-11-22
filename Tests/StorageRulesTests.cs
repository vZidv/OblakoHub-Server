using System.Text.Json;
using Xunit;

public sealed class StorageRulesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("OblakoHub-server-tests-").FullName;

    [Theory]
    [InlineData("../outside")]
    [InlineData("sub/../outside")]
    [InlineData("/file")]
    [InlineData("folder//file")]
    [InlineData("folder\\file")]
    [InlineData("file.")]
    [InlineData("file ")]
    public void ResolveRejectsInvalidPaths(string path) =>
        Assert.Throws<ArgumentException>(() => StorageRules.Resolve(_folder, path));

    [Fact]
    public void ResolveAcceptsNestedPathAndExplicitRoot()
    {
        Assert.Equal(Path.Combine(_folder, "photos", "pic.png"), StorageRules.Resolve(_folder, "photos/pic.png"));
        Assert.Equal(_folder, StorageRules.Resolve(_folder, "", allowRoot: true));
        Assert.Throws<ArgumentException>(() => StorageRules.Resolve(_folder, ""));
    }

    [Fact]
    public void PairingRequiresCorrectUnexpiredCodeAndUnpairedServer()
    {
        var now = DateTimeOffset.UnixEpoch;
        var hash = StorageRules.Hash("code");
        Assert.True(StorageRules.CanPair(null, now, now, hash, "code"));
        Assert.False(StorageRules.CanPair(null, now.AddTicks(1), now, hash, "code"));
        Assert.False(StorageRules.CanPair(null, now, now, hash, "wrong"));
        Assert.False(StorageRules.CanPair(null, now, now, hash, null));
        Assert.False(StorageRules.CanPair(StorageRules.Hash("token"), now, now, hash, "code"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0L, false)]
    [InlineData(19L, false)]
    [InlineData(20L, true)]
    [InlineData(50L, true)]
    [InlineData(51L, false)]
    public void PairingQuotaMustCoverUsedBytesAndFitOnDisk(long? quota, bool valid) =>
        Assert.Equal(valid, StorageRules.IsPairingQuotaValid(quota, new StorageUsage(100, 30, 20, null)));

    [Fact]
    public void ReadsLegacyTokenHash()
    {
        var path = Path.Combine(_folder, "credentials.json");
        var hash = StorageRules.Hash("token");
        File.WriteAllText(path, JsonSerializer.Serialize(hash));

        Assert.Equal(new StoredCredentials(hash, null), StorageRules.ReadCredentials(path));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"short\"")]
    public void ExistingInvalidCredentialsDoNotEnablePairing(string contents)
    {
        var path = Path.Combine(_folder, "credentials.json");
        File.WriteAllText(path, contents);
        Assert.Throws<InvalidDataException>(() => StorageRules.ReadCredentials(path));
    }

    [Fact]
    public void MissingCredentialsRepresentFreshServer() =>
        Assert.Null(StorageRules.ReadCredentials(Path.Combine(_folder, "credentials.json")));

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
