using System.IO;
using System.Text.RegularExpressions;
using AzuCraftyBoxes.Compatibility;
using AzuCraftyBoxes.IContainers;
using AzuCraftyBoxes.Patches;
using AzuCraftyBoxes.Util;
using AzuCraftyBoxes.Util.Functions;

namespace AzuCraftyBoxes
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency("kg.ItemDrawers", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.bepinex.plugins.backpacks", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.bepinex.plugins.jewelcrafting", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("aedenthorn.CraftFromContainers")]
    [BepInIncompatibility("CFCMod")]
    public class AzuCraftyBoxesPlugin : BaseUnityPlugin
    {
        internal const string ModName = "AzuCraftyBoxes";
        internal const string ModVersion = "1.8.27";
        internal const string Author = "Azumatt";
        private const string ModGUID = $"{Author}.{ModName}";
        private static string ConfigFileName = $"{ModGUID}.cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        internal static readonly Harmony harmony = new(ModGUID);
        public static readonly ManualLogSource AzuCraftyBoxesLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        private FileSystemWatcher _watcher = null!;
        private FileSystemWatcher _yamlwatcher = null!;
        private readonly object _reloadLock = new();
        private readonly object _yamlreloadLock = new();
        private DateTime _lastConfigReloadTime;
        private DateTime _yamllastConfigReloadTime;
        private const long RELOAD_DELAY = 10000000; // One second

        internal static bool skip;
        public static Vector3 lastPosition = Vector3.positiveInfinity;
        public static List<IContainer> cachedContainerList = new List<IContainer>();

        internal static readonly string yamlFileName = $"{Author}.{ModName}.yml";
        internal static readonly string yamlPath = Paths.ConfigPath + Path.DirectorySeparatorChar + yamlFileName;
        internal static readonly CustomSyncedValue<string> CraftyContainerData = new(ConfigSync, "craftyboxesData", "");
        internal static readonly CustomSyncedValue<string> CraftyContainerGroupsData = new(ConfigSync, "craftyboxesGroupsData", "");
        internal const string PreventPullingLogicKey = "ACB_PreventPulling";

        //
        internal static Dictionary<string, Dictionary<string, List<string>>> yamlData = null!;
        internal static Dictionary<string, HashSet<string>> groups = new();
        internal static Dictionary<string, bool> CanItemBePulledCache = null!;
        internal static bool BackpacksIsLoaded = false;

        public enum Toggle
        {
            On = 1,
            Off = 0,
        }

        public enum GamepadButton
        {
            None,
            AlternateFunction,
            LeftTrigger,
            RightTrigger,
            LeftBumper,
            RightBumper,
            DPadLeft,
            DPadRight,
            DPadUp,
            DPadDown,
            LeftStick,
            RightStick,
            ButtonA,
            ButtonB,
            ButtonX,
            ButtonY,
        }

        public void Awake()
        {
            bool saveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;

            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

            ModEnabled = config("1 - General", "Mod Enabled", Toggle.On, "If off, everything in the mod will not run. This is useful if you want to disable the mod without uninstalling it.");
            debugLogsEnabled = config("1 - General", "Output Debug Logs", Toggle.Off, "If on, the debug logs will be displayed in the BepInEx console window when BepInEx debugging is enabled.");
            preventPullingLogicMessage = config("1 - General", "Prevent Pulling Message", Toggle.On, "If on, a message will be displayed above the player's head when the prevention pulling logic is toggled using the keybind.", false);
            preventPullingStringFormat = config("1 - General", "Prevent Pulling Format", "<size=30><color=#ffffff>{0}</color></size>\n<size=25>{1}</size>", "String format for the message displayed when the prevention pulling logic is toggled. {0} is replaced by the message, and {1} is replaced by the on/off status. Set to nothing to leave it as default.", false);
            preventPullingStatusEffectDisplay = config("1 - General", "Prevent Pulling Status", Toggle.On, "If on, a status effect is displayed while you can't pull from containers, or can only pull from your own.", false);
            preventPullingStatusEffectDisplay.SettingChanged += (sender, args) =>
            {
                if (Player.m_localPlayer != null)
                    SE_ContainerPull.CheckAndSetStatusEffect(Player.m_localPlayer);
            };
            mRange = config("2 - CraftyBoxes", "Container Range", 20f, "The maximum range from which to pull items from.");
            leaveOne = config("2 - CraftyBoxes", "Leave One Item", Toggle.Off, new ConfigDescription("* If on, leaves one item in the chest when pulling from it, so that you are able to pull from it again and store items more easily with other mods. (Such as AzuAutoStore or QuickStackStore). If off, it will pull all items from the chest.", null, new ConfigurationManagerAttributes() { Order = 2 }));
            resourceString = TextEntryConfig("2 - CraftyBoxes", "ResourceCostString", "{0}/{1}", new ConfigDescription("String used to show required and available resources. {0} is replaced by how much is available, and {1} is replaced by how much is required. Set to nothing to leave it as default.", null, new ConfigurationManagerAttributes() { Order = 1 }), false);
            flashColor = config("2 - CraftyBoxes", "FlashColor", Color.yellow, "Resource amounts will flash to this colour when coming from containers", false);
            unFlashColor = config("2 - CraftyBoxes", "UnFlashColor", Color.white, "Resource amounts will flash from this colour when coming from containers (set both colors to the same color for no flashing)", false);
            canbuildDisplayColor = config("2 - CraftyBoxes", "Can Build Color", Color.green, "The color of the build panel's count of pieces you can build", false);
            cannotbuildDisplayColor = config("2 - CraftyBoxes", "Cannot Build Color", Color.red, "The color of the build panel's count if you cannot build something", false);
            chestToggleEnabled = config("2 - CraftyBoxes", "Container Toggle", Toggle.On, "If on, players can mark individual containers as excluded from crafting. If off, the toggle and its hover text are gone and every container is pulled from again, whatever it was marked as.");
            chestToggleHoverText = config("2 - CraftyBoxes", "Container Toggle Hover Text", Toggle.On, "If on, the toggle key and the container's crafting status are shown in its hover text.", false);
            pullItemsKey = config("3 - Keys", "PullItemsKey", new KeyboardShortcut(KeyCode.P, KeyCode.LeftShift), new ConfigDescription("While in build mode, pulls the selected piece's materials from nearby containers into your inventory so you can carry them (portal, cart, etc.) without building and breaking the piece. Use https://docs.unity3d.com/Manual/ConventionalGameInput.html", new AcceptableShortcuts()), false);
            pullItemsGamepadKey = config("3 - Keys", "PullItemsGamepadKey", GamepadButton.DPadRight, "Controller button, pressed while holding the game's alternate function, to pull the selected piece's materials into your inventory while in build mode. Minimap zoom in and out is suppressed while in build mode. Set to None to disable.", false);
            fillAllModKey = config("3 - Keys", "FillAllModKey", new KeyboardShortcut(KeyCode.LeftShift), new ConfigDescription("Modifier key to pull all available fuel or ore when down. Use https://docs.unity3d.com/Manual/ConventionalGameInput.html", new AcceptableShortcuts()), false);
            fillAllModGamepadKey = config("3 - Keys", "FillAllModGamepadKey", GamepadButton.AlternateFunction, "Controller modifier to pull all available fuel or ore when held. AlternateFunction uses the game's alternate-interact button (same as chest toggle; follows controller layout). Set to None to disable.", false);
            preventPullingLogic = config("3 - Keys", "Prevent Pulling Logic", new KeyboardShortcut(KeyCode.O, KeyCode.LeftAlt), new ConfigDescription("Key to cycle what you pull from: all containers, only containers you built yourself, or nothing. Nothing prevents all pulling logic from running, essentially making the mod appear as if it's not installed. This is different from the Mod Enabled option because it allows toggling on the fly (specifically for you as the player)  Use https://docs.unity3d.com/Manual/ConventionalGameInput.html", new AcceptableShortcuts()), false);
            chestToggleKey = config("3 - Keys", "Container Toggle Key", new KeyboardShortcut(KeyCode.E, KeyCode.LeftShift), new ConfigDescription("Key to toggle whether the container you are looking at can be crafted from. Gamepads still use the alt keys + use. Use https://docs.unity3d.com/Manual/ConventionalGameInput.html", new AcceptableShortcuts()), false);

            if (!File.Exists(yamlPath))
            {
                WriteConfigFileFromResource(yamlPath);
            }

            CraftyContainerData.ValueChanged += OnValChangedUpdate; // check for file changes
            CraftyContainerData.AssignLocalValue(File.ReadAllText(yamlPath));

            Assembly assembly = Assembly.GetExecutingAssembly();
            harmony.PatchAll(assembly);
            SetupWatcher();

            Config.Save();
            if (saveOnSet)
            {
                Config.SaveOnConfigSet = saveOnSet;
            }

            SE_ContainerPull.CreateEffect();
        }

        private static void WriteConfigFileFromResource(string configFilePath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = "AzuCraftyBoxes.Example.yml";

            using Stream resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                throw new FileNotFoundException($"Resource '{resourceName}' not found in the assembly.");
            }

            using StreamReader reader = new StreamReader(resourceStream);
            string contents = reader.ReadToEnd();

            File.WriteAllText(configFilePath, contents);
        }

        private void Start()
        {
            AutoDoc();

            if (Chainloader.PluginInfos.ContainsKey("org.bepinex.plugins.backpacks"))
            {
                BackpacksIsLoaded = true;
            }
            if (Chainloader.PluginInfos.ContainsKey("org.bepinex.plugins.backpacks"))
            {
                BackpacksIsLoaded = true;
            }

            EpicLootCompat.Init(ModGUID);
        }

        private void Update()
        {
            Player? player = Player.m_localPlayer;
            if (player == null) return;

            CraftyToggle.CheckToggleKey(player);
            PullBuildItems.CheckKey(player);

            if (preventPullingLogic.Value.IsKeyDown() && player.TakeInput())
            {
                string onOff = player.CyclePullMode() switch
                {
                    PullMode.All => "<color=green>Yes</color>",
                    PullMode.MineOnly => "<color=yellow>Only mine</color>",
                    _ => "<color=red>No</color>",
                };
                string message = "Pull from containers?";
                AzuCraftyBoxesLogger.LogIfReleaseAndDebugEnable(message);
                if (preventPullingLogicMessage.Value.isOn())
                {
                    Chat.instance.AddInworldText(
                        player.gameObject,
                        player.GetPlayerID(),
                        player.GetHeadPoint(),
                        Talker.Type.Normal,
                        UserInfo.GetLocalUser(),
                        Localization.instance.Localize(string.Format(preventPullingStringFormat.Value, message, onOff))
                    );
                }
            }
        }

        private void LateUpdate()
        {
            skip = false;
        }

        private void AutoDoc()
        {
#if DEBUG
            // Store Regex to get all characters after a [
            Regex regex = new(@"\[(.*?)\]");

            // Strip using the regex above from Config[x].Description.Description
            string Strip(string x) => regex.Match(x).Groups[1].Value;
            StringBuilder sb = new();
            string lastSection = "";
            foreach (ConfigDefinition x in Config.Keys)
            {
                // skip first line
                if (x.Section != lastSection)
                {
                    lastSection = x.Section;
                    sb.Append($"{Environment.NewLine}`{x.Section}`{Environment.NewLine}");
                }

                sb.Append($"\n{x.Key} [{Strip(Config[x].Description.Description)}]" +
                          $"{Environment.NewLine}   * {Config[x].Description.Description.Replace("[Synced with Server]", "").Replace("[Not Synced with Server]", "")}" +
                          $"{Environment.NewLine}     * Default Value: {Config[x].GetSerializedValue()}{Environment.NewLine}");
            }

            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, $"{ModName}_AutoDoc.md"), sb.ToString());
