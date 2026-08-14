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
    private string pendingRequestId;
    private bool pendingAbandoned;

    public bool IsBusy => !string.IsNullOrWhiteSpace(pendingRequestId);
    public event Action<bool> BusyChanged;

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
        string requestId = Begin(completed);
        if (Application.platform == RuntimePlatform.Android)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(JavaClass))
            {
                bridge.CallStatic(
                    "createDocument",
                    gameObject.name,
                    nameof(OnDocumentPickerResult),
                    requestId,
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
            Finish(requestId, new RecoveryDocumentPickerResult(true, false, destination, null));
        }
        catch (Exception exception)
        {
            Finish(requestId, new RecoveryDocumentPickerResult(false, false, null, exception.Message));
        }
    }

    public void OpenDocument(
        string stagedDestinationPath,
        Action<RecoveryDocumentPickerResult> completed)
    {
        string requestId = Begin(completed);
        if (Application.platform == RuntimePlatform.Android)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(JavaClass))
            {
                bridge.CallStatic(
                    "openDocument",
                    gameObject.name,
                    nameof(OnDocumentPickerResult),
                    requestId,
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
                Finish(requestId, new RecoveryDocumentPickerResult(
                    false,
                    false,
                    null,
                    "No exported recovery file is available in the local Recovery/Exports folder."));
                return;
            }
            string parent = Path.GetDirectoryName(stagedDestinationPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.Copy(source, stagedDestinationPath, true);
            Finish(requestId, new RecoveryDocumentPickerResult(true, false, stagedDestinationPath, null));
        }
        catch (Exception exception)
        {
            Finish(requestId, new RecoveryDocumentPickerResult(false, false, null, exception.Message));
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
            Debug.LogWarning("Document picker returned an unreadable result: " + exception.Message);
            return;
        }
        if (result == null || string.IsNullOrWhiteSpace(result.requestId) ||
            !string.Equals(result.requestId, pendingRequestId, StringComparison.Ordinal))
        {
            return;
        }
        bool cancelled = result != null && string.Equals(result.error, "cancelled", StringComparison.OrdinalIgnoreCase);
        Finish(result.requestId, new RecoveryDocumentPickerResult(
            result != null && result.succeeded,
            cancelled,
            result?.path,
            result?.error));
    }

    /// <summary>
    /// Relinquishes ownership of an outstanding platform picker result. The
    /// Android activity may still finish, but its late result will be ignored.
    /// </summary>
    public void CancelPending()
    {
        if (!IsBusy)
            return;
        pendingCallback = null;
        pendingAbandoned = true;
    }

    private string Begin(Action<RecoveryDocumentPickerResult> completed)
    {
        if (completed == null) throw new ArgumentNullException(nameof(completed));
        if (IsBusy)
            throw new InvalidOperationException("A recovery document picker operation is already active.");
        pendingRequestId = Guid.NewGuid().ToString("N");
        pendingCallback = completed;
        pendingAbandoned = false;
        BusyChanged?.Invoke(true);
        return pendingRequestId;
    }

    private void Finish(string requestId, RecoveryDocumentPickerResult result)
    {
        if (string.IsNullOrWhiteSpace(requestId) ||
            !string.Equals(requestId, pendingRequestId, StringComparison.Ordinal))
        {
            return;
        }
        Action<RecoveryDocumentPickerResult> callback = pendingAbandoned ? null : pendingCallback;
        pendingCallback = null;
        pendingRequestId = null;
        pendingAbandoned = false;
        BusyChanged?.Invoke(false);
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
        public string requestId;
        public bool succeeded;
        public string path;
        public string error;
    }
}
