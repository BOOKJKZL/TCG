using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Gacha.Application;
using NUnit.Framework;
using UnityEngine;

public sealed class GameIdentityServiceTests
{
    [Test]
    public void Configuration_IsNamespacedMinimalAndDisabledUntilExternalClientExists()
    {
        GameIdentityConfiguration configuration = GameIdentityConfiguration.Load();

        Assert.That(configuration.SchemaVersion, Is.EqualTo(1));
        Assert.That(configuration.Enabled, Is.False);
        Assert.That(configuration.ProjectId, Is.EqualTo(GameIdentityConfiguration.ExpectedProjectId));
        Assert.That(configuration.SaveNamespace, Is.EqualTo(GameIdentityConfiguration.ExpectedNamespace));
        Assert.That(configuration.LinkedProfile, Is.EqualTo("gacha-linked"));
        Assert.That(configuration.Scopes, Is.EquivalentTo(new[] { "openid", "email", "offline_access" }));

        string playerAccountSettings = File.ReadAllText(
            Path.Combine(Application.dataPath, "Resources/Other/UnityPlayerAccountSettings.asset"));
        Assert.That(playerAccountSettings, Does.Match(@"clientId:\s*\r?\n\s*scopeMask: 7"));
        Assert.That(playerAccountSettings, Does.Contain("scopeMask: 7"));
        Assert.That(playerAccountSettings.ToLowerInvariant(), Does.Not.Contain("gmail"));
        Assert.That(playerAccountSettings.ToLowerInvariant(), Does.Not.Contain("drive"));
    }

    [TestCase("gmail.readonly")]
    [TestCase("drive.readonly")]
    [TestCase("profile")]
    public void Configuration_RejectsAnyScopeOutsideRecoverableEmailIdentity(string forbiddenScope)
    {
        string json = ConfigurationJson(true).Replace(
            "\"offline_access\"",
            "\"offline_access\", \"" + forbiddenScope + "\"");

        Assert.That(
            () => GameIdentityConfiguration.Parse(json),
            Throws.TypeOf<GameIdentityConfigurationException>().With.Message.Contains("only openid"));
    }

    [Test]
    public async Task DisabledConfiguration_PerformsNoBackupAndNoBackendCalls()
    {
        var backend = new FakeBackend(GameIdentityBackendOutcome.CurrentPlayerLinked);
        var target = new FakeTarget(CreateState("local", 2));
        var store = new MemoryProfileStore();
        var coordinator = new GameIdentityLinkCoordinator(
            GameIdentityConfiguration.Parse(ConfigurationJson(false)),
            backend,
            target,
            store);

        GameIdentityConnectResult result = await coordinator.ConnectAsync();

        Assert.That(result.Outcome, Is.EqualTo(GameIdentityConnectOutcome.ExternalSetupRequired));
        Assert.That(backend.LinkCalls, Is.Zero);
        Assert.That(target.CaptureCalls, Is.Zero);
        Assert.That(target.BackupCalls, Is.Zero);
        Assert.That(target.SaveCloudCalls, Is.Zero);
    }

    [Test]
    public async Task NewIdentity_LinksCurrentPlayerAndUploadsItsExistingProgress()
    {
        PlayerRecoveryState original = CreateState("local", 4);
        var backend = new FakeBackend(GameIdentityBackendOutcome.CurrentPlayerLinked)
        {
            ActiveProfile = "default",
            RedactedIdentity = "j***@e***.com"
        };
        var target = new FakeTarget(original);
        var store = new MemoryProfileStore();
        var coordinator = new GameIdentityLinkCoordinator(
            GameIdentityConfiguration.Parse(ConfigurationJson(true)),
            backend,
            target,
            store);

        GameIdentityConnectResult result = await coordinator.ConnectAsync();

        Assert.That(result.Outcome, Is.EqualTo(GameIdentityConnectOutcome.LinkedCurrentPlayer));
        Assert.That(result.RedactedIdentity, Is.EqualTo("j***@e***.com"));
        Assert.That(target.BackupCalls, Is.EqualTo(1));
        Assert.That(target.LastCloudSave.Cards["local"], Is.EqualTo(4));
        Assert.That(store.ActiveProfile, Is.EqualTo("default"));
        Assert.That(backend.RestoreCalls, Is.Zero);
    }

