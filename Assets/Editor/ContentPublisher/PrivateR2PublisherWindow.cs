using System;
using System.IO;
using System.Threading;
using Gacha.EditorTools.Content;
using UnityEditor;
using UnityEngine;

public sealed class PrivateR2PublisherWindow : EditorWindow
{
    private string releaseRoot;
    private string runtimeConfigPath;
    private string s3Endpoint;
    private string bucketName;
    private string publicBaseUrl;
    private string objectPrefix = "releases/android";
    private string accessKeyId;
    private string secretAccessKey;
    private Vector2 scroll;
    private R2ReleaseUploadPlan preview;
    private CancellationTokenSource cancellation;
    private bool publishing;

    [MenuItem("Tools/Universal Gacha/Private R2 Publisher")]
    private static void Open()
    {
        GetWindow<PrivateR2PublisherWindow>("Private R2 Publisher");
    }

    private void OnEnable()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        releaseRoot = ContentPackagePublisherBatch.DefaultReleaseRoot;
        runtimeConfigPath = Path.Combine(projectRoot, "LocalContent", "remote-content.json");
        s3Endpoint = Environment.GetEnvironmentVariable("GACHA_R2_S3_ENDPOINT") ?? string.Empty;
        bucketName = Environment.GetEnvironmentVariable("GACHA_R2_BUCKET") ?? string.Empty;
        publicBaseUrl = Environment.GetEnvironmentVariable("GACHA_R2_PUBLIC_BASE_URL") ?? string.Empty;
        objectPrefix = Environment.GetEnvironmentVariable("GACHA_R2_OBJECT_PREFIX") ?? objectPrefix;
        accessKeyId = Environment.GetEnvironmentVariable("GACHA_R2_ACCESS_KEY_ID") ?? string.Empty;
        secretAccessKey = Environment.GetEnvironmentVariable("GACHA_R2_SECRET_ACCESS_KEY") ?? string.Empty;
    }

    private void OnDisable()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Private Cloudflare R2 Release", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Credentials remain in this Editor process and are never saved in Assets. Preview is offline; publishing writes immutable ZIP files first and catalog.json last.",
            MessageType.Info);

        releaseRoot = EditorGUILayout.TextField("Release directory", releaseRoot);
        runtimeConfigPath = EditorGUILayout.TextField("Runtime config", runtimeConfigPath);
        s3Endpoint = EditorGUILayout.TextField("R2 S3 endpoint", s3Endpoint);
        bucketName = EditorGUILayout.TextField("R2 bucket", bucketName);
        publicBaseUrl = EditorGUILayout.TextField("Public HTTPS base", publicBaseUrl);
        objectPrefix = EditorGUILayout.TextField("Object prefix", objectPrefix);
        accessKeyId = EditorGUILayout.PasswordField("Access key id", accessKeyId);
        secretAccessKey = EditorGUILayout.PasswordField("Secret access key", secretAccessKey);

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
                EditorGUILayout.SelectableLabel($"{archive.ObjectKey}  ({archive.Bytes} bytes)", GUILayout.Height(18f));
            EditorGUILayout.EndScrollView();
        }

        EditorGUI.BeginDisabledGroup(publishing || !CanPublish());
        if (GUILayout.Button("Publish verified release to R2", GUILayout.Height(34f)))
            PublishAsync();
        EditorGUI.EndDisabledGroup();

        if (!CanPublish())
        {
            EditorGUILayout.HelpBox(
                "Real publishing requires the R2 S3 endpoint, bucket, public HTTPS base URL, access key id, and secret access key.",
                MessageType.Warning);
        }
    }

    private bool CanPublish()
    {
        return Uri.TryCreate(s3Endpoint, UriKind.Absolute, out Uri endpoint) &&
               string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(bucketName) &&
               Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out Uri publicUri) &&
               string.Equals(publicUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(accessKeyId) &&
               !string.IsNullOrWhiteSpace(secretAccessKey);
    }

    private bool CreatePreview(bool showDialog)
    {
        try
        {
            if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out Uri publicUri))
                throw new InvalidDataException("Public HTTPS base URL is invalid.");
            preview = R2ReleasePublisher.CreatePlan(new R2ReleasePublishRequest(
                releaseRoot,
                publicUri,
                objectPrefix,
                runtimeConfigPath));
            Debug.Log(
                $"R2 offline preflight passed: archives={preview.Archives.Count}, " +
                $"catalog='{preview.CatalogUri}', sha256={preview.CatalogSha256}.");
            if (showDialog)
                EditorUtility.DisplayDialog("R2 preflight passed", $"Verified {preview.Archives.Count} archives.", "OK");
            return true;
        }
        catch (Exception exception)
        {
            preview = null;
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("R2 preflight failed", exception.Message, "OK");
            return false;
        }
    }

    private async void PublishAsync()
    {
        if (!CreatePreview(false))
            return;
        if (!EditorUtility.DisplayDialog(
                "Publish private content",
                $"Write {preview.Archives.Count} archives and catalog to bucket '{bucketName}'? Catalog will be written last.",
                "Publish",
                "Cancel"))
            return;

        publishing = true;
        cancellation = new CancellationTokenSource();
        Repaint();
        try
        {
            var credentials = new CloudflareR2Credentials(
                new Uri(s3Endpoint),
                bucketName,
                accessKeyId,
                secretAccessKey);
            using (var store = new CloudflareR2ObjectStore(credentials, TimeSpan.FromMinutes(5)))
            {
                var publisher = new R2ReleasePublisher(store);
                R2ReleasePublishResult result = await publisher.PublishAsync(preview, cancellation.Token);
                Debug.Log(
                    $"R2 publication passed: uploaded={result.UploadedArchives}, reused={result.ReusedArchives}, " +
                    $"catalog='{result.CatalogUri}', runtimeConfig='{result.RuntimeConfigPath}'.");
                EditorUtility.DisplayDialog(
                    "R2 publication passed",
                    $"Uploaded {result.UploadedArchives}, reused {result.ReusedArchives}. Public catalog and runtime config verified.",
                    "OK");
            }
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("R2 publication cancelled before completion. Catalog is written only after all archives pass verification.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("R2 publication failed", exception.Message, "OK");
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            publishing = false;
            Repaint();
        }
    }
}

