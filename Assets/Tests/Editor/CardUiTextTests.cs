using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;

public sealed class CardUiTextTests
{
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<Action> callbacks = new Queue<Action>();
        public override void Post(SendOrPostCallback callback, object state) => callbacks.Enqueue(() => callback(state));
        public void Drain() { while (callbacks.Count > 0) callbacks.Dequeue()(); }
    }

    private sealed class Deferred<T> : AsyncOperationBase<T>
    {
        public int WaitCalls;
        protected override void Execute() { }
        protected override bool InvokeWaitForCompletion()
        {
            WaitCalls++;
            throw new InvalidOperationException("Synchronous resource waits are forbidden in this fixture.");
        }
        public void Succeed(T value) => Complete(value, true, (string)null);
    }

    private sealed class DelayedDatabase : LocalizedStringDatabase
    {
        public readonly Dictionary<string, AsyncOperationHandle<StringTable>> Tables =
            new Dictionary<string, AsyncOperationHandle<StringTable>>();
        public int Requests;
        public override AsyncOperationHandle<StringTable> GetTableAsync(TableReference reference, Locale locale = null)
        {
            Assert.That(reference.TableCollectionName, Is.EqualTo(CardUiText.TableName));
            Assert.That(locale, Is.Not.Null);
            Requests++;
            return Tables[locale.Identifier.Code];
        }
    }

    private sealed class DelayedSettings : LocalizationSettings
    {
        public AsyncOperationHandle<Locale> LocaleOperation;
        public DelayedDatabase Database;
        public int SynchronousLocaleReads;
        public override AsyncOperationHandle<Locale> GetSelectedLocaleAsync() => LocaleOperation;
        public override Locale GetSelectedLocale()
        {
            SynchronousLocaleReads++;
            throw new InvalidOperationException("Synchronous locale selection is forbidden in this fixture.");
        }
        public override LocalizedStringDatabase GetStringDatabase() => Database;
    }

    private ResourceManager manager;
    private QueuedContext context;
    private SynchronizationContext previousContext;
    private LocalizationSettings previousSettings;
    private DelayedSettings settings;
    private readonly List<AsyncOperationHandle> handles = new List<AsyncOperationHandle>();
    private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

    [SetUp]
    public void SetUp()
    {
        previousSettings = LocalizationSettings.GetInstanceDontCreateDefault();
        previousContext = SynchronizationContext.Current;
        context = new QueuedContext();
        SynchronizationContext.SetSynchronizationContext(context);
        ResetText();
        manager = new ResourceManager();
        settings = ScriptableObject.CreateInstance<DelayedSettings>();
        settings.Database = new DelayedDatabase();
        objects.Add(settings);
        LocalizationSettings.Instance = settings;
    }

    [TearDown]
    public void TearDown()
    {
        ResetText();
        LocalizationSettings.Instance = previousSettings;
        foreach (AsyncOperationHandle handle in handles)
            if (handle.IsValid()) handle.Release();
        handles.Clear();
        manager.Dispose();
        foreach (UnityEngine.Object value in objects)
            UnityEngine.Object.DestroyImmediate(value);
        objects.Clear();
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    [Test]
    public void PendingLocaleAndTableReturnImmediatelyThenNotifyOnceForTheWholeTable()
    {
        var localeLoad = new Deferred<Locale>();
        settings.LocaleOperation = Start(localeLoad);
        var tableLoad = new Deferred<StringTable>();
        settings.Database.Tables["zh"] = Start(tableLoad);
        int notifications = 0;
        CardUiText.TextChanged += () => notifications++;
        Assert.That(CardUiText.Get("start.title"), Is.EqualTo(CardUiText.EnglishFallbacks["start.title"]));
        Assert.That(CardUiText.Get("common.action.retry"), Is.EqualTo("Retry"));
        Assert.That(settings.Database.Requests, Is.Zero);
        Assert.That(localeLoad.WaitCalls, Is.Zero);
        localeLoad.Succeed(Locale("zh"));
        Pump();
        Assert.That(settings.Database.Requests, Is.EqualTo(1));
        Assert.That(CardUiText.Get("start.title"), Is.EqualTo(CardUiText.EnglishFallbacks["start.title"]));
        Assert.That(notifications, Is.Zero);
        StringTable table = Table("zh", "测试标题");
        table.AddEntry("common.action.retry", "重试");
        tableLoad.Succeed(table);
        Pump();
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(CardUiText.Get("start.title"), Is.EqualTo("测试标题"));
        Assert.That(CardUiText.Get("common.action.retry"), Is.EqualTo("重试"));
        Assert.That(settings.Database.Requests, Is.EqualTo(1));
        Assert.That(tableLoad.WaitCalls, Is.Zero);
        Assert.That(settings.SynchronousLocaleReads, Is.Zero);
    }

    [Test]
    public void LocaleSwitchRejectsLateOldTableAndRefreshInvalidatesCachedText()
    {
        var english = new Deferred<StringTable>();
        var japanese = new Deferred<StringTable>();
        settings.Database.Tables["en"] = Start(english);
        settings.Database.Tables["ja"] = Start(japanese);
        Select(Locale("en"));
        int notifications = 0;
        CardUiText.TextChanged += () => notifications++;
        CardUiText.Get("start.title");
        Select(Locale("ja"));
        Assert.That(CardUiText.Get("start.title"), Is.EqualTo(CardUiText.EnglishFallbacks["start.title"]));
        english.Succeed(Table("en", "Old language result"));
        Pump();
        Assert.That(notifications, Is.Zero);
        StringTable table = Table("ja", "日本語タイトル");
        japanese.Succeed(table);
        Pump();
        Assert.That(CardUiText.Get("start.title"), Is.EqualTo("日本語タイトル"));
        Assert.That(notifications, Is.EqualTo(1));
        table.GetEntry("start.title").Value = "更新したタイトル";
        settings.ForceRefresh();
        Assert.That(CardUiText.Get("start.title"), Is.EqualTo("更新したタイトル"));
        Assert.That(CardUiText.Get("unknown.fixture.key"), Is.EqualTo("unknown.fixture.key"));
        table.AddEntry("common.action.retry", "");
        Assert.That(CardUiText.Get("common.action.retry"), Is.EqualTo("Retry"));
        table.GetEntry("common.action.retry").Value = "再試行";
        Assert.That(CardUiText.Get("common.action.retry"), Is.EqualTo("再試行"), "Temporary fallback must never enter the localized cache.");
    }

    [Test]
    public void ResetDetachesPendingCallbacksWithoutReleasingDatabaseOwnedHandles()
    {
        Select(Locale("en"));
        var load = new Deferred<StringTable>();
        AsyncOperationHandle<StringTable> handle = Start(load);
        settings.Database.Tables["en"] = handle;
        int notifications = 0;
        CardUiText.TextChanged += () => notifications++;
        CardUiText.Get("start.title");
        ResetText();
        Assert.That(handle.IsValid(), Is.True);
        load.Succeed(Table("en", "Loaded after reset"));
        Pump();
        Assert.That(notifications, Is.Zero);
        Assert.That(handle.IsValid(), Is.True);
        Assert.That(CardUiText.Get("start.title"), Is.EqualTo("Loaded after reset"));
    }

    private AsyncOperationHandle<T> Start<T>(Deferred<T> operation)
    {
        AsyncOperationHandle<T> handle = manager.StartOperation(operation, default);
        handles.Add(handle);
        return handle;
    }
    private void Select(Locale locale)
    {
        var operation = new Deferred<Locale>();
        settings.LocaleOperation = Start(operation);
        operation.Succeed(locale);
    }
    private Locale Locale(string code)
    {
        Locale locale = UnityEngine.Localization.Locale.CreateLocale(code);
        objects.Add(locale);
        return locale;
    }
    private StringTable Table(string code, string title)
    {
        var table = ScriptableObject.CreateInstance<StringTable>();
        var shared = ScriptableObject.CreateInstance<SharedTableData>();
        shared.TableCollectionName = CardUiText.TableName;
        table.SharedData = shared;
        table.LocaleIdentifier = new LocaleIdentifier(code);
        table.AddEntry("start.title", title);
        objects.Add(table);
        objects.Add(shared);
        return table;
    }
    private void Pump()
    {
        // This package exposes its actual deferred-callback pump internally.
        MethodInfo update = typeof(ResourceManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(float) }, null);
        Assert.That(update, Is.Not.Null, "The installed ResourceManager must expose Update(float).");
        update.Invoke(manager, new object[] { 0f });
        context.Drain();
    }
    private static void ResetText() => typeof(CardUiText)
        .GetMethod("ResetRuntimeState", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
}