    [Test]
    public async Task NewIdentity_WhenInitialCloudWriteIsOffline_RemainsRecoverableForLaterRetry()
    {
        var backend = new FakeBackend(GameIdentityBackendOutcome.CurrentPlayerLinked)
        {
            ActiveProfile = "default"
        };
        var target = new FakeTarget(CreateState("local", 5)) { SaveCloudSucceeds = false };
        var store = new MemoryProfileStore();
        var coordinator = new GameIdentityLinkCoordinator(
            GameIdentityConfiguration.Parse(ConfigurationJson(true)),
            backend,
            target,
            store);

        GameIdentityConnectResult result = await coordinator.ConnectAsync();

        Assert.That(result.Outcome, Is.EqualTo(GameIdentityConnectOutcome.LinkedCurrentPlayerCloudPending));
        Assert.That(result.Succeeded, Is.True, "An irreversible successful link must not be reported as undone.");
        Assert.That(store.ActiveProfile, Is.EqualTo("default"));
        Assert.That(backend.RestoreCalls, Is.Zero);
        Assert.That(target.Current.Inventory.Cards["local"], Is.EqualTo(5));
    }

    [Test]
    public async Task PendingSaveChoice_BlocksAccountChangesBeforeBackupOrBrowserSignIn()
    {
        var backend = new FakeBackend(GameIdentityBackendOutcome.ExistingPlayerSignedIn);
        var target = new FakeTarget(CreateState("local", 1)) { HasPendingConflict = true };
        var store = new MemoryProfileStore();
        var coordinator = new GameIdentityLinkCoordinator(
            GameIdentityConfiguration.Parse(ConfigurationJson(true)),
            backend,
            target,
            store);

        GameIdentityConnectResult result = await coordinator.ConnectAsync();

        Assert.That(result.Outcome, Is.EqualTo(GameIdentityConnectOutcome.ConflictPending));
        Assert.That(backend.LinkCalls, Is.Zero);
        Assert.That(target.CaptureCalls, Is.Zero);
        Assert.That(target.BackupCalls, Is.Zero);
    }

    [Test]
    public async Task ExistingIdentity_WithDifferentProgress_PreservesLocalAndRequiresExplicitChoice()
    {
        PlayerRecoveryState original = CreateState("local", 2);
        var backend = new FakeBackend(GameIdentityBackendOutcome.ExistingPlayerSignedIn)
        {
            ActiveProfile = "gacha-linked"
        };
        var target = new FakeTarget(original)
        {
            CloudResult = CloudInventoryLoadResult.Success(CreateInventory("cloud", 7))
        };
        var store = new MemoryProfileStore { ActiveProfile = "default" };
        var coordinator = new GameIdentityLinkCoordinator(
            GameIdentityConfiguration.Parse(ConfigurationJson(true)),
            backend,
            target,
            store);

        GameIdentityConnectResult result = await coordinator.ConnectAsync();

        Assert.That(result.Outcome, Is.EqualTo(GameIdentityConnectOutcome.ConflictPending));
        Assert.That(result.RequiresConflictChoice, Is.True);
        Assert.That(target.Current.Inventory.Cards["local"], Is.EqualTo(2));
        Assert.That(target.Current.Inventory.Cards.ContainsKey("cloud"), Is.False);
        Assert.That(target.SaveCloudCalls, Is.Zero);
        Assert.That(store.ActiveProfile, Is.EqualTo("gacha-linked"));
        Assert.That(backend.UsedForceLink, Is.False);
    }

    [Test]
    public async Task ExistingIdentity_WhenCloudSaveFails_RestoresLocalAndPreviousProfile()
    {
        PlayerRecoveryState original = CreateState("local", 3);
        var backend = new FakeBackend(GameIdentityBackendOutcome.ExistingPlayerSignedIn)
        {
            ActiveProfile = "gacha-linked",
            CurrentProfileValue = "default"
        };
        var target = new FakeTarget(original)
        {
            CloudResult = CloudInventoryLoadResult.Empty(),
            SaveCloudSucceeds = false
        };
        var store = new MemoryProfileStore { ActiveProfile = "default" };
        var coordinator = new GameIdentityLinkCoordinator(
            GameIdentityConfiguration.Parse(ConfigurationJson(true)),
            backend,
            target,
            store);

        GameIdentityConnectResult result = await coordinator.ConnectAsync();

        Assert.That(result.Outcome, Is.EqualTo(GameIdentityConnectOutcome.Failed));
        Assert.That(target.Current.Inventory.Cards["local"], Is.EqualTo(3));
        Assert.That(target.ApplyCalls, Is.EqualTo(2));
        Assert.That(backend.RestoreCalls, Is.EqualTo(1));
        Assert.That(backend.RestoredProfile, Is.EqualTo("default"));
        Assert.That(store.ActiveProfile, Is.EqualTo("default"));
        Assert.That(result.BackupPath, Is.EqualTo("memory://pre-account-link.gachasave"));
    }

