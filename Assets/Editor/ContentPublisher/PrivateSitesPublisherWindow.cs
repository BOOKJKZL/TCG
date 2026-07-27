using System;
using System.IO;
using System.Threading;
using Gacha.EditorTools.Content;
using UnityEditor;
using UnityEngine;

public sealed class PrivateSitesPublisherWindow : EditorWindow
{
    private const string DefaultSiteBaseUrl = "https://universal-gacha-content.jiejingleek.chatgpt.site";

    private string releaseRoot;
    private string runtimeConfigPath;
    private string credentialPath;
    private string siteBaseUrl = DefaultSiteBaseUrl;
    private string publisherToken;
    private string tokenSha256;
    private Vector2 scroll;
    private R2ReleaseUploadPlan preview;
    private CancellationTokenSource cancellation;
    private bool publishing;

    [MenuItem("Tools/Universal Gacha/Sites Content Publisher")]
    private static void Open()
    {
        GetWindow<PrivateSitesPublisherWindow>("Sites Publisher");
    }

    private void OnEnable()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        releaseRoot = ContentPackagePublisherBatch.DefaultReleaseRoot;
        runtimeConfigPath = Path.Combine(projectRoot, "LocalContent", "remote-content.json");
        credentialPath = Path.Combine(projectRoot, "LocalContent", "site-publisher-credential.json");
        siteBaseUrl = Environment.GetEnvironmentVariable("GACHA_SITE_BASE_URL") ?? DefaultSiteBaseUrl;
        publisherToken = Environment.GetEnvironmentVariable("GACHA_SITE_PUBLISH_TOKEN") ?? string.Empty;
        if (string.IsNullOrEmpty(publisherToken) && File.Exists(credentialPath))
            LoadCredential(false);
        UpdateTokenFingerprint();
    }

    private void OnDisable()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Private Sites Content Publisher", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "The computer uploads verified ZIP files directly to the Site API. The game remains read-only. " +
            "The local token is never written to Assets, logs, catalog, runtime config, or the APK.",
            MessageType.Info);

        releaseRoot = EditorGUILayout.TextField("Release directory", releaseRoot);
        runtimeConfigPath = EditorGUILayout.TextField("Runtime config", runtimeConfigPath);
        credentialPath = EditorGUILayout.TextField("Local credential", credentialPath);
        siteBaseUrl = EditorGUILayout.TextField("Site base URL", siteBaseUrl);
        publisherToken = EditorGUILayout.PasswordField("Publisher token", publisherToken);
        UpdateTokenFingerprint();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(publishing);
            if (GUILayout.Button("Generate local credential"))
                GenerateCredential();
            if (GUILayout.Button("Load credential"))
                LoadCredential(true);
            EditorGUI.EndDisabledGroup();
        }

        if (!string.IsNullOrEmpty(tokenSha256))
        {
            EditorGUILayout.LabelField("Binding SHA-256", tokenSha256);
            if (GUILayout.Button("Copy binding SHA-256"))
                EditorGUIUtility.systemCopyBuffer = tokenSha256;
            EditorGUILayout.HelpBox(
                "Bind this SHA-256 once in the owner-only Site console. The raw token stays on this computer.",
                MessageType.None);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(publishing);
            if (GUILayout.Button("Offline preflight"))
                CreatePreview(true);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!publishing);
            if (GUILayout.Button("Cancel upload"))
                cancellation?.Cancel();
            EditorGUI.EndDisabledGroup();
        }

        if (preview != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preflight result", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Catalog", preview.CatalogUri.AbsoluteUri);
            EditorGUILayout.LabelField("Archives", preview.Archives.Count.ToString());
            EditorGUILayout.LabelField("Catalog SHA-256", preview.CatalogSha256);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(150f));
            foreach (R2ReleaseObject archive in preview.Archives)
                EditorGUILayout.SelectableLabel(archive.ObjectKey + "  (" + archive.Bytes + " bytes)", GUILayout.Height(18f));
            EditorGUILayout.EndScrollView();
        }

        EditorGUI.BeginDisabledGroup(publishing || !CanPublish());
        if (GUILayout.Button("Publish verified release to Site", GUILayout.Height(34f)))
            PublishAsync();
        EditorGUI.EndDisabledGroup();

        if (!CanPublish())
        {
            EditorGUILayout.HelpBox(
                "Generate or load a local credential before publishing. Bind its SHA-256 in the Site console once.",
                MessageType.Warning);
        }
    }

    private void GenerateCredential()
    {
        if (File.Exists(credentialPath) && !EditorUtility.DisplayDialog(
                "Rotate local publisher credential",
                "Replace the current local token? The Site console must bind the new SHA-256 before publishing again.",
                "Rotate",
                "Cancel"))
            return;
        try
        {
            Uri siteUri = ParseSiteBaseUri();
            SitesPublisherCredential credential = SitesPublisherCredentialStore.GenerateAndSave(credentialPath, siteUri);
            publisherToken = credential.PublisherToken;
            siteBaseUrl = credential.SiteBaseUri.AbsoluteUri.TrimEnd('/');
            UpdateTokenFingerprint();
            EditorGUIUtility.systemCopyBuffer = tokenSha256;
            EditorUtility.DisplayDialog(
                "Local credential generated",
                "The binding SHA-256 was copied. Paste it into the owner-only Site console; the raw token remains in LocalContent.",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Credential generation failed", exception.Message, "OK");
        }
    }

    private void LoadCredential(bool showDialog)
    {
        try
        {
            SitesPublisherCredential credential = SitesPublisherCredentialStore.Load(credentialPath);
            publisherToken = credential.PublisherToken;
            siteBaseUrl = credential.SiteBaseUri.AbsoluteUri.TrimEnd('/');
            UpdateTokenFingerprint();
            if (showDialog)
                EditorUtility.DisplayDialog("Credential loaded", "The local publisher credential is ready.", "OK");
        }
        catch (Exception exception)
        {
            if (showDialog)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Credential load failed", exception.Message, "OK");
            }
        }
    }

    private bool CanPublish()
    {
        try
        {
            new SitesContentApiCredentials(ParseSiteBaseUri(), publisherToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool CreatePreview(bool showDialog)
    {
        try
        {
            Uri publicContentBase = new Uri(ParseSiteBaseUri(), "api/content/");
            preview = R2ReleasePublisher.CreatePlan(new R2ReleasePublishRequest(
                releaseRoot,
                publicContentBase,
                string.Empty,
                runtimeConfigPath));
            Debug.Log(
                "Sites offline preflight passed: archives=" + preview.Archives.Count +
                ", catalog='" + preview.CatalogUri + "', sha256=" + preview.CatalogSha256 + ".");
            if (showDialog)
                EditorUtility.DisplayDialog("Sites preflight passed", "Verified " + preview.Archives.Count + " archives.", "OK");
            return true;
        }
        catch (Exception exception)
        {
            preview = null;
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Sites preflight failed", exception.Message, "OK");
            return false;
        }
    }

    private async void PublishAsync()
    {
        if (!CreatePreview(false))
            return;
        if (!EditorUtility.DisplayDialog(
                "Publish private content",
                "Upload " + preview.Archives.Count + " archives through the paired computer API? Catalog is written last.",
                "Publish",
                "Cancel"))
            return;

        publishing = true;
        cancellation = new CancellationTokenSource();
        Repaint();
        try
        {
            var credentials = new SitesContentApiCredentials(ParseSiteBaseUri(), publisherToken);
            using (var store = new SitesContentApiObjectStore(credentials, TimeSpan.FromMinutes(5)))
            {
                R2ReleasePublishResult result = await new R2ReleasePublisher(store)
                    .PublishAsync(preview, cancellation.Token);
                Debug.Log(
                    "Sites publication passed: uploaded=" + result.UploadedArchives +
                    ", reused=" + result.ReusedArchives + ", catalog='" + result.CatalogUri + "'.");
                EditorUtility.DisplayDialog(
                    "Sites publication passed",
                    "Uploaded " + result.UploadedArchives + ", reused " + result.ReusedArchives +
                    ". Public catalog and runtime config verified.",
                    "OK");
            }
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("Sites publication cancelled. Catalog is written only after all archives pass verification.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Sites publication failed", exception.Message, "OK");
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            publishing = false;
            Repaint();
        }
    }

    private Uri ParseSiteBaseUri()
    {
        if (!Uri.TryCreate(siteBaseUrl, UriKind.Absolute, out Uri uri))
            throw new InvalidDataException("Site base URL is invalid.");
        return new SitesContentApiCredentials(uri, string.IsNullOrEmpty(publisherToken) ? new string('A', 43) : publisherToken)
            .SiteBaseUri;
    }

    private void UpdateTokenFingerprint()
    {
        tokenSha256 = string.IsNullOrEmpty(publisherToken)
            ? string.Empty
            : SitesPublisherCredential.ComputeSha256(publisherToken);
    }
}

public static class PrivateSitesPublisherBatch
{
    private const string DefaultSiteBaseUrl = "https://universal-gacha-content.jiejingleek.chatgpt.site";
    private const string SiteBaseUrlVariable = "GACHA_SITE_BASE_URL";
    private const string PublisherTokenVariable = "GACHA_SITE_PUBLISH_TOKEN";
    private const string CredentialPathVariable = "GACHA_SITE_CREDENTIAL_PATH";

    public static void GenerateCredentialFromEnvironment()
    {
        string credentialPath = ResolveCredentialPath();
        if (File.Exists(credentialPath))
        {
            throw new InvalidOperationException(
                "Refusing to replace the existing Sites publisher credential: " + credentialPath);
        }

        SitesPublisherCredential credential = SitesPublisherCredentialStore.GenerateAndSave(
            credentialPath,
            ResolveConfiguredSiteBaseUri());
        Debug.Log(
            "Sites batch credential generated at the ignored local credential path. " +
            "Bind this SHA-256 in the owner console: " + credential.TokenSha256 + ".");
    }

    public static void PreflightFromEnvironment()
    {
        R2ReleaseUploadPlan plan = CreatePlan(ResolveSiteBaseUri());
        Debug.Log(
            "Sites batch offline preflight passed: archives=" + plan.Archives.Count +
            ", catalog='" + plan.CatalogUri + "', sha256=" + plan.CatalogSha256 + ".");
    }

    public static void PublishFromEnvironment()
    {
        SitesPublisherCredential credential = ResolveCredential();
        R2ReleaseUploadPlan plan = CreatePlan(credential.SiteBaseUri);
        using (var store = new SitesContentApiObjectStore(
                   new SitesContentApiCredentials(credential.SiteBaseUri, credential.PublisherToken),
                   TimeSpan.FromMinutes(5)))
        {
            R2ReleasePublishResult result = new R2ReleasePublisher(store)
                .PublishAsync(plan)
                .GetAwaiter()
                .GetResult();
            Debug.Log(
                "Sites batch publication passed: uploaded=" + result.UploadedArchives +
                ", reused=" + result.ReusedArchives + ", catalog='" + result.CatalogUri + "'.");
        }
    }

    private static R2ReleaseUploadPlan CreatePlan(Uri siteBaseUri)
    {
        string runtimeConfig = Path.Combine(ResolveProjectRoot(), "LocalContent", "remote-content.json");
        return R2ReleasePublisher.CreatePlan(new R2ReleasePublishRequest(
            ContentPackagePublisherBatch.DefaultReleaseRoot,
            new Uri(siteBaseUri, "api/content/"),
            string.Empty,
            runtimeConfig));
    }

    private static Uri ResolveSiteBaseUri()
    {
        string configuredSiteBaseUrl = Environment.GetEnvironmentVariable(SiteBaseUrlVariable);
        if (!string.IsNullOrWhiteSpace(configuredSiteBaseUrl))
            return ValidateSiteBaseUri(configuredSiteBaseUrl.Trim());

        string credentialPath = ResolveCredentialPath();
        if (File.Exists(credentialPath))
            return SitesPublisherCredentialStore.Load(credentialPath).SiteBaseUri;

        return ValidateSiteBaseUri(DefaultSiteBaseUrl);
    }

    private static Uri ResolveConfiguredSiteBaseUri()
    {
        string value = Environment.GetEnvironmentVariable(SiteBaseUrlVariable) ?? DefaultSiteBaseUrl;
        return ValidateSiteBaseUri(value.Trim());
    }

    private static Uri ValidateSiteBaseUri(string value)
    {
        string placeholderToken = new string('A', 43);
        return new SitesContentApiCredentials(new Uri(value), placeholderToken).SiteBaseUri;
    }

    private static SitesPublisherCredential ResolveCredential()
    {
        string token = Environment.GetEnvironmentVariable(PublisherTokenVariable);
        if (!string.IsNullOrWhiteSpace(token))
            return new SitesPublisherCredential(ResolveConfiguredSiteBaseUri(), token.Trim());
        return SitesPublisherCredentialStore.Load(ResolveCredentialPath());
    }

    private static string ResolveCredentialPath()
    {
        string configuredPath = Environment.GetEnvironmentVariable(CredentialPathVariable);
        return string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(ResolveProjectRoot(), "LocalContent", "site-publisher-credential.json")
            : Path.GetFullPath(configuredPath.Trim());
    }

    private static string ResolveProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
    }
}
