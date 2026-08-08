using System;
using System.IO;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

public sealed class PlayerUiErrorTests
{
    private sealed class ThrowingCatalogProvider : ICatalogProvider
    {
        private readonly Exception exception;

        public ThrowingCatalogProvider(Exception exception)
        {
            this.exception = exception;
        }

        public CatalogLoadResult Load() => throw exception;
    }

    private sealed class AudioSink : IAudioFeedbackSink
    {
        public int Count { get; private set; }
        public bool TryPlay(string cueKey)
        {
            Count++;
            return true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        UIFeedbackService.Configure(false, true, 1f, true);
    }

    [TestCase(ContentDownloadPreflightStatus.Offline, PlayerUiErrorCode.Offline)]
    [TestCase(ContentDownloadPreflightStatus.InsufficientSpace, PlayerUiErrorCode.InsufficientSpace)]
    [TestCase(ContentDownloadPreflightStatus.StorageUnavailable, PlayerUiErrorCode.ServiceUnavailable)]
    public void PreflightMapper_UsesStructuredStatus(
        ContentDownloadPreflightStatus status,
        PlayerUiErrorCode expected)
    {
        Assert.That(PlayerUiErrorMapper.FromPreflight(status).Code, Is.EqualTo(expected));
    }

    [TestCase(ContentDownloadPreflightStatus.NoSelection)]
    [TestCase(ContentDownloadPreflightStatus.AlreadyCurrent)]
    [TestCase(ContentDownloadPreflightStatus.Ready)]
    [TestCase(ContentDownloadPreflightStatus.WaitingForWifi)]
    [TestCase(ContentDownloadPreflightStatus.CellularConfirmationRequired)]
    public void PreflightMapper_DoesNotTurnPolicyOrSuccessIntoAnError(
        ContentDownloadPreflightStatus status)
    {
        Assert.That(PlayerUiErrorMapper.FromPreflight(status), Is.Null);
    }

    [TestCase(ContentInstallPlanStatus.InsufficientSpace, PlayerUiErrorCode.InsufficientSpace)]
    [TestCase(ContentInstallPlanStatus.InvalidPackage, PlayerUiErrorCode.CatalogCorrupt)]
    [TestCase(ContentInstallPlanStatus.StorageUnavailable, PlayerUiErrorCode.ServiceUnavailable)]
    public void InstallPlanMapper_UsesStructuredStatus(
        ContentInstallPlanStatus status,
        PlayerUiErrorCode expected)
    {
        Assert.That(PlayerUiErrorMapper.FromInstallPlan(status).Code, Is.EqualTo(expected));
    }

    [TestCase(ContentInstallPlanStatus.Ready)]
    [TestCase(ContentInstallPlanStatus.AlreadyCurrent)]
    public void InstallPlanMapper_DoesNotTurnSuccessIntoAnError(ContentInstallPlanStatus status)
    {
        Assert.That(PlayerUiErrorMapper.FromInstallPlan(status), Is.Null);
    }

    [Test]
    public void CatalogMapper_UsesOnlyStructuredState()
    {
        var catalog = new UniversalCatalog(
            Array.Empty<LanguageDefinition>(),
            Array.Empty<GameDefinition>(),
            Array.Empty<SetDefinition>(),
            Array.Empty<CollectibleItemDefinition>(),
            Array.Empty<RarityDefinition>(),
            Array.Empty<VariantDefinition>(),
            Array.Empty<PrintingDefinition>(),
            Array.Empty<ProductDefinition>());
        CatalogLoadResult ready = CatalogLoadResult.Success(catalog, 0, 0, 1);
        CatalogLoadResult empty = CatalogLoadResult.Success(catalog, 0, 0, 0);

        Assert.That(PlayerUiErrorMapper.FromCatalog(ready), Is.Null);
        Assert.That(PlayerUiErrorMapper.FromCatalog(empty).Code,
            Is.EqualTo(PlayerUiErrorCode.NotInstalled));
        Assert.That(
            PlayerUiErrorMapper.FromCatalog(CatalogLoadResult.Failure(
                "network not found /storage/private")).Code,
            Is.EqualTo(PlayerUiErrorCode.Unexpected));
        Assert.That(
            PlayerUiErrorMapper.FromCatalog(CatalogLoadResult.Failure(
                "private detail", CatalogFailureReason.CatalogCorrupt)).Code,
            Is.EqualTo(PlayerUiErrorCode.CatalogCorrupt));
        Assert.That(
            PlayerUiErrorMapper.FromCatalog(CatalogLoadResult.Failure(
                "private detail", CatalogFailureReason.ServiceUnavailable)).Code,
            Is.EqualTo(PlayerUiErrorCode.ServiceUnavailable));
        Assert.That(PlayerUiErrorMapper.Create(PlayerUiErrorCode.Offline)
            .Supports(PlayerUiErrorAction.ManageContent), Is.True);
    }

    [Test]
    public void CatalogSession_PreservesContextAsAStructuredFailureReason()
    {
        CatalogLoadResult corrupt = new CatalogSession(
            new ThrowingCatalogProvider(new InvalidDataException("private path"))).EnsureLoaded();
        CatalogLoadResult unavailable = new CatalogSession(
            new ThrowingCatalogProvider(new IOException("private path"))).EnsureLoaded();

        Assert.That(corrupt.FailureReason, Is.EqualTo(CatalogFailureReason.CatalogCorrupt));
        Assert.That(unavailable.FailureReason, Is.EqualTo(CatalogFailureReason.ServiceUnavailable));
    }