    [Test]
    public void EmailRedaction_NeverReturnsTheFullAddress()
    {
        string redacted = GameIdentityService.RedactEmail("jiejing@example.com");

        Assert.That(redacted, Is.EqualTo("j***@e***.com"));
        Assert.That(redacted, Does.Not.Contain("jiejing"));
        Assert.That(redacted, Does.Not.Contain("example"));
    }

    private static string ConfigurationJson(bool enabled) =>
        "{" +
        "\"schemaVersion\":1," +
        "\"enabled\":" + enabled.ToString().ToLowerInvariant() + "," +
        "\"projectId\":\"" + GameIdentityConfiguration.ExpectedProjectId + "\"," +
        "\"linkedProfile\":\"gacha-linked\"," +
        "\"saveNamespace\":\"" + GameIdentityConfiguration.ExpectedNamespace + "\"," +
        "\"scopes\":[\"openid\",\"email\",\"offline_access\"]" +
        "}";

    private static PlayerRecoveryState CreateState(string cardId, int count) =>
        new PlayerRecoveryState(
            CreateInventory(cardId, count),
            new LanguagePreferences("zh", "ja"),
            new ExperienceSettings(true, false, true, 1f));

    private static InventoryData CreateInventory(string cardId, int count)
    {
        var inventory = new InventoryData { LastModifiedUtcTicks = DateTime.UtcNow.Ticks };
        inventory.Cards[cardId] = count;
        return inventory;
    }

    private sealed class FakeBackend : IGameIdentityBackend
    {
        private readonly GameIdentityBackendOutcome outcome;

        public FakeBackend(GameIdentityBackendOutcome outcome) => this.outcome = outcome;
        public string CurrentProfileValue { get; set; } = "default";
        public string CurrentProfile => CurrentProfileValue;
        public string ActiveProfile { get; set; } = "gacha-linked";
        public string RedactedIdentity { get; set; } = "p***@e***.com";
        public int LinkCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public string RestoredProfile { get; private set; }
        public bool UsedForceLink { get; private set; }

        public Task<GameIdentityBackendResult> LinkOrSignInAsync(string linkedProfile)
        {
            LinkCalls++;
            return Task.FromResult(new GameIdentityBackendResult(
                outcome,
                ActiveProfile,
                "player-id",
                RedactedIdentity));
        }

        public Task RestoreProfileAsync(string previousProfile)
        {
            RestoreCalls++;
            RestoredProfile = previousProfile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTarget : IGameIdentityPlayerTarget
    {
        private readonly CloudInventoryConflictCoordinator conflict = new CloudInventoryConflictCoordinator();

        public FakeTarget(PlayerRecoveryState current) => Current = current;
        public bool HasPendingConflict { get; set; }
        public PlayerRecoveryState Current { get; private set; }
        public CloudInventoryLoadResult CloudResult { get; set; } = CloudInventoryLoadResult.Empty();
        public bool SaveCloudSucceeds { get; set; } = true;
        public int CaptureCalls { get; private set; }
        public int BackupCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public int SaveCloudCalls { get; private set; }
        public InventoryData LastCloudSave { get; private set; }

        public PlayerRecoveryState Capture()
        {
            CaptureCalls++;
            return Current;
        }

        public string CreateSafetyBackup(PlayerRecoveryState state)
        {
            BackupCalls++;
            return "memory://pre-account-link.gachasave";
        }

        public void Apply(PlayerRecoveryState state)
        {
            ApplyCalls++;
            Current = state;
        }

        public Task<CloudInventoryLoadResult> LoadCloudAsync() => Task.FromResult(CloudResult);

        public Task<bool> SaveCloudAsync(InventoryData inventory)
        {
            SaveCloudCalls++;
            LastCloudSave = CloudInventoryConflictCoordinator.Clone(inventory);
            return Task.FromResult(SaveCloudSucceeds);
        }

        public InventoryConflictPreparation PrepareConflict(
            InventoryData local,
            InventoryData cloud,
            bool cloudFound) => conflict.Prepare(local, cloud, cloudFound);
    }

    private sealed class MemoryProfileStore : IGameIdentityProfileStore
    {
        public string ActiveProfile { get; set; } = string.Empty;
        public void SetActiveProfile(string profile) => ActiveProfile = profile ?? string.Empty;
    }
}