#endif
        }

        private void OnDestroy()
        {
            SaveWithRespectToConfigSet();
            _watcher?.Dispose();
            _yamlwatcher?.Dispose();
        }

        private void SetupWatcher()
        {
            _watcher = new(Paths.ConfigPath, ConfigFileName);
            _watcher.Changed += ReadConfigValues;
            _watcher.Created += ReadConfigValues;
            _watcher.Renamed += ReadConfigValues;
            _watcher.IncludeSubdirectories = true;
            _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            _watcher.EnableRaisingEvents = true;

            _yamlwatcher = new(Paths.ConfigPath, yamlFileName);
            _yamlwatcher.Changed += ReadYamlFiles;
            _yamlwatcher.Created += ReadYamlFiles;
            _yamlwatcher.Renamed += ReadYamlFiles;
            _yamlwatcher.IncludeSubdirectories = true;
            _yamlwatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            _yamlwatcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            DateTime now = DateTime.Now;
            long time = now.Ticks - _lastConfigReloadTime.Ticks;
            if (time < RELOAD_DELAY)
            {
                return;
            }

            lock (_reloadLock)
            {
                if (!File.Exists(ConfigFileFullPath))
                {
                    AzuCraftyBoxesLogger.LogWarning("Config file does not exist. Skipping reload.");
                    return;
                }

                try
                {
                    AzuCraftyBoxesLogger.LogDebug("Reloading configuration...");
                    SaveWithRespectToConfigSet(true);
                    AzuCraftyBoxesLogger.LogInfo("Configuration reload complete.");
                }
                catch (Exception ex)
                {
                    AzuCraftyBoxesLogger.LogError($"Error reloading configuration: {ex.Message}");
                }
            }

            _lastConfigReloadTime = now;
        }

        private void ReadYamlFiles(object sender, FileSystemEventArgs e)
        {
            DateTime now = DateTime.Now;
            long time = now.Ticks - _yamllastConfigReloadTime.Ticks;
            if (time < RELOAD_DELAY)
            {
                return;
            }

            lock (_yamlreloadLock)
            {
                if (!File.Exists(yamlPath))
                {
                    AzuCraftyBoxesLogger.LogWarning("Yaml config file does not exist!");
                    return;
                }

                try
                {
                    AzuCraftyBoxesLogger.LogDebug("ReadConfigValues called");
                    CraftyContainerData.AssignLocalValue(File.ReadAllText(yamlPath));
                }
                catch (Exception ex)
                {
                    AzuCraftyBoxesLogger.LogError($"There was an issue loading your {yamlFileName}");
                    AzuCraftyBoxesLogger.LogError($"Please check your entries for spelling and format!\n {ex}");
                }
            }

            _yamllastConfigReloadTime = now;
        }

        private static void OnValChangedUpdate()
        {
            AzuCraftyBoxesLogger.LogDebug("OnValChanged called");
            try
            {
                YamlUtils.ReadYaml(CraftyContainerData.Value);
                YamlUtils.ParseGroups();
            }
            catch (Exception e)
            {
                AzuCraftyBoxesLogger.LogError($"Failed to deserialize {yamlFileName}: {e}");
            }
        }

        private void SaveWithRespectToConfigSet(bool reload = false)
        {
            bool originalSaveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            if (reload)
                Config.Reload();
            Config.Save();
            if (originalSaveOnSet)
            {
                Config.SaveOnConfigSet = originalSaveOnSet;
            }
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        internal static ConfigEntry<Toggle> ModEnabled = null!;
        internal static ConfigEntry<Toggle> debugLogsEnabled = null!;
        internal static ConfigEntry<Toggle> leaveOne = null!;
        public static ConfigEntry<Color> flashColor = null!;
        public static ConfigEntry<Color> unFlashColor = null!;
        public static ConfigEntry<Color> canbuildDisplayColor = null!;
        public static ConfigEntry<Color> cannotbuildDisplayColor = null!;
        public static ConfigEntry<string> resourceString = null!;
        public static ConfigEntry<string> pulledMessage = null!;
        public static ConfigEntry<KeyboardShortcut> pullItemsKey = null!;
        public static ConfigEntry<GamepadButton> pullItemsGamepadKey = null!;
        public static ConfigEntry<KeyboardShortcut> fillAllModKey = null!;
        public static ConfigEntry<GamepadButton> fillAllModGamepadKey = null!;
        public static ConfigEntry<KeyboardShortcut> preventPullingLogic = null!;
        public static ConfigEntry<Toggle> preventPullingLogicMessage = null!;
        public static ConfigEntry<string> preventPullingStringFormat = null!;
        public static ConfigEntry<Toggle> preventPullingStatusEffectDisplay = null!;
        public static ConfigEntry<float> mRange = null!;
        public static ConfigEntry<Toggle> chestToggleEnabled = null!;
        public static ConfigEntry<Toggle> chestToggleHoverText = null!;
        public static ConfigEntry<KeyboardShortcut> chestToggleKey = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        internal ConfigEntry<T> TextEntryConfig<T>(string group, string name, T value, string desc, bool synchronizedSetting = true)
        {
            ConfigurationManagerAttributes attributes = new()
            {
                CustomDrawer = TextAreaDrawer,
            };
            return config(group, name, value, new ConfigDescription(desc, null, attributes), synchronizedSetting);
        }

        internal ConfigEntry<T> TextEntryConfig<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigurationManagerAttributes attributes = new()
            {
                CustomDrawer = TextAreaDrawer,
            };
            return config(group, name, value, new ConfigDescription(description.Description, null, attributes), synchronizedSetting);
        }

        internal static void TextAreaDrawer(ConfigEntryBase entry)
        {
            GUILayout.ExpandHeight(true);
            GUILayout.ExpandWidth(true);
            entry.BoxedValue = GUILayout.TextArea((string)entry.BoxedValue, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order = null!;
            [UsedImplicitly] public bool? Browsable = null!;
            [UsedImplicitly] public string Category = null!;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
        }

        class AcceptableShortcuts : AcceptableValueBase // Used for KeyboardShortcut Configs
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        #endregion
    }

    public static class KeyboardExtensions
    {
        public static bool IsKeyDown(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        public static bool IsKeyHeld(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }
    }

    public static class GamepadExtensions
    {
        public static bool IsBound(this AzuCraftyBoxesPlugin.GamepadButton button)
        {
            return button != AzuCraftyBoxesPlugin.GamepadButton.None;
        }

        public static bool IsButtonHeld(this AzuCraftyBoxesPlugin.GamepadButton button)
        {
            if (button == AzuCraftyBoxesPlugin.GamepadButton.AlternateFunction && !ZInput.IsGamepadActive()) return false;

            string buttonName = button.ZInputName();
            return buttonName.Length > 0 && ZInput.instance != null && ZInput.GetButton(buttonName);
        }

        public static bool IsButtonDown(this AzuCraftyBoxesPlugin.GamepadButton button)
        {
            if (!ZInput.IsGamepadActive()) return false;

            string buttonName = button.ZInputName();
            return buttonName.Length > 0 && ZInput.instance != null && ZInput.GetButtonDown(buttonName);
        }

        public static string DisplayName(this AzuCraftyBoxesPlugin.GamepadButton button)
        {
            string buttonName = button.ZInputName();
            if (buttonName.Length > 0 && ZInput.instance != null)
            {
                try
                {
                    // Returns the glyph the game shows for the bound control
                    string boundKey = ZInput.instance.GetBoundKeyString(buttonName, true);
                    if (!string.IsNullOrEmpty(boundKey))
                    {
                        return boundKey;
                    }
                }
                catch (KeyNotFoundException)
                {
                    // No glyph is registered for the bound control, fall back to the config value's name
                }
            }

            return button.ToString();
        }

        private static string ZInputName(this AzuCraftyBoxesPlugin.GamepadButton button)
        {
            switch (button)
            {
                case AzuCraftyBoxesPlugin.GamepadButton.AlternateFunction:
                    return ZInput.IsNonClassicFunctionality() ? "JoyAltKeys" : "JoyAltPlace";
                case AzuCraftyBoxesPlugin.GamepadButton.LeftTrigger:
                    return "JoyLTrigger";
                case AzuCraftyBoxesPlugin.GamepadButton.RightTrigger:
                    return "JoyRTrigger";
                case AzuCraftyBoxesPlugin.GamepadButton.LeftBumper:
                    return "JoyLBumper";
                case AzuCraftyBoxesPlugin.GamepadButton.RightBumper:
                    return "JoyRBumper";
                case AzuCraftyBoxesPlugin.GamepadButton.DPadLeft:
                    return "JoyDPadLeft";
                case AzuCraftyBoxesPlugin.GamepadButton.DPadRight:
                    return "JoyDPadRight";
                case AzuCraftyBoxesPlugin.GamepadButton.DPadUp:
                    return "JoyDPadUp";
                case AzuCraftyBoxesPlugin.GamepadButton.DPadDown:
                    return "JoyDPadDown";
                case AzuCraftyBoxesPlugin.GamepadButton.LeftStick:
                    return "JoyLStick";
                case AzuCraftyBoxesPlugin.GamepadButton.RightStick:
                    return "JoyRStick";
                case AzuCraftyBoxesPlugin.GamepadButton.ButtonA:
                    return "JoyButtonA";
                case AzuCraftyBoxesPlugin.GamepadButton.ButtonB:
                    return "JoyButtonB";
                case AzuCraftyBoxesPlugin.GamepadButton.ButtonX:
                    return "JoyButtonX";
                case AzuCraftyBoxesPlugin.GamepadButton.ButtonY:
                    return "JoyButtonY";
                default:
                    return string.Empty;
            }
        }
    }

    public static class LoggerExtensions
    {
        public static void LogIfReleaseAndDebugEnable(this ManualLogSource logger, string message)
        {
#if Release
            if (AzuCraftyBoxesPlugin.debugLogsEnabled.Value.isOn())
            {
                logger.LogDebug(message);
            }
#endif
        }

        public static void LogIfDebugBuild(this ManualLogSource logger, string message)
        {
#if DEBUG
            logger.LogDebug(message);
#endif
        }

        public static void LogIfDebuggingEpicLoot(this ManualLogSource logger, string message)
        {
#if EpicLootTesting
            logger.LogDebug(message);
#endif
        }
    }

    public static class ToggleExtensions
    {
        public static bool isOn(this AzuCraftyBoxesPlugin.Toggle toggle)
        {
            return toggle == AzuCraftyBoxesPlugin.Toggle.On;
        }

        public static bool isOff(this AzuCraftyBoxesPlugin.Toggle toggle)
        {
            return toggle == AzuCraftyBoxesPlugin.Toggle.Off;
        }
    }
}
