using AzuCraftyBoxes.IContainers;
using AzuCraftyBoxes.Util.Functions;

namespace AzuCraftyBoxes.Patches;

public static class PullBuildItems
{
    internal static bool IsBound()
    {
        return AzuCraftyBoxesPlugin.pullItemsKey.Value.MainKey != KeyCode.None || AzuCraftyBoxesPlugin.pullItemsGamepadKey.Value.IsBound();
    }

    internal static string Hint()
    {
        string keyboardHint = AzuCraftyBoxesPlugin.pullItemsKey.Value.MainKey != KeyCode.None ? AzuCraftyBoxesPlugin.pullItemsKey.Value.ToString() : string.Empty;
        string gamepadHint = AzuCraftyBoxesPlugin.pullItemsGamepadKey.Value.IsBound()
            ? $"{AzuCraftyBoxesPlugin.GamepadButton.AlternateFunction.DisplayName()} + {AzuCraftyBoxesPlugin.pullItemsGamepadKey.Value.DisplayName()}"
            : string.Empty;

        if (ZInput.IsGamepadActive())
        {
            return gamepadHint.Length > 0 ? gamepadHint : keyboardHint;
        }

        return keyboardHint.Length > 0 ? keyboardHint : gamepadHint;
    }

    private static bool IsPressed()
    {
        if (AzuCraftyBoxesPlugin.pullItemsKey.Value.IsKeyDown()) return true;

        // Controller: hold the game's alternate function and press the configured button
        return AzuCraftyBoxesPlugin.pullItemsGamepadKey.Value.IsBound()
               && AzuCraftyBoxesPlugin.GamepadButton.AlternateFunction.IsButtonHeld()
               && AzuCraftyBoxesPlugin.pullItemsGamepadKey.Value.IsButtonDown();
    }

    // Face buttons double as vanilla actions (A = jump on the Alternative layouts), so swallow those while the chord is possible
    internal static bool SuppressJump(Player player)
    {
        if (MiscFunctions.ShouldPrevent() || !ZInput.IsGamepadActive()) return false;
        if (AzuCraftyBoxesPlugin.pullItemsGamepadKey.Value != AzuCraftyBoxesPlugin.GamepadButton.ButtonA) return false;

        return player.InPlaceMode() && AzuCraftyBoxesPlugin.GamepadButton.AlternateFunction.IsButtonHeld();
    }

    internal static void CheckKey(Player player)
    {
        if (MiscFunctions.ShouldPrevent() || !IsBound() || !player.InPlaceMode() || !player.TakeInput()) return;
        if (!IsPressed()) return;

        Piece? piece = player.GetSelectedPiece();
        if (!piece || piece.m_repairPiece) return;

        Pull(player, piece);
    }

    internal static void Pull(Player player, Piece piece)
    {
        Inventory inventory = player.GetInventory();
        List<IContainer> nearbyContainers = Boxes.QueryFrame.Get(player, AzuCraftyBoxesPlugin.mRange.Value);
        List<string> pulled = new();
        List<string> missing = new();

        foreach (Piece.Requirement requirement in piece.m_resources)
        {
            if (requirement?.m_resItem == null || requirement.m_amount <= 0) continue;
            if (!MiscFunctions.CheckItemDropIntegrity(requirement.m_resItem)) continue;

            string sharedName = requirement.m_resItem.m_itemData.m_shared.m_name;
            string prefabName = requirement.m_resItem.name;
            int needed = requirement.m_amount - inventory.CountItems(sharedName);
            if (needed <= 0) continue;

            int got = 0;
            foreach (IContainer c in nearbyContainers)
            {
                if (needed <= 0) break;
                if (!Boxes.CanItemBePulled(c.GetPrefabName(), prefabName)) continue;

                int allowed = Boxes.CheckAndDecrement(c.ItemCount(sharedName));
                if (allowed <= 0) continue;

                int moved = MoveToInventory(c, requirement.m_resItem, sharedName, Mathf.Min(needed, allowed), inventory);
                needed -= moved;
                got += moved;
            }

            if (got > 0) pulled.Add($"{got} {sharedName}");
            if (needed > 0) missing.Add($"{needed} {sharedName}");
        }

        if (pulled.Count == 0 && missing.Count == 0)
        {
            player.Message(MessageHud.MessageType.Center, "You already have everything for this piece");
            return;
        }

        string message = pulled.Count > 0 ? $"Pulled {string.Join(", ", pulled)}" : string.Empty;
        if (missing.Count > 0)
        {
            message += (message.Length > 0 ? "\n" : string.Empty) + $"Still missing {string.Join(", ", missing)}";
        }

        player.Message(MessageHud.MessageType.Center, Localization.instance.Localize(message));
    }

    private static int MoveToInventory(IContainer container, ItemDrop resItem, string sharedName, int amount, Inventory inventory)
    {
        if (amount <= 0) return 0;

        Inventory? cInventory = container.GetInventory();
        int moved = 0;

        if (cInventory != null)
        {
            // Move the real stacks so quality, variant and custom data come along
            List<ItemDrop.ItemData> items = cInventory.GetAllItems();
            for (int i = 0; i < items.Count && moved < amount; ++i)
            {
                ItemDrop.ItemData item = items[i];
                if (item?.m_shared?.m_name != sharedName) continue;

                int take = Mathf.Min(item.m_stack, amount - moved);
                if (take <= 0 || !inventory.CanAddItem(item, take)) continue;

                ItemDrop.ItemData clone = item.Clone();
                clone.m_stack = take;
                if (!inventory.AddItem(clone)) continue;

                cInventory.RemoveItem(item, take);
                moved += take;
                if (take == item.m_stack) --i; // stack removed, list shifted
            }
        }
        else
        {
            // Drawers only track a prefab and a count, so a fresh stack is created in the inventory
            int maxStack = Mathf.Max(1, resItem.m_itemData.m_shared.m_maxStackSize);
            int space = inventory.FindFreeStackSpace(sharedName, Game.m_worldLevel) + inventory.GetEmptySlots() * maxStack;
            int take = Mathf.Min(amount, space);
            if (take > 0 && inventory.AddItem(resItem.gameObject, take))
            {
                container.RemoveItem(sharedName, take);
                moved = take;
            }
        }

        if (moved > 0)
        {
            container.Save();
        }

        return moved;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
static class PullBuildItemsSuppressJumpPatch
{
    private static void Prefix(Player __instance, ref bool jump)
    {
        if (jump && PullBuildItems.SuppressJump(__instance))
        {
            jump = false;
        }
    }
}

[HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
static class PullBuildItemsSuppressMinimapZoomPatch
{
    private static bool Prefix(string name, ref bool __result)
    {
        if (name != "JoyMiniMapZoomIn" && name != "JoyMiniMapZoomOut") return true;

        Player player = Player.m_localPlayer;
        if (!player || !player.InPlaceMode()) return true;

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Hud), nameof(Hud.SetupPieceInfo))]
static class PullBuildItemsHintPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Hud __instance, Piece piece)
    {
        if (MiscFunctions.ShouldPrevent() || !PullBuildItems.IsBound()) return;
        if (!piece || piece.m_repairPiece || piece.m_resources.Length == 0 || !__instance.m_pieceDescription) return;

        string hint = Localization.instance.Localize($"\n[<color=yellow><b>{PullBuildItems.Hint()}</b></color>] Pull materials from nearby containers");
        if (!__instance.m_pieceDescription.text.EndsWith(hint))
        {
            __instance.m_pieceDescription.text += hint;
        }
    }
}
