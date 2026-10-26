using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AzuCraftyBoxes.Compatibility;
using AzuCraftyBoxes.IContainers;
using AzuCraftyBoxes.Patches;
using Backpacks;
using ItemDataManager;
using UnityEngine.Pool;
using static AzuCraftyBoxes.Patches.CacheCurrentCraftingStationPrefabName;

namespace AzuCraftyBoxes.Util.Functions;

public class Boxes
{
    internal struct Tally
    {
        public int Sum;
        public int Having;
    }

    private readonly struct PullKey : IEquatable<PullKey>
    {
        private readonly string _sharedName;
        private readonly string _itemPrefab;

        public PullKey(string sharedName, string itemPrefab)
        {
            _sharedName = sharedName;
            _itemPrefab = itemPrefab;
        }

        public bool Equals(PullKey other) => _sharedName == other._sharedName && _itemPrefab == other._itemPrefab;

        public override bool Equals(object? obj) => obj is PullKey other && Equals(other);

        public override int GetHashCode() => (_sharedName.GetHashCode() * 397) ^ _itemPrefab.GetHashCode();
    }

    internal static readonly HashSet<Container> Containers = new();
    private static readonly HashSet<Container> ContainersToAdd = new();
    private static readonly HashSet<Container> ContainersToRemove = new();
    private static int _lastRegistryFrame; // so we only flush once per frame
    private static ConcurrentDictionary<float, Stopwatch> stopwatches = new ConcurrentDictionary<float, Stopwatch>();

    internal static void AddContainer(Container container)
    {
        if (!container) return;
        if (!Containers.Contains(container))
        {
            ContainersToAdd.Add(container);
            if (container)
                AzuCraftyBoxesPlugin.AzuCraftyBoxesLogger.LogIfReleaseAndDebugEnable($"Added container {container.name} to list");
        }
    }

    internal static void RemoveContainer(Container container)
    {
        if (!container) return;
        if (Containers.Contains(container))
        {
            ContainersToRemove.Add(container);
            if (container)
                AzuCraftyBoxesPlugin.AzuCraftyBoxesLogger.LogIfReleaseAndDebugEnable($"Removed container {container.name} from list");
        }
    }

    internal static bool IsInList(Container? container)
    {
        return container && Containers.Contains(container);
    }

    private static void FlushRegistryIfNeeded()
    {
        int f = Time.frameCount;
        if (_lastRegistryFrame == f) return;
        _lastRegistryFrame = f;

        if (ContainersToAdd.Count > 0)
        {
            foreach (var c in ContainersToAdd) Containers.Add(c);
            ContainersToAdd.Clear();
        }

        if (ContainersToRemove.Count > 0)
        {
            foreach (var c in ContainersToRemove) Containers.Remove(c);
            ContainersToRemove.Clear();
        }
    }

    internal static void UpdateContainers()
    {
        foreach (Container container in ContainersToAdd)
        {
            Containers.Add(container);
        }

        ContainersToAdd.Clear();
        foreach (Container container in ContainersToRemove)
        {
            Containers.Remove(container);
        }

        ContainersToRemove.Clear();
    }

    private static readonly List<IContainer> _scratchNearby = new(256);
    private static readonly List<IContainer> _scratchkgDrawers = new(128);
    private static readonly List<IContainer> _scratchmkzDrawers = new(128);
    private static readonly List<IContainer> _scratchBackpacks = new(32);
    private static readonly List<IContainer> _scratchQuivers = new(8);
    private static readonly List<IContainer> _scratchGemBags = new(32);

    private static Vector3 _lastQueryPos = Vector3.positiveInfinity;
    private static float _lastQueryRange;
    private static float _lastQueryTime;
    private static readonly float _cacheWindow = 0.25f; // tweakable

    // The fully built cached list:
    private static readonly List<IContainer> _cachedAll = new(256);

    private static readonly Dictionary<string, Tally> _talliesAny = new(64);
    private static readonly Dictionary<PullKey, Tally> _talliesPullable = new(64);
    private static readonly ConditionalWeakTable<Inventory, object> _watched = new();
    private static readonly object _watchedMarker = new();
    private static int _vanillaCount;
    private static int _cachedAllCount;
    private static int _countsVersion;
    private static int _memoVersion = -1;
    private static int _lastDropFrame = -1;
    private static int _dropStreak;
    private static string _memoStation = string.Empty;
    private static object? _memoYaml;

