using GitHubGoal.Core.Models;
using GitHubGoal.Core.Services;
using Xunit;

namespace GitHubGoal.Core.Tests;

public sealed class EntitlementServiceTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(
        Path.GetTempPath(), $"ghg-plan-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }

    // --- tier ordering ----------------------------------------------------

    [Fact]
    public void Tiers_are_ordered_so_they_can_be_compared()
    {
        Assert.True(SubscriptionPlan.Free < SubscriptionPlan.Plus);
        Assert.True(SubscriptionPlan.Plus < SubscriptionPlan.Pro);
    }

    [Fact]
    public void Tier_values_leave_room_for_a_tier_in_between()
    {
        // Renumbering would break every already-persisted settings file.
        Assert.True((int)SubscriptionPlan.Plus - (int)SubscriptionPlan.Free > 1);
        Assert.True((int)SubscriptionPlan.Pro - (int)SubscriptionPlan.Plus > 1);
    }

    // --- feature matrix ---------------------------------------------------

    [Fact]
    public void Ungated_features_are_available_on_every_tier()
    {
        // Nothing is gated yet, so a capability nobody has priced must not be locked.
        var undeclared = (PlanFeature)9999;

        Assert.Equal(SubscriptionPlan.Free, FeatureMatrix.MinimumPlan(undeclared));
        Assert.True(FeatureMatrix.Includes(SubscriptionPlan.Free, undeclared));
    }

    [Fact]
    public void Nothing_is_gated_yet()
    {
        Assert.Empty(FeatureMatrix.Gated);
    }

    // --- plan resolution --------------------------------------------------

    [Fact]
    public void Starts_on_Free_when_there_is_no_licence()
    {
        var service = CreateService(out _, out _);

        Assert.Equal(SubscriptionPlan.Free, service.CurrentPlan);
    }

    [Fact]
    public void Restores_the_cached_tier_before_any_validation_runs()
    {
        var settings = new SettingsService(_settingsPath);
        settings.Load();
        var saved = settings.Current;
        saved.CachedPlan = SubscriptionPlan.Pro;
        settings.Save(saved);

        var service = new EntitlementService(
            new FakeLicenseStore(),
            new FakeValidator(License.Free),
            settings);

        Assert.Equal(SubscriptionPlan.Pro, service.CurrentPlan);
    }

    [Fact]
    public void Expired_cached_tier_falls_back_to_Free()
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        var settings = new SettingsService(_settingsPath);
        settings.Load();
        var saved = settings.Current;
        saved.CachedPlan = SubscriptionPlan.Pro;
        saved.CachedPlanExpiresAt = now.AddDays(-1);
        settings.Save(saved);

        var service = new EntitlementService(
            new FakeLicenseStore(),
            new FakeValidator(License.Free),
            settings,
            new FakeTime(now));

        Assert.Equal(SubscriptionPlan.Free, service.CurrentPlan);
    }

    [Fact]
    public async Task Activating_a_valid_key_raises_the_tier_and_stores_the_key()
    {
        var service = CreateService(out var store, out _, new License(SubscriptionPlan.Plus, "KEY", null));

        var plan = await service.ActivateAsync("  KEY  ");

        Assert.Equal(SubscriptionPlan.Plus, plan);
        Assert.Equal(SubscriptionPlan.Plus, service.CurrentPlan);

        // Trimmed, so a pasted key with stray whitespace still matches on next launch.
        Assert.Equal("KEY", store.Saved);
    }

    [Fact]
    public async Task A_rejected_key_is_not_stored()
    {
        var service = CreateService(out var store, out _, License.Free);

        var plan = await service.ActivateAsync("nonsense");

        Assert.Equal(SubscriptionPlan.Free, plan);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task Activating_persists_the_tier_for_the_next_launch()
    {
        var settings = new SettingsService(_settingsPath);
        settings.Load();

        var service = new EntitlementService(
            new FakeLicenseStore(),
            new FakeValidator(new License(SubscriptionPlan.Pro, "KEY", null)),
            settings);

        await service.ActivateAsync("KEY");

        var reloaded = new SettingsService(_settingsPath);
        Assert.Equal(SubscriptionPlan.Pro, reloaded.Load().CachedPlan);
    }

    [Fact]
    public async Task Deactivating_clears_the_key_and_drops_to_Free()
    {
        var service = CreateService(out var store, out _, new License(SubscriptionPlan.Pro, "KEY", null));
        await service.ActivateAsync("KEY");

        service.Deactivate();

        Assert.Equal(SubscriptionPlan.Free, service.CurrentPlan);
        Assert.True(store.Cleared);
    }

    [Fact]
    public async Task PlanChanged_fires_only_when_the_tier_actually_moves()
    {
        var service = CreateService(out _, out _, new License(SubscriptionPlan.Plus, "KEY", null));

        var changes = 0;
        service.PlanChanged += () => changes++;

        await service.ActivateAsync("KEY");
        await service.ActivateAsync("KEY");

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_licence_that_expires_stops_granting_its_tier()
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTime(now);

        var settings = new SettingsService(_settingsPath);
        settings.Load();

        var service = new EntitlementService(
            new FakeLicenseStore(),
            new FakeValidator(new License(SubscriptionPlan.Pro, "KEY", now.AddHours(1))),
            settings,
            clock);

        await service.ActivateAsync("KEY");
        Assert.Equal(SubscriptionPlan.Pro, service.CurrentPlan);

        clock.Now = now.AddHours(2);
        Assert.Equal(SubscriptionPlan.Free, service.CurrentPlan);
    }

    [Fact]
    public async Task Activating_a_blank_key_is_rejected_without_touching_the_store()
    {
        var service = CreateService(out var store, out _);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ActivateAsync("   "));
        Assert.Null(store.Saved);
    }

    private EntitlementService CreateService(
        out FakeLicenseStore store,
        out SettingsService settings,
        License? resolved = null)
    {
        store = new FakeLicenseStore();
        settings = new SettingsService(_settingsPath);
        settings.Load();

        return new EntitlementService(store, new FakeValidator(resolved ?? License.Free), settings);
    }

    private sealed class FakeLicenseStore : ILicenseStore
    {
        public string? Saved { get; private set; }

        public bool Cleared { get; private set; }

        public string? ReadKey() => Saved;

        public void SaveKey(string key) => Saved = key;

        public void Clear()
        {
            Saved = null;
            Cleared = true;
        }
    }

    private sealed class FakeValidator : ILicenseValidator
    {
        private readonly License _result;

        public FakeValidator(License result) => _result = result;

        public Task<License> ValidateAsync(string? key, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.IsNullOrWhiteSpace(key) ? License.Free : _result);
    }

    private sealed class FakeTime : TimeProvider
    {
        public FakeTime(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
