using System;
using Gacha.Application;

public sealed class PlayerCollectionProgressStore : ICollectionProgressStore
{
    private readonly Inventory inventory;
    private readonly Action<InventoryData> saveLocal;

    public PlayerCollectionProgressStore()
        : this(null, LocalSaveService.Save)
    {
    }

    public PlayerCollectionProgressStore(Inventory inventory, Action<InventoryData> saveLocal)
    {
        this.inventory = inventory;
        this.saveLocal = saveLocal ?? throw new ArgumentNullException(nameof(saveLocal));
    }

    public CollectionItemProgress GetProgress(string printingId)
    {
        if (string.IsNullOrWhiteSpace(printingId))
            throw new ArgumentException("A printing id is required.", nameof(printingId));

        Inventory target = RequiredInventory();
        return new CollectionItemProgress(
            printingId,
            target.GetPrintingCount(printingId),
            target.IsPrintingUnseen(printingId));
    }

    public bool MarkSeen(string printingId)
    {
        if (string.IsNullOrWhiteSpace(printingId))
            throw new ArgumentException("A printing id is required.", nameof(printingId));

        Inventory target = RequiredInventory();
        if (!target.IsPrintingUnseen(printingId))
            return false;

        InventoryData rollback = InventoryData.FromSnapshot(target.Data.ToSnapshot());
        try
        {
            target.MarkPrintingSeen(printingId);
            saveLocal(target.Data);
            return true;
        }
        catch
        {
            target.ReplaceData(rollback);
            throw;
        }
    }

    private Inventory RequiredInventory()
    {
        Inventory target = inventory != null ? inventory : Inventory.Instance;
        if (target == null)
            throw new InvalidOperationException("Inventory is not initialized.");
        return target;
    }
}
