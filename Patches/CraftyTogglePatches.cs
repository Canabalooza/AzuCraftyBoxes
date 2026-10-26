using AzuCraftyBoxes.APIs;
using AzuCraftyBoxes.Util;
using AzuCraftyBoxes.Util.Functions;
using TMPro;

namespace AzuCraftyBoxes.Patches;

public static class CraftyToggle
{
    internal static bool Enabled => AzuCraftyBoxesPlugin.chestToggleEnabled.Value.isOn();

    internal static bool ShowHover => Enabled && AzuCraftyBoxesPlugin.chestToggleHoverText.Value.isOn();

    internal static bool KeyHeld => AzuCraftyBoxesPlugin.chestToggleKey.Value.IsKeyHeld();

    internal static bool MineOnly => Player.m_localPlayer && Player.m_localPlayer.GetPullMode() == PullMode.MineOnly;

    internal static bool PullModeAllows(ZNetView? nview, bool mineOnly, long playerId)
    {
        if (!mineOnly) return true;

        ZDO? zdo = nview ? nview.GetZDO() : null;
        return zdo != null && zdo.GetLong(ZDOVars.s_creator, 0L) == playerId;
    }

    internal static void CheckToggleKey(Player player)
    {
        if (!Enabled || !AzuCraftyBoxesPlugin.chestToggleKey.Value.IsKeyDown() || !player.TakeInput()) return;

        GameObject hoverObject = player.GetHoverObject();
        if (!hoverObject) return;

        Container container = hoverObject.GetComponentInParent<Container>();
        if (Boxes.IsInList(container) && container.m_nview && container.m_nview.IsValid())
        {
            ToggleContainer(container, player);
            return;
        }

        ZNetView? nview = FindApiContainerView(hoverObject);
        if (nview) ToggleView(nview);
    }

    internal static void ToggleContainer(Container container, Humanoid character)
    {
        if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, true)) return;

        if (!container.CheckAccess(Game.instance.GetPlayerProfile().GetPlayerID()))
        {
            character.Message(MessageHud.MessageType.Center, "$msg_cantopen");
            return;
        }

        container.m_nview.InvokeRPC("RPC_UpdateCrafty");
    }

    internal static void ToggleView(ZNetView nview)
    {
        if (PrivateArea.CheckAccess(nview.transform.position, 0f, true))
        {
            nview.ToggleCrafty();
        }
    }

    internal static string HoverLine(bool crafty)
    {
        string mark = crafty ? "<color=#5FA052>\u2705\ufe0f</color>" : "<color=#bf4747>\u274e</color>";
        string keys = ZInput.IsGamepadActive()
            ? (!ZInput.IsNonClassicFunctionality() ? "$KEY_AltPlace + $KEY_Use" : "$KEY_AltKeys + $KEY_Use")
            : AzuCraftyBoxesPlugin.chestToggleKey.Value.ToString();
        return Localization.instance.Localize($"\n[<color=yellow><b>{keys}</b></color>] $hud_crafting $piece_chestwood {mark}");
    }

    internal static ZNetView? FindApiContainerView(GameObject go)
    {
        if (!go) return null;

        ZNetView nview = go.GetComponentInParent<ZNetView>();
        if (!nview || !nview.IsValid()) return null;
        if (!ItemDrawers_API.IsDrawer(nview) && !MkzItemDrawers_API.IsDrawer(nview)) return null;

        return nview;
    }
}

public static class ContainerExtensions
{
    internal static readonly int NoCraftyHash = "AzuCraftyBoxes_NoCrafty".GetStableHashCode();

    internal static bool IsCraftyView(this ZNetView nview)
    {
        if (!CraftyToggle.Enabled) return true;

        ZDO? zdo = nview ? nview.GetZDO() : null;
        return zdo != null && !zdo.GetBool(NoCraftyHash, false);
    }

    internal static void ToggleCrafty(this ZNetView nview)
    {
        if (!nview || !nview.IsValid()) return;

        nview.ClaimOwnership();
        ZDO zdo = nview.GetZDO();
        zdo.Set(NoCraftyHash, !zdo.GetBool(NoCraftyHash, false));
    }

    internal static bool IsCraftyContainer(this Container container)
    {
        if (!CraftyToggle.Enabled) return true;

        ZDO? zdo = container?.m_nview?.GetZDO();
        return zdo != null && !zdo.GetBool(NoCraftyHash, false);
    }

    public static void RPC_UpdateCrafty(this Container container, long sender)
    {
        container.UpdateCrafty();
    }

    public static void UpdateCrafty(this Container container)
    {
        if (!container.m_nview || !container.m_nview.IsValid() || !container.m_nview.IsOwner()) return;

        ZDO? zdo = container.m_nview.GetZDO();
        if (zdo == null) return;

        zdo.Set(NoCraftyHash, !zdo.GetBool(NoCraftyHash, false));
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.Awake))]
internal static class RegisterUpdateCrafty
{
    private static void Postfix(Container __instance)
    {
        if (!__instance.m_nview || __instance.m_nview.GetZDO() == null) return;

        __instance.m_nview.Register("RPC_UpdateCrafty", (Action<long>)__instance.RPC_UpdateCrafty);
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
internal static class BlockHoverTextDisplay
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Container __instance, ref string __result)
    {
        if (!CraftyToggle.ShowHover) return;
        if (!Boxes.IsInList(__instance) || !__instance.m_nview || !__instance.m_nview.IsValid()) return;
        if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position)) return;

        __result += CraftyToggle.HoverLine(__instance.IsCraftyContainer());
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.Interact))]
internal static class ChestBlockInteraction
{
    private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
    {
        if (hold || !CraftyToggle.Enabled) return true;
        if (!Boxes.IsInList(__instance) || !__instance.m_nview || !__instance.m_nview.IsValid()) return true;

        if (ZInput.IsGamepadActive())
        {
            if (!alt) return true;
            CraftyToggle.ToggleContainer(__instance, character);
        }
        else if (!CraftyToggle.KeyHeld)
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.Interact))]
internal static class ApiContainerBlockInteraction
{
    private static bool Prefix(GameObject go, bool hold, bool alt)
    {
        if (hold || !CraftyToggle.Enabled) return true;

        ZNetView? nview = CraftyToggle.FindApiContainerView(go);
        if (!nview) return true;

        if (ZInput.IsGamepadActive())
        {
            if (!alt) return true;
            CraftyToggle.ToggleView(nview);
            return false;
        }

        return !CraftyToggle.KeyHeld;
    }
}

[HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
internal static class ApiContainerHoverTextDisplay
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Hud __instance, Player player)
    {
        if (!CraftyToggle.ShowHover) return;
        if (!__instance.m_hoverName || __instance.m_hoverName.text.Length == 0) return;

        ZNetView? nview = CraftyToggle.FindApiContainerView(player.GetHoverObject());
        if (!nview || !PrivateArea.CheckAccess(nview.transform.position)) return;

        string text = CraftyToggle.HoverLine(nview.IsCraftyView());
        if (!__instance.m_hoverName.text.EndsWith(text))
        {
            __instance.m_hoverName.text += text;
        }
    }
}
