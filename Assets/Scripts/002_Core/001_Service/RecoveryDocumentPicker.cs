using System;
using System.IO;
using UnityEngine;

public sealed class RecoveryDocumentPickerResult
{
    public RecoveryDocumentPickerResult(bool succeeded, bool cancelled, string path, string error)
    {
        Succeeded = succeeded;
        Cancelled = cancelled;
        Path = path;
        Error = error;
    }

    public bool Succeeded { get; }
    public bool Cancelled { get; }
    public string Path { get; }
    public string Error { get; }
}

public sealed class RecoveryDocumentPicker : MonoBehaviour
{
    private const string JavaClass = "com.universalgacha.recovery.RecoveryDocumentBridge";
    private Action<RecoveryDocumentPickerResult> pendingCallback;

    public static RecoveryDocumentPicker GetOrCreate()
    {
        RecoveryDocumentPicker existing = FindFirstObjectByType<RecoveryDocumentPicker>();
        if (existing != null) return existing;
        var gameObject = new GameObject("RecoveryDocumentPicker");
        DontDestroyOnLoad(gameObject);
        return gameObject.AddComponent<RecoveryDocumentPicker>();
    }

    public void CreateDocument(
        string stagedSourcePath,
        string suggestedFileName,
        Action<RecoveryDocumentPickerResult> completed)
    {
        Begin(completed);
        if (Application.platform == RuntimePlatform.Android)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(JavaClass))
            {
                bridge.CallStatic(
                    "createDocument",
                    gameObject.name,
                    nameof(OnDocumentPickerResult),
                    suggestedFileName,
                    stagedSourcePath);
            }
            return;
#endif
        }

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "Recovery", "Exports");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, suggestedFileName);
            File.Copy(stagedSourcePath, destination, true);
            Finish(new RecoveryDocumentPickerResult(true, false, destination, null));
        }
        catch (Exception exception)
        {
            Finish(new RecoveryDocumentPickerResult(false, false, null, exception.Message));
        }
    }

    public void OpenDocument(
        string stagedDestinationPath,
        Action<RecoveryDocumentPickerResult> completed)
    {
        Begin(completed);
        if (Application.platform == RuntimePlatform.Android)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(JavaClass))
            {
                bridge.CallStatic(
                    "openDocument",
                    gameObject.name,
                    nameof(OnDocumentPickerResult),
                    stagedDestinationPath);
            }
            return;
#endif
        }

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "Recovery", "Exports");
            string source = Directory.Exists(directory)
                ? LatestExport(directory)
                : null;
            if (source == null)
            {
                Finish(new RecoveryDocumentPickerResult(
                    false,
                    false,
                    null,
                    "No exported recovery file is available in the local Recovery/Exports folder."));
                return;
            }
            string parent = Path.GetDirectoryName(stagedDestinationPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.Copy(source, stagedDestinationPath, true);
            Finish(new RecoveryDocumentPickerResult(true, false, stagedDestinationPath, null));
        }
        catch (Exception exception)
        {
            Finish(new RecoveryDocumentPickerResult(false, false, null, exception.Message));
        }
    }

    public void OnDocumentPickerResult(string json)
    {
        AndroidPickerResult result;
        try
        {
            result = JsonUtility.FromJson<AndroidPickerResult>(json);
        }
        catch (Exception exception)
        {
            Finish(new RecoveryDocumentPickerResult(false, false, null, exception.Message));
            return;
        }
        bool cancelled = result != null && string.Equals(result.error, "cancelled", StringComparison.OrdinalIgnoreCase);
        Finish(new RecoveryDocumentPickerResult(
            result != null && result.succeeded,
            cancelled,
            result?.path,
            result?.error));
    }

    private void Begin(Action<RecoveryDocumentPickerResult> completed)
    {
        if (completed == null) throw new ArgumentNullException(nameof(completed));
        if (pendingCallback != null)
            throw new InvalidOperationException("A recovery document picker operation is already active.");
        pendingCallback = completed;
    }

    private void Finish(RecoveryDocumentPickerResult result)
    {
        Action<RecoveryDocumentPickerResult> callback = pendingCallback;
        pendingCallback = null;
        callback?.Invoke(result);
    }

    private static string LatestExport(string directory)
    {
        string latest = null;
        DateTime latestWrite = DateTime.MinValue;
        foreach (string path in Directory.GetFiles(directory, "*.gachasave", SearchOption.TopDirectoryOnly))
        {
            DateTime write = File.GetLastWriteTimeUtc(path);
            if (latest == null || write > latestWrite)
            {
                latest = path;
                latestWrite = write;
            }
        }
        return latest;
    }

    [Serializable]
    private sealed class AndroidPickerResult
    {
        public bool succeeded;
        public string path;
        public string error;
    }
}