    internal static void InvalidateCounts() => _countsVersion++;

    private static void Watch(Inventory? inventory)
    {
        if (inventory == null || _watched.TryGetValue(inventory, out _)) return;
        _watched.Add(inventory, _watchedMarker);
        inventory.m_onChanged += InvalidateCounts;
    }

    private static bool MemoUsable(List<IContainer> containers)
    {
        if (containers != _cachedAll || containers.Count != _cachedAllCount)
            return false;

        int frame = Time.frameCount;
        if (_memoVersion != _countsVersion || _memoYaml != AzuCraftyBoxesPlugin.yamlData || _memoStation != CacheCurrentCraftingStationPrefabName.CachedStationName)
        {
            _talliesAny.Clear();
            _talliesPullable.Clear();
            _memoVersion = _countsVersion;
            _memoYaml = AzuCraftyBoxesPlugin.yamlData;
            _memoStation = CacheCurrentCraftingStationPrefabName.CachedStationName;
            if (frame != _lastDropFrame)
            {
                _dropStreak = frame - _lastDropFrame == 1 ? _dropStreak + 1 : 0;
                _lastDropFrame = frame;
            }
        }

        if (_dropStreak >= 3)
            return frame - _lastDropFrame > 1;
        return true;
    }

    internal static int TallyAny(List<IContainer> containers, string sharedName, out Tally tally)
    {
        tally = default;
        if (!MemoUsable(containers))
            return 0;
        if (_talliesAny.TryGetValue(sharedName, out tally))
            return _vanillaCount;

        for (int i = 0; i < _vanillaCount; i++)
        {
            IContainer container = containers[i];
            if (container == null) continue;
            int count = container.ItemCount(sharedName);
            if (count <= 0) continue;
            tally.Having++;
            tally.Sum += count;
        }

        _talliesAny[sharedName] = tally;
        return _vanillaCount;
    }

    internal static int TallyPullable(List<IContainer> containers, string sharedName, string itemPrefabName, out Tally tally)
    {
        tally = default;
        if (!MemoUsable(containers))
            return 0;

        PullKey key = new(sharedName, itemPrefabName);
        if (_talliesPullable.TryGetValue(key, out tally))
            return _vanillaCount;

        string? lastPrefab = null;
        bool canPull = false;
        for (int i = 0; i < _vanillaCount; i++)
        {
            IContainer container = containers[i];
            if (container == null) continue;
            try
            {
                string prefabName = container.GetPrefabName();
                if (prefabName != lastPrefab)
                {
                    canPull = CanItemBePulled(prefabName, itemPrefabName);
                    lastPrefab = prefabName;
                }

                if (!canPull) continue;
                int count = container.ItemCount(sharedName);
                if (count <= 0) continue;
                tally.Having++;
                tally.Sum += count;
            }
            catch
            {
                // ignored
            }
        }

        _talliesPullable[key] = tally;
        return _vanillaCount;
    }

    internal static int Takeable(Tally tally)
    {
        if (!AzuCraftyBoxesPlugin.leaveOne.Value.isOn())
            return tally.Sum;
        return tally.Sum - tally.Having;
    }

