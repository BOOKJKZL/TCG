using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gacha.Application;
using Gacha.Presentation;
using NUnit.Framework;

public class ContentManagementPresentationTests
{
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<Action> work = new Queue<Action>();

        public int Count
        {
            get
            {
                lock (work)
                    return work.Count;
            }
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            lock (work)
                work.Enqueue(() => callback(state));
        }

        public void Drain()
        {
            while (true)
            {
                Action next;
                lock (work)
                {
                    if (work.Count == 0)
                        return;
                    next = work.Dequeue();
                }
                next();
            }
        }
    }

    [TestCase(ContentPackageOperationState.Idle, null, ContentPackagePrimaryAction.Install, "content.status.ready")]
    [TestCase(ContentPackageOperationState.Planning, null, ContentPackagePrimaryAction.None, "content.status.checking")]
    [TestCase(ContentPackageOperationState.Downloading, null, ContentPackagePrimaryAction.None, "content.status.downloading")]
    [TestCase(ContentPackageOperationState.Paused, null, ContentPackagePrimaryAction.Resume, "content.status.paused")]
    [TestCase(ContentPackageOperationState.Installing, null, ContentPackagePrimaryAction.None, "content.status.installing")]
    [TestCase(ContentPackageOperationState.Succeeded, null, ContentPackagePrimaryAction.None, "content.status.installed")]
    [TestCase(ContentPackageOperationState.AlreadyCurrent, null, ContentPackagePrimaryAction.None, "content.status.current")]
    [TestCase(ContentPackageOperationState.Cancelled, null, ContentPackagePrimaryAction.Install, "content.status.cancelled")]
    [TestCase(ContentPackageOperationState.Failed, null, ContentPackagePrimaryAction.Retry, "content.status.failed")]
    [TestCase(ContentPackageOperationState.Blocked, ContentInstallPlanStatus.InsufficientSpace, ContentPackagePrimaryAction.Retry, "content.status.insufficient_space")]
    public void Resolve_MapsOperationToStableUiState(
        ContentPackageOperationState state,
        ContentInstallPlanStatus? planStatus,
        ContentPackagePrimaryAction action,
        string statusKey)
    {
        ContentPackageUiState result = ContentPackageItemPresentation.Resolve(state, planStatus);

        Assert.That(result.PrimaryAction, Is.EqualTo(action));
        Assert.That(result.StatusKey, Is.EqualTo(statusKey));
        Assert.That(result.IsBusy, Is.EqualTo(
            state == ContentPackageOperationState.Planning ||
            state == ContentPackageOperationState.Downloading ||
            state == ContentPackageOperationState.Installing));
        Assert.That(result.IsError, Is.EqualTo(
            state == ContentPackageOperationState.Failed ||
            state == ContentPackageOperationState.Blocked));
    }

    [TestCase(ContentInstallAction.Install, ContentPackagePrimaryAction.Install, "content.action.install")]
    [TestCase(ContentInstallAction.Update, ContentPackagePrimaryAction.Update, "content.action.update")]
    [TestCase(ContentInstallAction.Repair, ContentPackagePrimaryAction.Repair, "content.action.repair")]
    public void Resolve_UsesInstallPlanActionForReadyPackage(
        ContentInstallAction installAction,
        ContentPackagePrimaryAction expected,
        string key)
    {
        ContentPackageUiState result = ContentPackageItemPresentation.Resolve(
            ContentPackageOperationState.Idle,
            null,
            installAction);

        Assert.That(result.PrimaryAction, Is.EqualTo(expected));
        Assert.That(result.PrimaryActionKey, Is.EqualTo(key));
    }

    [Test]
    public async Task Dispatcher_QueuesWorkerCallbackUntilUiThreadDrainsContext()
    {
        var context = new QueuedContext();
        var dispatcher = new SynchronizationContextUiThreadDispatcher(context);
        int uiThread = Environment.CurrentManagedThreadId;
        int observedThread = -1;

        await Task.Run(() => dispatcher.Post(() => observedThread = Environment.CurrentManagedThreadId));

        Assert.That(context.Count, Is.EqualTo(1));
        Assert.That(observedThread, Is.EqualTo(-1));
        context.Drain();
        Assert.That(observedThread, Is.EqualTo(uiThread));
        Assert.That(dispatcher.IsDispatchThread, Is.True);
    }

    [Test]
    public void Dispatcher_RunsUiThreadCallbackImmediately()
    {
        var dispatcher = new SynchronizationContextUiThreadDispatcher(new QueuedContext());
        bool called = false;

        dispatcher.Post(() => called = true);

        Assert.That(called, Is.True);
    }
}
