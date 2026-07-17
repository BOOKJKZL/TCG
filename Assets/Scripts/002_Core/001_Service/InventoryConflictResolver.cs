/// <summary>
/// Replaceable policy for reconciling the local and remote inventory snapshots.
/// </summary>
public interface IInventoryConflictResolver
{
    InventoryData Resolve(InventoryData local, InventoryData remote);
}

public sealed class LatestWriteWinsInventoryConflictResolver : IInventoryConflictResolver
{
    public InventoryData Resolve(InventoryData local, InventoryData remote)
    {
        local = local ?? new InventoryData();
        remote = remote ?? new InventoryData();

        if (!local.HasProgress && remote.HasProgress)
            return remote;
        if (local.HasProgress && !remote.HasProgress)
            return local;

        // Prefer the remote snapshot on an exact tie so an existing cloud save is
        // not accidentally replaced by a freshly-created empty local file.
        return local.LastModifiedUtcTicks > remote.LastModifiedUtcTicks ? local : remote;
    }
}
