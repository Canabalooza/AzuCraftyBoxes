using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace AzuCraftyBoxes.Compatibility;

internal static class BowsBeforeHoesCompat
{
    private const string Guid = "Azumatt.BowsBeforeHoes";
    private static MethodInfo? getQuiverInventory;
    private static MethodInfo? saveQuiver;

    internal static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(Guid);

    internal static Inventory? GetInventory(ItemDrop.ItemData item)
    {
        if (!IsLoaded) return null;
        EnsureMethods();
        return getQuiverInventory?.Invoke(null, new object?[] { item }) as Inventory;
    }

    internal static void Save(ItemDrop.ItemData item)
    {
        EnsureMethods();
        saveQuiver?.Invoke(null, new object?[] { item });
    }

    private static void EnsureMethods()
    {
        if (getQuiverInventory != null) return;
        if (!Chainloader.PluginInfos.TryGetValue(Guid, out PluginInfo plugin)) return;

        Type? api = plugin.Instance.GetType().Assembly.GetType("BowsBeforeHoes.API");
        if (api == null) return;
        getQuiverInventory = AccessTools.Method(api, "GetQuiverInventory", new[] { typeof(ItemDrop.ItemData) });
        saveQuiver = AccessTools.Method(api, "SaveQuiver", new[] { typeof(ItemDrop.ItemData) });
    }
}
