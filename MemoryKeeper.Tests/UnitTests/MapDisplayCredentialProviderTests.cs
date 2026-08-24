using MemoryKeeper.Application;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MapDisplayCredentialProviderTests
{
    [Fact]
    public async Task Deployment_File_Is_Read_Automatically_Without_User_Setting()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"memorykeeper-map-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const string deploymentKey = "AIzaDeploymentCredentialForUnitTest12345";
            await File.WriteAllTextAsync(
                Path.Combine(directory, MapDisplayCredentialProvider.DeploymentFileName),
                deploymentKey);
            var settings = new FixedSettings("AIzaLegacyCredentialForUnitTest123456789");

            var resolved = await MapDisplayCredentialProvider.GetAsync(
                settings,
                deploymentDirectory: directory);

            Assert.Equal(deploymentKey, resolved);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_Deployment_File_Allows_Lower_Priority_Fallbacks()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"memorykeeper-map-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Null(MapDisplayCredentialProvider.ReadDeploymentCredential(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Ignored_Development_LocalEnv_Is_Read_Without_User_Setting()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"memorykeeper-map-test-{Guid.NewGuid():N}");
        var configDirectory = Path.Combine(directory, "config");
        Directory.CreateDirectory(configDirectory);
        try
        {
            const string developmentKey = "AIzaDevelopmentCredentialForUnitTest12345";
            await File.WriteAllTextAsync(
                Path.Combine(configDirectory, MapDisplayCredentialProvider.DevelopmentConfigurationFileName),
                $"# local only{Environment.NewLine}{MapDisplayCredentialProvider.EnvironmentVariable}='{developmentKey}'");

            var resolved = MapDisplayCredentialProvider.ReadDevelopmentCredential(
                Path.Combine(directory, "bin", "x64", "Debug"));

            Assert.Equal(developmentKey, resolved);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedSettings : ISettingRepository
    {
        private readonly string? _legacyKey;

        public FixedSettings(string? legacyKey) => _legacyKey = legacyKey;

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(key == SettingKeys.GoogleMapsApiKey && _legacyKey is not null
                ? new Setting { Id = Guid.NewGuid(), Key = key, Value = _legacyKey }
                : null);

        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Setting?>(null);

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Setting>>([]);

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
