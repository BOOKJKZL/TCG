using System;
using System.Collections.Generic;
using Gacha.Application;
using Gacha.Domain;

public sealed class PlayerInventoryProgressStore : IInventoryProgressStore
{
    private readonly Inventory inventory;
    private readonly Action<InventoryData> saveLocal;

    public PlayerInventoryProgressStore()
        : this(null, LocalSaveService.Save)
    {
    }

    public PlayerInventoryProgressStore(Inventory inventory, Action<InventoryData> saveLocal)
    {
        this.inventory = inventory;
        this.saveLocal = saveLocal ?? throw new ArgumentNullException(nameof(saveLocal));
    }

    public int GetProductsOpened(string productId)
    {
        return RequiredInventory().GetProductsOpened(productId);
    }

    public ProductInventoryCommit Commit(ProductDrawResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        Inventory target = RequiredInventory();
        InventoryData rollback = InventoryData.FromSnapshot(target.Data.ToSnapshot());
        var awards = new List<InventoryAward>(result.Printings.Count);
        try
        {
            foreach (DrawnPrinting drawn in result.Printings)
            {
                int previous = target.GetPrintingCount(drawn.PrintingId);
                target.AddPrinting(drawn.PrintingId);
                awards.Add(new InventoryAward(drawn.PrintingId, previous, previous + 1));
            }

            target.IncrementProductCounter(result.ProductId);
            saveLocal(target.Data);
            return new ProductInventoryCommit(
                result.ProductId,
                target.GetProductsOpened(result.ProductId),
                awards.AsReadOnly());
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