    internal static List<IContainer> GetNearbyContainers<T>(T src, float rangeMeters) where T : Component
    {
        if (!Player.m_localPlayer || !src) return EmptyIContainers();

        FlushRegistryIfNeeded();

        Vector3 pos = src.transform.position;

        // Reuse cache if player hasn't moved much & within time window
        if ((Time.time - _lastQueryTime) <= _cacheWindow &&
            (pos - _lastQueryPos).sqrMagnitude < 0.25f * 0.25f &&
            Mathf.Approximately(rangeMeters, _lastQueryRange))
        {
            return _cachedAll; // already a combined (nearby + drawers + bags) list
        }

        _scratchNearby.Clear();
        _scratchkgDrawers.Clear();
        _scratchmkzDrawers.Clear();
        _scratchBackpacks.Clear();
        _scratchQuivers.Clear();
        _scratchGemBags.Clear();
        _cachedAll.Clear();

        // 1) Nearby vanilla containers (no LINQ, squared distance, in-use check)
        bool mineOnly = CraftyToggle.MineOnly;
        long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
        float r2 = rangeMeters * rangeMeters;
        foreach (var c in Containers)
        {
            if (!c || !c.IsCraftyContainer() || !CraftyToggle.PullModeAllows(c.m_nview, mineOnly, playerId)) continue;
            if (c.m_wagon != null && c.m_wagon.InUse()) continue;
            if (OpenedBySomeoneElse(c)) continue;

            Vector3 d = c.transform.position - pos;
            if (d.sqrMagnitude > r2) continue;

            if (!c.IsInUse() || c.IsInUse() && c.IsOwner())
            {
                _scratchNearby.Add(VanillaContainer.Create(c));
                Watch(c.GetInventory());
            }
        }


        var drawers = APIs.ItemDrawers_API.AllDrawersInRange(pos, rangeMeters);
        foreach (var d in drawers)
        {
            if (d.m_nview.IsCraftyView() && CraftyToggle.PullModeAllows(d.m_nview, mineOnly, playerId))
                _scratchkgDrawers.Add(kgDrawer.Create(d));
        }
        var mkzdrawers = APIs.MkzItemDrawers_API.AllDrawersInRange(pos, rangeMeters);
        foreach (var d in mkzdrawers)
        {
            if (d.m_nview.IsCraftyView() && CraftyToggle.PullModeAllows(d.m_nview, mineOnly, playerId))
                _scratchmkzDrawers.Add(mkzDrawer.Create(d));
        }
        
        if (AzuCraftyBoxesPlugin.BackpacksIsLoaded)
        {
            var items = Player.m_localPlayer.GetInventory().GetAllItems();
            // de-dupe by reference
            var seen = HashSetPool<ItemContainer>.Get();
            for (int i = 0; i < items.Count; ++i)
            {
                var it = items[i];
                if (it == null) continue;

                var data = it.Data("org.bepinex.plugins.backpacks");
                if (data == null) continue;

                var cont = data.Get<ItemContainer>();
                if (cont == null) continue;

                if (seen.Add(cont))
                    _scratchBackpacks.Add(BackpackContainer.Create(cont));
            }

            HashSetPool<ItemContainer>.Release(seen);
        }

        if (BowsBeforeHoesCompat.IsLoaded)
        {
            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
            {
                if (BowsBeforeHoesCompat.GetInventory(item) is { } inventory)
                    _scratchQuivers.Add(new BowsBeforeHoesQuiver(item, inventory));
            }
        }
        
        if (Jewelcrafting.API.IsLoaded())
        {
            var items = Player.m_localPlayer.GetInventory().GetAllItems();
            for (int i = 0; i < items.Count; ++i)
            {
                var it = items[i];
                if (it == null) continue;

                if (Jewelcrafting.API.IsFreelyAccessibleInventory(it))
                {
                    // fast path: we assume GetItemContainerInventory(it) != null when accessible
                    if (Jewelcrafting.API.GetItemContainerInventory(it) != null)
                        _scratchGemBags.Add(new GemBagContainer(it));
                }
            }
        }


        _cachedAll.AddRange(_scratchNearby); // Makail ItemDrawers should still be captured by this.
        _cachedAll.AddRange(_scratchkgDrawers);
        _cachedAll.AddRange(_scratchBackpacks);
        _cachedAll.AddRange(_scratchQuivers);
        _cachedAll.AddRange(_scratchGemBags);
        _vanillaCount = _scratchNearby.Count;
        _cachedAllCount = _cachedAll.Count;
        InvalidateCounts();

        _lastQueryPos = pos;
        _lastQueryRange = rangeMeters;
        _lastQueryTime = Time.time;
        
        AzuCraftyBoxesPlugin.lastPosition = pos;
        AzuCraftyBoxesPlugin.cachedContainerList = _scratchNearby;

        return _cachedAll;
    }

