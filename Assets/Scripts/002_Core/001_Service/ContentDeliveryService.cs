using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Remote content boundary. UI code can ask for a content group without knowing
/// whether it comes from Unity CCD, Cloudflare R2, or another static HTTPS host.
/// </summary>
public interface IContentDeliveryService
{
    Task InitializeAsync();
    Task<long> GetDownloadSizeAsync(string label);
    Task DownloadAsync(string label, IProgress<float> progress = null);
    Task<bool> UpdateCatalogAsync();
}

public sealed class AddressablesContentDeliveryService : IContentDeliveryService
{
    private bool _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        AsyncOperationHandle handle = Addressables.InitializeAsync();
        await WaitForCompletionAsync(handle);
        EnsureSucceeded(handle, "Addressables initialization");
        Addressables.Release(handle);
        _initialized = true;
    }

    public async Task<long> GetDownloadSizeAsync(string label)
    {
        await InitializeAsync();
        AsyncOperationHandle<long> handle = Addressables.GetDownloadSizeAsync(label);
        await WaitForCompletionAsync(handle);
        EnsureSucceeded(handle, $"Download size lookup for '{label}'");
        long size = handle.Result;
        Addressables.Release(handle);
        return size;
    }

    public async Task DownloadAsync(string label, IProgress<float> progress = null)
    {
        await InitializeAsync();
        AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(label, false);
        while (!handle.IsDone)
        {
            progress?.Report(handle.GetDownloadStatus().Percent);
            await Task.Yield();
        }

        EnsureSucceeded(handle, $"Content download for '{label}'");
        progress?.Report(1f);
        Addressables.Release(handle);
    }

    public async Task<bool> UpdateCatalogAsync()
    {
        await InitializeAsync();
        AsyncOperationHandle<List<string>> checkHandle = Addressables.CheckForCatalogUpdates(false);
        await WaitForCompletionAsync(checkHandle);
        EnsureSucceeded(checkHandle, "Remote catalog check");

        List<string> catalogs = checkHandle.Result;
        if (catalogs == null || catalogs.Count == 0)
        {
            Addressables.Release(checkHandle);
            return false;
        }

        AsyncOperationHandle updateHandle = Addressables.UpdateCatalogs(catalogs, false);
        await WaitForCompletionAsync(updateHandle);
        EnsureSucceeded(updateHandle, "Remote catalog update");
        Addressables.Release(updateHandle);
        Addressables.Release(checkHandle);
        return true;
    }

    private static async Task WaitForCompletionAsync(AsyncOperationHandle handle)
    {
        while (!handle.IsDone)
            await Task.Yield();
    }

    private static void EnsureSucceeded(AsyncOperationHandle handle, string operation)
    {
        if (handle.Status != AsyncOperationStatus.Succeeded)
            throw new InvalidOperationException($"{operation} failed.", handle.OperationException);
    }
}
