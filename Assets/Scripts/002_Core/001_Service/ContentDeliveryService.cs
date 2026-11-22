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
    private Task initialization;

    public Task InitializeAsync()
    {
        if (initialization == null || initialization.IsFaulted || initialization.IsCanceled)
            initialization = ReadAndReleaseAsync(Addressables.InitializeAsync(false), "Addressables initialization");
        return initialization;
    }

    public async Task<long> GetDownloadSizeAsync(string label)
    {
        await InitializeAsync();
        return await ReadAndReleaseAsync(Addressables.GetDownloadSizeAsync(label), $"Download size lookup for '{label}'");
    }

    public async Task DownloadAsync(string label, IProgress<float> progress = null)
    {
        await InitializeAsync();
        AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(label, false);
        try
        {
            while (!handle.IsDone)
            {
                progress?.Report(handle.GetDownloadStatus().Percent);
                await Task.Yield();
            }
            EnsureSucceeded(handle, $"Content download for '{label}'");
            progress?.Report(1f);
        }
        finally
        {
            Addressables.Release(handle);
        }
    }

    public async Task<bool> UpdateCatalogAsync()
    {
        await InitializeAsync();
        List<string> catalogs = await ReadAndReleaseAsync(Addressables.CheckForCatalogUpdates(false), "Remote catalog check");
        if (catalogs == null || catalogs.Count == 0)
            return false;

        await ReadAndReleaseAsync(Addressables.UpdateCatalogs(catalogs, false), "Remote catalog update");
        return true;
    }

    private static async Task<T> ReadAndReleaseAsync<T>(AsyncOperationHandle<T> handle, string operation)
    {
        try
        {
            while (!handle.IsDone)
                await Task.Yield();
            EnsureSucceeded(handle, operation);
            return handle.Result;
        }
        finally
        {
            Addressables.Release(handle);
        }
    }

    private static void EnsureSucceeded(AsyncOperationHandle handle, string operation)
    {
        if (handle.Status != AsyncOperationStatus.Succeeded)
            throw new InvalidOperationException($"{operation} failed.", handle.OperationException);
    }
}
