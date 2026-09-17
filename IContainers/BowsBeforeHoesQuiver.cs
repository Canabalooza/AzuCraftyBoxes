using AzuCraftyBoxes.Compatibility;

namespace AzuCraftyBoxes.IContainers;

internal sealed class BowsBeforeHoesQuiver(ItemDrop.ItemData item, Inventory inventory) : IContainer
{
    public int ProcessContainerInventory(string reqName, int totalAmount, int totalRequirement)
    {
        for (int i = 0; i < inventory.m_inventory.Count && totalAmount < totalRequirement; ++i)
        {
            ItemDrop.ItemData storedItem = inventory.m_inventory[i];
            if (storedItem.m_shared.m_name != reqName) continue;

            int amount = Mathf.Min(storedItem.m_stack, totalRequirement - totalAmount);
            if (amount == storedItem.m_stack)
            {
                inventory.RemoveItem(i);
                --i;
            }
            else
            {
                storedItem.m_stack -= amount;
            }

            totalAmount += amount;
        }

        Save();
        return totalAmount;
    }

    public int ItemCount(string name) => inventory.CountItems(name);

    public void RemoveItem(string name, int amount)
    {
        inventory.RemoveItem(name, amount);
        Save();
    }

    public Vector3 GetPosition() => Player.m_localPlayer.transform.position;
    public string GetPrefabName() => item.m_dropPrefab?.name ?? "";
    public Inventory GetInventory() => inventory;

    public void Save()
    {
        inventory.Changed();
        BowsBeforeHoesCompat.Save(item);
    }
}