    private static bool OpenedBySomeoneElse(Container c)
    {
        ZDO? zdo = c.m_nview ? c.m_nview.GetZDO() : null;
        return zdo != null && !zdo.IsOwner() && zdo.GetInt(ZDOVars.s_inUse, 0) == 1;
    }

    private static readonly List<IContainer> _empty = new(0);
    private static List<IContainer> EmptyIContainers() => _empty;

    internal static class QueryFrame
    {
        public static int FrameId;
        public static List<IContainer> Nearby;

        public static List<IContainer> Get<T>(T src, float range) where T : Component
        {
            int f = Time.frameCount;
            if (FrameId != f)
            {
                FrameId = f;
                Nearby = Boxes.GetNearbyContainers(src, range);
            }

            return Nearby;
        }
    }


    public static void AddContainerIfNotExists(string containerName)
    {
        if (!AzuCraftyBoxesPlugin.yamlData.ContainsKey(containerName))
        {
            AzuCraftyBoxesPlugin.yamlData[containerName] = new Dictionary<string, List<string>>
            {
                { "exclude", new List<string>() },
                { "includeOverride", new List<string>() },
            };

            YamlUtils.WriteYaml(AzuCraftyBoxesPlugin.yamlPath);
        }
    }

    // Get a list of all excluded prefabs for all containers in the container data

    public static Dictionary<string, List<string>> GetExcludedPrefabsForAllContainers()
    {
        Dictionary<string, List<string>> excludedPrefabsForAllContainers = new Dictionary<string, List<string>>();

        foreach (string? container in GetAllContainers())
        {
            excludedPrefabsForAllContainers[container] = GetExcludedPrefabs(container);
        }

        return excludedPrefabsForAllContainers;
    }

    // Get a list of all containers
    public static List<string> GetAllContainers()
    {
        return AzuCraftyBoxesPlugin.yamlData.Keys.Where(key => key != "groups").ToList();
    }

    private static bool PassesIncludeExcludeChecks(Dictionary<string, List<string>> data, string prefab)
    {
        if (data.TryGetValue("includeOverride", out List<string> includeValues) && includeValues.Contains(prefab))
            return true;

        if (!data.TryGetValue("exclude", out List<string> excludeList))
            return true;

        foreach (string? excludedItem in excludeList)
        {
            if (prefab.Equals(excludedItem))
                return false;

            if (AzuCraftyBoxesPlugin.groups.TryGetValue(excludedItem, out HashSet<string> groupPrefabs) && groupPrefabs.Contains(prefab))
                return false;
        }

        // 3. If we got here, no exclude matched and no includeOverride was needed
        // By default, allow pulling
        return true;
    }

