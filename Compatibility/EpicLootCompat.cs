using AzuCraftyBoxes.IContainers;
using AzuCraftyBoxes.Util.Functions;
using EpicLootApi = EpicLootAPI.EpicLoot;

namespace AzuCraftyBoxes.Compatibility;

public static class EpicLootCompat
{
    private const string TablePrefabName = "piece_enchantingtable";

    internal static void Init(string providerId)
    {
        if (!EpicLootApi.IsLoaded()) return;
        EpicLootApi.RegisterInventoryProvider(providerId, GetItems, CountItem, RemoveItem, RemoveExactItem);
    }

    private static List<IContainer> Nearby() =>
        MiscFunctions.ShouldPrevent() ? [] : Boxes.QueryFrame.Get(Player.m_localPlayer, AzuCraftyBoxesPlugin.mRange.Value);

    // EpicLoot needs the live instances, not copies, so magic data survives the round trip.
    private static List<ItemDrop.ItemData> GetItems()
    {
        List<ItemDrop.ItemData> items = [];
        foreach (IContainer container in Nearby())
        {
            Inventory? inventory = container.GetInventory();
            if (inventory == null) continue;

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab == null) continue;
                if (!Boxes.CanItemBePulled(container.GetPrefabName(), item.m_dropPrefab.name, TablePrefabName)) continue;
                items.Add(item);
            }
        }

        return items;
    }

    private static int CountItem(string itemName)
    {
        int count = 0;
        string prefabName = PrefabNameOf(itemName);
        foreach (IContainer container in Nearby())
        {
            if (!Boxes.CanItemBePulled(container.GetPrefabName(), prefabName, TablePrefabName)) continue;
            count += Boxes.CheckAndDecrement(container.ItemCount(itemName));
        }

        return count;
    }

    private static string PrefabNameOf(string sharedName)
    {
        foreach (GameObject prefab in ObjectDB.instance.m_items)
        {
            if (prefab.TryGetComponent(out ItemDrop itemDrop) && itemDrop.m_itemData.m_shared.m_name == sharedName)
                return prefab.name;
        }

        return sharedName;
    }

    private static int RemoveItem(string itemName, int amount)
    {
        int removed = 0;
        string prefabName = PrefabNameOf(itemName);
        foreach (IContainer container in Nearby())
        {
            if (removed >= amount) break;
            if (!Boxes.CanItemBePulled(container.GetPrefabName(), prefabName, TablePrefabName)) continue;

            int available = Boxes.CheckAndDecrement(container.ItemCount(itemName));
            if (available <= 0) continue;

            int take = Math.Min(available, amount - removed);
            container.RemoveItem(itemName, take);
            container.Save();
            removed += take;
        }

        if (removed < amount)
            AzuCraftyBoxesPlugin.AzuCraftyBoxesLogger.LogIfReleaseAndDebugEnable($"Only removed {removed}/{amount} of '{itemName}' from containers.");

        return removed;
    }

    // Match by reference: a name match would consume the wrong enchanted item.
    private static int RemoveExactItem(ItemDrop.ItemData item, int amount)
    {
        foreach (IContainer container in Nearby())
        {
            Inventory? inventory = container.GetInventory();
            if (inventory == null) continue;
            if (item.m_dropPrefab != null && !Boxes.CanItemBePulled(container.GetPrefabName(), item.m_dropPrefab.name, TablePrefabName)) continue;

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            ItemDrop.ItemData? match = items.Contains(item) ? item : items.Find(i => i.m_shared.m_name == item.m_shared.m_name && i.m_quality == item.m_quality && i.m_variant == item.m_variant && i.m_worldLevel == item.m_worldLevel && i.m_customData.Count == item.m_customData.Count && !i.m_customData.Except(item.m_customData).Any());
            if (match == null) continue;

            int take = Math.Min(match.m_stack, amount);
            if (!inventory.RemoveItem(match, take)) continue;

            container.Save();
            return take;
        }

        return 0;
    }
}