    [Test]
    public void CompletedOperations_DoNotProducePlayerErrors()
    {
        Assert.That(PlayerUiErrorMapper.FromInstall(ContentPackageInstallStatus.Succeeded), Is.Null);
        Assert.That(PlayerUiErrorMapper.FromInstall(ContentPackageInstallStatus.Cancelled), Is.Null);
        Assert.That(PlayerUiErrorMapper.FromImage(ContentImageLoadStatus.Succeeded), Is.Null);
        Assert.That(PlayerUiErrorMapper.FromRemoval(ContentPackageRemovalStatus.Removed), Is.Null);
        Assert.That(PlayerUiErrorMapper.FromRemoval(ContentPackageRemovalStatus.NotInstalled), Is.Null);
        Assert.That(PlayerUiErrorMapper.FromRemoval(ContentPackageRemovalStatus.Cancelled), Is.Null);
    }

    [TestCase(ContentPackageInstallStatus.ArchiveNotFound)]
    [TestCase(ContentPackageInstallStatus.IntegrityMismatch)]
    [TestCase(ContentPackageInstallStatus.InvalidArchive)]
    public void InstallMapper_MapsUntrustedArchiveFailuresToVerification(
        ContentPackageInstallStatus status)
    {
        Assert.That(PlayerUiErrorMapper.FromInstall(status).Code,
            Is.EqualTo(PlayerUiErrorCode.VerificationFailed));
    }

    [Test]
    public void MessageOnlyFailure_IsUnexpectedAndNeverAppearsInLocalizedCopy()
    {
        const string detail = "/storage/private Content https://credential.example sentinel stack";
        PlayerUiError error = PlayerUiErrorMapper.FromDetail(detail);

        Assert.That(error.Code, Is.EqualTo(PlayerUiErrorCode.Unexpected));
        foreach (string asset in new[]
                 {
                     "Assets/Resources/Data/Localization/Card_UI_en.asset",
                     "Assets/Resources/Data/Localization/Card_UI_zh.asset",
                     "Assets/Resources/Data/Localization/Card_UI_ja.asset"
                 })
        {
            StringTable table = AssetDatabase.LoadAssetAtPath<StringTable>(asset);
            foreach (PlayerUiErrorCode code in Enum.GetValues(typeof(PlayerUiErrorCode)))
            {
                PlayerUiError mapped = PlayerUiErrorMapper.Create(code);
                string title = table.GetEntry(PlayerUiErrorText.Key(mapped, "title"))?.LocalizedValue;
                string body = table.GetEntry(PlayerUiErrorText.Key(mapped, "body"))?.LocalizedValue;
                Assert.That(title, Is.Not.Empty, asset + " " + code + " title");
                Assert.That(body, Is.Not.Empty, asset + " " + code + " body");
                Assert.That(title + body, Does.Not.Contain("/storage/"));
                Assert.That(title + body, Does.Not.Contain("credential.example"));
                Assert.That(title + body, Does.Not.Contain("sentinel"));
            }
        }
    }

    [Test]
    public void Presenter_AnnouncesOnceAndMuteSuppressesAudio()
    {
        var panel = new VisualElement();
        var title = new Label();
        var body = new Label();
        var audio = new AudioSink();
        int errorCues = 0;
        Action<FeedbackCue> handler = cue => { if (cue == FeedbackCue.Error) errorCues++; };
        UIFeedbackService.RegisterAudioSink(audio);
        UIFeedbackService.FeedbackPlayed += handler;
        UIFeedbackService.Configure(true, true, 1f, false);
        try
        {
            using var presenter = new PlayerUiErrorPresenter(panel, title, body);
            PlayerUiError error = PlayerUiErrorMapper.Create(PlayerUiErrorCode.Offline);
            presenter.Show(error);
            presenter.Show(error);

            Assert.That(errorCues, Is.EqualTo(1));
            Assert.That(audio.Count, Is.EqualTo(0));
            Assert.That(presenter.IsAnimating, Is.False);
            Assert.That(panel.style.opacity.value, Is.EqualTo(1f));
            presenter.Hide();
            Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.None));
        }
        finally
        {
            UIFeedbackService.FeedbackPlayed -= handler;
            UIFeedbackService.UnregisterAudioSink(audio);
        }
    }

    [Test]
    public void Presenter_UsesMotionAndAudioOnlyForANewErrorTransition()
    {
        var panel = new VisualElement();
        var audio = new AudioSink();
        UIFeedbackService.RegisterAudioSink(audio);
        UIFeedbackService.Configure(false, true, 1f, true);
        try
        {
            using var presenter = new PlayerUiErrorPresenter(panel, new Label(), new Label());
            PlayerUiError offline = PlayerUiErrorMapper.Create(PlayerUiErrorCode.Offline);
            presenter.Show(offline);

            Assert.That(audio.Count, Is.EqualTo(1));
            Assert.That(presenter.IsAnimating, Is.True);
            Assert.That(panel.ClassListContains("is-entering"), Is.True);

            presenter.Show(offline);
            Assert.That(audio.Count, Is.EqualTo(1));

            presenter.Show(PlayerUiErrorMapper.Create(PlayerUiErrorCode.ServiceUnavailable));
            Assert.That(audio.Count, Is.EqualTo(2));
            presenter.Hide();
            Assert.That(panel.ClassListContains("is-leaving"), Is.True);
        }
        finally
        {
            UIFeedbackService.UnregisterAudioSink(audio);
        }
    }
}