public static class PrivateR2PublisherBatch
{
    public static void PreflightFromEnvironment()
    {
        R2ReleaseUploadPlan plan = CreatePlanFromEnvironment(true);
        Debug.Log(
            $"R2 batch offline preflight passed: archives={plan.Archives.Count}, " +
            $"catalog='{plan.CatalogUri}', sha256={plan.CatalogSha256}.");
    }

    public static void PublishFromEnvironment()
    {
        string s3Endpoint = Required("GACHA_R2_S3_ENDPOINT");
        string bucket = Required("GACHA_R2_BUCKET");
        string accessKeyId = Required("GACHA_R2_ACCESS_KEY_ID");
        string secretAccessKey = Required("GACHA_R2_SECRET_ACCESS_KEY");
        R2ReleaseUploadPlan plan = CreatePlanFromEnvironment(false);
        using (var store = new CloudflareR2ObjectStore(
                   new CloudflareR2Credentials(new Uri(s3Endpoint), bucket, accessKeyId, secretAccessKey),
                   TimeSpan.FromMinutes(5)))
        {
            R2ReleasePublishResult result = new R2ReleasePublisher(store)
                .PublishAsync(plan)
                .GetAwaiter()
                .GetResult();
            Debug.Log(
                $"R2 batch publication passed: uploaded={result.UploadedArchives}, " +
                $"reused={result.ReusedArchives}, catalog='{result.CatalogUri}'.");
        }
    }

    private static R2ReleaseUploadPlan CreatePlanFromEnvironment(bool allowPlaceholderPublicUrl)
    {
        string publicBaseUrl = Environment.GetEnvironmentVariable("GACHA_R2_PUBLIC_BASE_URL");
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            if (!allowPlaceholderPublicUrl)
                publicBaseUrl = Required("GACHA_R2_PUBLIC_BASE_URL");
            else
                publicBaseUrl = "https://content.example.invalid";
        }
        string prefix = Environment.GetEnvironmentVariable("GACHA_R2_OBJECT_PREFIX") ?? "releases/android";
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string runtimeConfig = Path.Combine(projectRoot, "LocalContent", "remote-content.json");
        return R2ReleasePublisher.CreatePlan(new R2ReleasePublishRequest(
            ContentPackagePublisherBatch.DefaultReleaseRoot,
            new Uri(publicBaseUrl),
            prefix,
            runtimeConfig));
    }

    private static string Required(string name)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Required environment variable is missing: " + name);
        return value.Trim();
    }
}