    public static bool CanItemBePulled(string container, string prefab, string stationName = "")
    {
        if (AzuCraftyBoxesPlugin.yamlData == null)
        {
            AzuCraftyBoxesPlugin.AzuCraftyBoxesLogger.LogError("yamlData is null. Make sure to call DeserializeYamlFile() before using CanItemBePulled.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(CachedStationName))
        {
            stationName = CachedStationName;
        }

        // -----------------------------------------------------------------------
        // 1) Check stationName first (if not empty)
        // -----------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(stationName) && AzuCraftyBoxesPlugin.yamlData.TryGetValue(stationName, out Dictionary<string, List<string>>? stationData))
        {
            // Apply station include/exclude logic
            bool stationPass = PassesIncludeExcludeChecks(stationData, prefab);

            if (!stationPass)
            {
                // If the station explicitly excludes this item,
                // we can return false immediately, no need to check container
                return false;
            }
            // If station passed (i.e. not excluded), we still continue
            // to container checks.
            // (If you prefer station "includeOverride" to skip container checks,
            // you can detect that here and return true. But that changes logic.)
        }

        // -----------------------------------------------------------------------
        // 2) Now apply container filters
        // -----------------------------------------------------------------------
        // If container is NOT in yaml, we allow by default
        if (!AzuCraftyBoxesPlugin.yamlData.TryGetValue(container, out Dictionary<string, List<string>>? containerData))
        {
            // Container not found => allow pulling
            return true;
        }

        // Check container include/exclude logic
        bool containerPass = PassesIncludeExcludeChecks(containerData, prefab);
        return containerPass;
    }


    // Check if a prefab is excluded from a container

    /*public static bool CanItemBePulled(string container, string prefab, string stationName = "")
    {
        if (AzuCraftyBoxesPlugin.yamlData == null)
        {
            AzuCraftyBoxesPlugin.AzuCraftyBoxesLogger.LogError("yamlData is null. Make sure to call DeserializeYamlFile() before using CanItemBePulled.");
            return false;
        }

        if (!AzuCraftyBoxesPlugin.yamlData.TryGetValue(container, out Dictionary<string, List<string>> containerData))
        {
            //AzuCraftyBoxesPlugin.AzuCraftyBoxesLogger.LogInfo($"Container '{container}' not found in yamlData.");
            return true; // Allow pulling by default if the container is not defined in yamlData
        }

        List<string> excludeList = containerData.TryGetValue("exclude", out List<string> value1) ? value1 : new List<string>();
        List<string> includeOverrideList = containerData.TryGetValue("includeOverride", out List<string> value) ? value : new List<string>();

        if (includeOverrideList.Contains(prefab))
        {
            return true;
        }

        foreach (object? excludedItem in excludeList)
        {
            if (prefab.Equals(excludedItem))
            {
                return false;
            }

            if (GroupUtils.IsGroupDefined((string)excludedItem))
            {
                List<string> groupItems = GroupUtils.GetItemsInGroup((string)excludedItem);
                if (groupItems.Contains(prefab))
                {
                    return false;
                }
            }
        }

        return true;
    }*/


    internal static bool IsPrefabExcluded(string prefab, List<object> exclusionList)
    {
        if (exclusionList != null)
        {
            foreach (object? excludeItem in exclusionList)
            {
                string excludeItemName = excludeItem.ToString();

                if (AzuCraftyBoxesPlugin.groups.TryGetValue(excludeItemName, out HashSet<string> groupPrefabs))
                {
                    if (groupPrefabs.Contains(prefab))
                    {
                        return true;
                    }
                }
                else if (excludeItemName == prefab)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static List<string> GetExcludedPrefabs(string container)
    {
        if (AzuCraftyBoxesPlugin.yamlData.TryGetValue(container, out Dictionary<string, List<string>> containerData))
        {
            if (containerData.TryGetValue("exclude", out List<string> excludeList))
            {
                List<string> excludedPrefabs = new List<string>();
                foreach (string excludeItem in excludeList)
                {
                    if (AzuCraftyBoxesPlugin.groups.TryGetValue(excludeItem, out HashSet<string> groupPrefabs))
                    {
                        excludedPrefabs.AddRange(groupPrefabs);
                    }
                    else
                    {
                        excludedPrefabs.Add(excludeItem);
                    }
                }

                return excludedPrefabs;
            }
        }

        return new List<string>();
    }

    public static Stopwatch GetStopwatch(GameObject o)
    {
        float hash = GetGameObjectPosHash(o);
        Stopwatch stopwatch = null;

        if (!stopwatches.TryGetValue(hash, out stopwatch))
        {
            stopwatch = new Stopwatch();
            stopwatches.TryAdd(hash, stopwatch);
        }

        return stopwatch;
    }

    private static float GetGameObjectPosHash(GameObject o)
    {
        return (1000f * o.transform.position.x) + o.transform.position.y + (.001f * o.transform.position.z);
    }


    internal static int CheckAndDecrement(int amount)
    {
        if (amount <= 0) return amount;
        if (AzuCraftyBoxesPlugin.leaveOne.Value.isOn())
        {
            return amount - 1;
        }

        return amount;
    }

    public class LaterConsumption(string name, int amount, int quality, IContainer sourceContainer, ItemDrop.ItemData requiredItem)
    {
        public string Name { get; set; } = name;
        public int Amount { get; set; } = amount;
        public int Quality { get; set; } = quality;
        public IContainer SourceContainer { get; set; } = sourceContainer;
        public ItemDrop.ItemData RequiredItem { get; set; } = requiredItem;
    }
}

public static class ConsumptionManager
{
    public static ConcurrentBag<Boxes.LaterConsumption> PendingConsumptions { get; } = [];
}
