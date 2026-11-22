using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Gacha.Application;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerRecoveryPersistenceTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private readonly Stack<Action> cleanup = new Stack<Action>();
    private string root;
    private string path;
    private GameObject host;
    private Inventory inventory;
    private LanguageSelectionService languages;
    private ExperienceSettingsService experience;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "player-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        cleanup.Push(() => Directory.Delete(root, true));
        path = Path.Combine(root, "save.json");
        FieldInfo defaultPath = typeof(LocalSaveService).GetField("DefaultPathOverride", PrivateStatic);
        object previousPath = defaultPath.GetValue(null);
        bool previousLock = LocalSaveService.IsAutomaticSaveEnabled;
        cleanup.Push(() => defaultPath.SetValue(null, previousPath));
        cleanup.Push(() => typeof(LocalSaveService).GetField("automaticSaveEnabled", PrivateStatic)
            .SetValue(null, previousLock));
        defaultPath.SetValue(null, path);
        LocalSaveService.LoadDetailed();

        host = new GameObject(nameof(PlayerRecoveryPersistenceTests));
        host.SetActive(false);
        cleanup.Push(() => UnityEngine.Object.DestroyImmediate(host));
        inventory = host.AddComponent<Inventory>();
        ReplaceStaticProperty(typeof(Inventory), nameof(Inventory.Instance), inventory);
        inventory.ReplaceData(InventoryWith("local", 2));
        var preferences = new Preferences();
        languages = new LanguageSelectionService(preferences, new[] { "en", "zh" });
        experience = new ExperienceSettingsService(preferences);
        ReplaceStaticProperty(typeof(ApplicationServices), nameof(ApplicationServices.Catalog),
            new CatalogSession(new UnusedCatalog()));
        ReplaceStaticProperty(typeof(ApplicationServices), nameof(ApplicationServices.Languages), languages);
        ReplaceStaticProperty(typeof(ApplicationServices), nameof(ApplicationServices.ExperienceSettings), experience);
    }

    [TearDown]
    public void TearDown()
    {
        while (cleanup.Count > 0) cleanup.Pop()();
    }

    [TestCase(typeof(MainMenuBackController))]
    [TestCase(typeof(SaveRecoverySettingsPanel))]
    public void SettingsImport_PreservesProtectedFilesUntilDurableCommit(Type settingsPanel)
    {
        const string brokenActive = "broken active";
        const string brokenBackup = "broken backup";
        File.WriteAllText(path, brokenActive);
        File.WriteAllText(path + ".backup", brokenBackup);
        Assert.That(LocalSaveService.LoadDetailed().CanSave, Is.False);
        var service = new InventoryRecoveryService();
        var incoming = new PlayerRecoveryState(InventoryWith("import", 3),
            new LanguagePreferences("zh", "ja"), new ExperienceSettings(reduceMotion: true));
        string import = Path.Combine(root, "incoming.gachasave");
        string backup = Path.Combine(root, "before.gachasave");
        service.Export(import, incoming, "source-install");
        IPlayerRecoveryTarget target = RecoveryTarget(settingsPanel);
        Directory.CreateDirectory(path + ".tmp");
        Assert.Throws<InventoryRecoveryException>(() => service.Restore(import, backup, target, "local-install"));
        Assert.That(LocalSaveService.IsAutomaticSaveEnabled, Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(brokenActive));
        Assert.That(File.ReadAllText(path + ".backup"), Is.EqualTo(brokenBackup));
        Assert.That(inventory.Data.Cards, Is.EquivalentTo(InventoryWith("local", 2).Cards));
        Assert.That(languages.UiLanguageId, Is.EqualTo("en"));
        Assert.That(experience.Current.ReduceMotion, Is.False);
        Assert.That(service.Preview(backup).TotalCardCount, Is.EqualTo(2));

        Directory.Delete(path + ".tmp");
        service.Restore(import, Path.Combine(root, "retry.gachasave"), target, "local-install");
        Assert.That(LocalSaveService.IsAutomaticSaveEnabled, Is.True);
        LocalSaveLoadResult saved = LocalSaveService.LoadFromPath(path);
        Assert.That(saved.Revision, Is.EqualTo(1));
        Assert.That(saved.Data.Cards, Is.EquivalentTo(incoming.Inventory.Cards));
        Assert.That(inventory.Data.Cards, Is.EquivalentTo(incoming.Inventory.Cards));
        Assert.That(languages.UiLanguageId, Is.EqualTo("zh"));
        Assert.That(languages.RequestedContentLanguageId, Is.EqualTo("ja"));
        Assert.That(experience.Current.ReduceMotion, Is.True);
        string[] quarantined = Directory.GetFiles(root, "*.corrupt*").Select(File.ReadAllText).ToArray();
        Assert.That(quarantined, Does.Contain(brokenActive));
        Assert.That(quarantined, Does.Contain(brokenBackup));
    }

    [Test]
    public void AccountLink_CannotReleaseAnExistingRecoveryLock()
    {
        const string corrupt = "damaged save";
        File.WriteAllText(path, corrupt);
        Assert.That(LocalSaveService.LoadDetailed().CanSave, Is.False);
        var target = new UnityGameIdentityPlayerTarget();
        var selected = new PlayerRecoveryState(InventoryWith("account", 5),
            new LanguagePreferences("zh", "ja"), new ExperienceSettings(reduceMotion: true));
        Assert.Throws<InventoryRecoveryException>(() => target.Apply(selected));
        Assert.That(LocalSaveService.IsAutomaticSaveEnabled, Is.False);
        Assert.That(File.ReadAllText(path), Is.EqualTo(corrupt));
        Assert.That(inventory.Data.Cards, Is.EquivalentTo(InventoryWith("local", 2).Cards));
        Assert.That(languages.UiLanguageId, Is.EqualTo("en"));
        Assert.That(experience.Current.ReduceMotion, Is.False);
    }

    [TestCase(typeof(MainMenuBackController), InventoryConflictChoice.KeepLocal)]
    [TestCase(typeof(MainMenuBackController), InventoryConflictChoice.UseCloud)]
    [TestCase(typeof(MainMenuBackController), InventoryConflictChoice.SafeMerge)]
    [TestCase(typeof(CloudConflictSettingsDialog), InventoryConflictChoice.KeepLocal)]
    [TestCase(typeof(CloudConflictSettingsDialog), InventoryConflictChoice.UseCloud)]
    [TestCase(typeof(CloudConflictSettingsDialog), InventoryConflictChoice.SafeMerge)]
    public async Task CloudChoice_RetriesOnlyAfterLocalCommitSucceeds(Type panel, InventoryConflictChoice choice)
    {
        var localTargetType = panel.GetNestedTypes(BindingFlags.NonPublic)
            .Single(type => typeof(IInventoryConflictTarget).IsAssignableFrom(type));
        var target = new RecordingCloudTarget((IInventoryConflictTarget)Activator.CreateInstance(localTargetType, true));
        var conflict = new CloudInventoryConflictCoordinator();
        var cloud = InventoryWith("cloud", 7);
        Assert.That(conflict.Prepare(inventory.Data, cloud, true).RequiresChoice, Is.True);
        LocalSaveService.Commit(inventory.Data);
        byte[] previous = File.ReadAllBytes(path);
        Directory.CreateDirectory(path + ".tmp");
        InventoryConflictResolutionResult failed = await conflict.ResolveAsync(choice, target);
        Assert.That(failed.Succeeded, Is.False);
        Assert.That(conflict.HasPending, Is.True);
        Assert.That(conflict.IsResolving, Is.False);
        Assert.That(target.Uploads, Is.Empty);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(previous));
        Assert.That(inventory.Data.Cards, Is.EquivalentTo(InventoryWith("local", 2).Cards));
        Directory.Delete(path + ".tmp");
        InventoryConflictResolutionResult retried = await conflict.ResolveAsync(choice, target);
        Assert.That(retried.Succeeded, Is.True);
        Assert.That(conflict.HasPending, Is.False);
        var expected = new Dictionary<string, int>();
        if (choice != InventoryConflictChoice.UseCloud) expected.Add("local", 2);
        if (choice != InventoryConflictChoice.KeepLocal) expected.Add("cloud", 7);
        Assert.That(target.Uploads, Has.Count.EqualTo(1));
        Assert.That(target.Uploads[0].Cards, Is.EquivalentTo(expected));
        Assert.That(inventory.Data.Cards, Is.EquivalentTo(expected));
        LocalSaveLoadResult reloaded = LocalSaveService.LoadFromPath(path);
        Assert.That(reloaded.Revision, Is.EqualTo(2));
        Assert.That(reloaded.Data.Cards, Is.EquivalentTo(expected));
    }

    private void ReplaceStaticProperty(Type owner, string name, object replacement)
    {
        PropertyInfo property = owner.GetProperty(name);
        object previous = property.GetValue(null);
        cleanup.Push(() => property.SetValue(null, previous));
        property.SetValue(null, replacement);
    }

    private IPlayerRecoveryTarget RecoveryTarget(Type panel)
    {
        var controller = host.AddComponent(panel);
        bool main = panel == typeof(MainMenuBackController);
        if (main)
        {
            panel.GetField("languages", PrivateInstance).SetValue(controller, languages);
            panel.GetField("experience", PrivateInstance).SetValue(controller, experience);
        }
        object[] arguments = main ? new object[1] : new object[2];
        MethodInfo create = panel.GetMethod(main ? "TryCreateRecoveryTarget" : "TryCreateTarget", PrivateInstance);
        Assert.That(create.Invoke(controller, arguments), Is.EqualTo(true));
        return (IPlayerRecoveryTarget)arguments[0];
    }

    private static InventoryData InventoryWith(string card, int count) =>
        new InventoryData { Cards = new Dictionary<string, int> { [card] = count } };

    private sealed class UnusedCatalog : ICatalogProvider
    {
        public CatalogLoadResult Load() => throw new InvalidOperationException("Catalog load during player recovery.");
    }

    private sealed class Preferences : ILanguagePreferenceStore, IExperienceSettingsStore
    {
        private LanguagePreferences language = new LanguagePreferences("en", "en");
        private ExperienceSettings experience = new ExperienceSettings();
        LanguagePreferences ILanguagePreferenceStore.Load() => language;
        ExperienceSettings IExperienceSettingsStore.Load() => experience;
        public void Save(LanguagePreferences next) => language = next;
        public void Save(ExperienceSettings next) => experience = next;
    }

    private sealed class RecordingCloudTarget : IInventoryConflictTarget
    {
        private readonly IInventoryConflictTarget local;
        public RecordingCloudTarget(IInventoryConflictTarget local) => this.local = local;
        public List<InventoryData> Uploads { get; } = new List<InventoryData>();
        public InventoryData CaptureLocal() => local.CaptureLocal();
        public void ApplyLocal(InventoryData selected) => local.ApplyLocal(selected);
        public Task<bool> SaveCloudAsync(InventoryData selected)
        {
            Uploads.Add(CloudInventoryConflictCoordinator.Clone(selected));
            return Task.FromResult(true);
        }
    }
}
