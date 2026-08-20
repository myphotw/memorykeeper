using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class SetupWizardServiceTests
{
    [Fact]
    public async Task Setup_Does_Not_Require_Photo_Storage_Or_Google_Api_Key()
    {
        var settings = new InMemorySettingRepository();
        var resolver = new StubLocationResolver();
        var home = new HomeLocationService(settings, resolver, NullLogger<HomeLocationService>.Instance);
        var setup = new SetupWizardService(home, settings, NullLogger<SetupWizardService>.Instance);

        var selected = await home.SavePlaceSelectionAsync("home-place");
        await setup.MarkSetupCompletedAsync();
        var status = await setup.GetStatusAsync();

        Assert.True(selected.IsConfigured);
        Assert.False(status.NeedsSetup);
        Assert.True(status.HasHomeLocation);
        Assert.True(status.IsSetupMarkedComplete);
        Assert.Null(await settings.GetByKeyAsync(SettingKeys.GoogleMapsApiKey));
        Assert.DoesNotContain(settings.Values.Keys, key => key.Contains("Storage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Selected_Home_Is_Saved_And_Reloaded()
    {
        var settings = new InMemorySettingRepository();
        var resolver = new StubLocationResolver();
        var first = new HomeLocationService(settings, resolver, NullLogger<HomeLocationService>.Instance);

        await first.SavePlaceSelectionAsync("home-place");
        var reloaded = await new HomeLocationService(
            settings,
            resolver,
            NullLogger<HomeLocationService>.Instance).GetAsync();

        Assert.Equal(37.5012, reloaded.Latitude);
        Assert.Equal(126.8822, reloaded.Longitude);
        Assert.Equal("서울특별시 구로구 테스트로 1", reloaded.Address);
        Assert.Equal("home-place", reloaded.PlaceId);
    }

    [Fact]
    public async Task Existing_Legacy_Api_Key_Does_Not_Block_Or_Get_Overwritten()
    {
        var settings = new InMemorySettingRepository(new Dictionary<string, string>
        {
            [SettingKeys.GoogleMapsApiKey] = "legacy-value",
            [SettingKeys.TravelHomeLatitude] = "37.5012",
            [SettingKeys.TravelHomeLongitude] = "126.8822",
            [SettingKeys.TravelHomeAddress] = "기존 집 주소",
            [SettingKeys.SetupCompleted] = "true",
        });
        var home = new HomeLocationService(settings, new StubLocationResolver(), NullLogger<HomeLocationService>.Instance);
        var setup = new SetupWizardService(home, settings, NullLogger<SetupWizardService>.Instance);

        var status = await setup.GetStatusAsync();

        Assert.False(status.NeedsSetup);
        Assert.Equal("legacy-value", settings.Values[SettingKeys.GoogleMapsApiKey]);
    }

    [Fact]
    public async Task Completed_Flag_Without_Home_Still_Requests_Minimal_Setup()
    {
        var settings = new InMemorySettingRepository(new Dictionary<string, string>
        {
            [SettingKeys.SetupCompleted] = "true",
        });
        var home = new HomeLocationService(settings, new StubLocationResolver(), NullLogger<HomeLocationService>.Instance);
        var setup = new SetupWizardService(home, settings, NullLogger<SetupWizardService>.Instance);

        var status = await setup.GetStatusAsync();

        Assert.True(status.NeedsSetup);
        Assert.False(status.HasHomeLocation);
    }

    private sealed class StubLocationResolver : ILocationResolver
    {
        public Task<LocationResult?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocationResult?>(null);

        public Task<LocationResult?> ResolveAddressAsync(string address, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocationResult?>(new LocationResult
            {
                Address = address,
                DisplayName = address,
                Latitude = 37.5012,
                Longitude = 126.8822,
            });

        public Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(string input, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceSuggestionDto>>
            ([
                new PlaceSuggestionDto
                {
                    PlaceId = "home-place",
                    PrimaryText = "테스트 집",
                    SecondaryText = "서울특별시 구로구",
                    Description = "테스트 집, 서울특별시 구로구",
                },
            ]);

        public Task<LocationResult?> ResolvePlaceIdAsync(string placeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocationResult?>(new LocationResult
            {
                PlaceId = placeId,
                Address = "서울특별시 구로구 테스트로 1",
                DisplayName = "테스트 집",
                Latitude = 37.5012,
                Longitude = 126.8822,
            });

        public Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
            double latitude,
            double longitude,
            int maxResults = 5,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NearbyPlaceCandidateDto>>([]);
    }

    private sealed class InMemorySettingRepository : ISettingRepository
    {
        public Dictionary<string, string> Values { get; }

        public InMemorySettingRepository(Dictionary<string, string>? values = null)
        {
            Values = values ?? new Dictionary<string, string>();
        }

        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Setting?>(null);

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!Values.TryGetValue(key, out var value))
            {
                return Task.FromResult<Setting?>(null);
            }

            return Task.FromResult<Setting?>(Create(key, value));
        }

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Setting>>(Values.Select(pair => Create(pair.Key, pair.Value)).ToList());

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            Values[setting.Key] = setting.Value;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            Values[setting.Key] = setting.Value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            Values.Remove(setting.Key);
            return Task.CompletedTask;
        }

        private static Setting Create(string key, string value) => new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            Value = value,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }
}
