using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MonoMod.RuntimeDetour;
using RoR2;
using RiskOfOptions;
using RiskOfOptions.Options;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Networking;

namespace ConfigurableTrialPlugin
{

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]

    public class ConfigurableTrials : BaseUnityPlugin
    {
        public const string PluginGUID = "ShandoIsSad.ConfigurableTrials";
        public const string PluginAuthor = "ShandoVK";
        public const string PluginName = "ConfigurableTrials";
        public const string PluginVersion = "0.7.0";

        private static readonly PropertyInfo timeTilDoomsday = typeof(DoomTimer).GetProperty("timeUntilFailure", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo timePause = typeof(DoomTimer).GetField("pauseTime", BindingFlags.NonPublic | BindingFlags.Instance);
        private static float bossRushLeftoverTime;
        private static readonly FieldInfo trialSelectionField = typeof(DLC4TrialSelector).GetField("_trialSelection", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo canAddTrialMethod = typeof(DLC4TrialSelector).GetMethod("CanAddTrial", BindingFlags.NonPublic | BindingFlags.Instance);
        private static DLC4TrialSelector activeTrialSelector;
        private static string currentTrial;
        private static bool trialRentalGrant;

        public static ConfigEntry<float> startingTimer;
        public static ConfigEntry<float> timeAddedStage;
        public static ConfigEntry<float> timeAddedLoop;
        public static ConfigEntry<bool> timeAddedBossRush;
        public static ConfigEntry<bool> enableCustomStartItems;

        // dlc4 trial scenes and their variants (each scene has 3 variants), add to here if gearbox ever makes that claptrap trial or some other stupid thing
        private static readonly Dictionary<string, ConfigEntry<bool>> trialVariantToggle = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<string, (string section, (string id, string name)[] variants)> SceneVariants = new Dictionary<string, (string, (string, string)[])>
        {
            { "sad1", ("Winnowing of Predation", new[]
                {
                    ("MakingMoneyAndDollars", "Trial of Infernal Greed"),
                    ("BreakTheTargets", "Trial of Broken Glass"),
                    ("OpenTheChests", "Trial of the Scavenger's Dream")
                })
            },

            { "soul1", ("Winnowing of Mortality", new[]
                {
                    ("SpeedRunVariant_A", "Trial of the Agile"),
                    ("SpeedRunVariant_B", "Trial of the Fleet-Footed"),
                    ("SpeedRunVariant_C", "Trial of the Conduit")
                })
            },

            { "surv2", ("Winnowing of Survival", new[]
                {
                    ("UmbraVariant", "Trial of the Lesser Echoes"),
                    ("ProcCityVariant", "Trial of Casualty"),
                    ("MonstersOffMapVariant", "Trial of the Abyss")
                })
            },
        };

        private static readonly HashSet<ItemTier> vanillaTiers = new HashSet<ItemTier>
        {
            ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss, ItemTier.Lunar, ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss, ItemTier.Cursed
        };

        private static readonly HashSet<string> customTiers = new HashSet<string>
        {
            "Core"
            //Sibylline & Highlander items don't actually store unlike other items, so they persist into trials. Not sure if this is intentional \(- n -)/
        };

        // reference for anyone looking at this ig, these are the vanilla defaults for trial starting items: Greed: GoldenGoose:3, Procs: Clover, LowHealthOrbSpawner, Abyss: SprintDamage:10
        private static readonly Dictionary<string, ConfigEntry<string>> trialStartItems = new Dictionary<string, ConfigEntry<string>>();
        private static readonly Dictionary<string, string> customStartItems = new Dictionary<string, string>
        {
            {"MakingMoneyAndDollars", "GoldenGoose:3, MysticsItems_Idol, MinersHelmet:3, ITEM_PERMASPIKE, ITEM_ECONOMYWEAPON, CLASSICITEMSRETURNS_ITEM_JEWEL:2, CLASSICITEMSRETURNS_ITEM_WEAKENONCONTACT:3, BloodTester:5, LITLegacyGOLDENGUN"},
            {"BreakTheTargets", "RechargeableJump, ITEM_HORSE_HOOF, _4_TON_JETPACK_FAITHFUL_4TON_JETPACK_ITEM, MintCondition"},
            {"OpenTheChests", "SwiftSkateboard, ITEM_TSUNAMI_IONIC_COLA, ITEM_HORSE_HOOF"},
            {"SpeedRunVariant_A", "RechargeableJump, _4_TON_JETPACK_FAITHFUL_4TON_JETPACK_ITEM, JetBoots"},
            {"SpeedRunVariant_B", "RechargeableJump, JumpDamageStrike, SwiftSkateboard, MysticsItems_Flow:3, ITEM_TSUNAMI_IONIC_COLA"},
            {"SpeedRunVariant_C", "RechargeableJump, WatchMetronome:2, ITEM_SANDSWEPT_TORN_FEATHER, MintCondition"},
            {"UmbraVariant", "DroidHead, ExplorersScarf:3, ITEM_BLUELEMURIAN:3, ITEM_TSUNAMI_SUPPRESSOR, ITEM_PLATINUM_FISTS, ITEM_SANDSWEPT_UNIVERSAL_VIP_PASS:2, MysticsItems_KeepShopTerminalOpen:2, ITEM_DISCOUNTCARDNEW:2"},
            {"ProcCityVariant", "Clover, LowHealthOrbSpawner, ITEM_TSUNAMI_SUPPRESSOR, CLASSICITEMSRETURNS_ITEM_WEAKENONCONTACT:3, ITEM_DOUBLE_BAND, ITEM_SANDSWEPT_UNIVERSAL_VIP_PASS:2, MysticsItems_KeepShopTerminalOpen:2, ITEM_DISCOUNTCARDNEW:2"},
            {"MonstersOffMapVariant", "SprintDamage:10, ITEM_SANDSWEPT_MAKESHIFT_PLATE, MysticsItems_BackArmor:5, ITEM_HORSE_HOOF, MATTER_ACCELERATOR_FAITHFUL_MATTER_ACCELERATOR_ITEM:3, GuardingAmulet:5, LITLegacyREPULSIONARMOR"},
        };

        public void Awake()
        {
            Log.Init(Logger);

            // config
            startingTimer = Config.Bind(
                "Timer",
                "Initial time limit for trials",
                210f,
                new ConfigDescription("Starting timer for N’kuhana Trials, in seconds. Vanilla is 210s (3:30)",
                new AcceptableValueRange<float>(0f, 2592000f)));

            timeAddedStage = Config.Bind(
                "Timer",
                "Time added per stage cleared",
                0f,
                new ConfigDescription("For each stage you clear, the timer increases by this value. Set it to 0 to disable",
                new AcceptableValueRange<float>(0f, 86400f)));

            timeAddedLoop = Config.Bind(
                "Timer",
                "Time added per loop finished",
                0f,
                new ConfigDescription("For each time you loop, the timer increases by this value. Set it to 0 to disable",
                new AcceptableValueRange<float>(0f, 86400f)));

            timeAddedBossRush = Config.Bind(
                "Timer",
                "Time added for clearing boss rush",
                false,
                new ConfigDescription("Upon finishing Soulbound Threshold, leftover time from the boss rush portion is refunded to the timer."));

            enableCustomStartItems = Config.Bind(
                "Trial Items",
                "Enable custom starting items per trial",
                false,
                new ConfigDescription("Allows for changing items that are temporarily given to the player for doing trial challenges. Strongly recommended to look at/edit the entry fields if you have a lot of item mods enabled." ));

            foreach (var scene in SceneVariants.Values)
            {
                foreach (var (trialId, trialName) in scene.variants)
                {
                    trialVariantToggle[trialId] = Config.Bind(
                        scene.section,
                        trialId,
                        true,
                        $"Allows the '{trialName}' to appear");

                    trialStartItems[trialId] = Config.Bind(
                        "Trial Items",
                        trialId,
                        customStartItems.TryGetValue(trialId, out var defaultItems) ? defaultItems : "",
                        $"Ethereal items for use in the '{trialName}'.\n\n" + "Formatted like so: ItemID:Count, ItemID:Count\n" + "Example: SprintDamage, Hoof:5\n");
                }
            }

            // options compat
            if (RiskOfOptionsCompat.Enabled)
            {
                RiskOfOptionsCompat.AddFloatField(startingTimer);
                RiskOfOptionsCompat.AddFloatField(timeAddedStage);
                RiskOfOptionsCompat.AddFloatField(timeAddedLoop);
                RiskOfOptionsCompat.AddCheckbox(timeAddedBossRush, true);
                RiskOfOptionsCompat.AddCheckbox(enableCustomStartItems, true);

                foreach (var toggle in trialVariantToggle.Values)
                    RiskOfOptionsCompat.AddCheckbox(toggle, true);
                foreach (var entry in trialStartItems.Values)
                    RiskOfOptionsCompat.AddStringField(entry);
            }

            On.RoR2.Run.AdvanceStage += Run_AdvanceStage;
            On.RoR2.TrialVariantEventRelay.IsAvailableServer += TrialVariantEventRelay_IsAvailableServer;

            On.RoR2.DoomTimer.HideTimersServer += DoomTimer_HideTimersServer;
            On.RoR2.ChampRushMissionController.CompleteReaperFight += ChampRush_CompleteReaperFight;

            On.RoR2.TrialVariantEventRelay.OnVariantChosen += TrialVariantEventRelay_OnVariantChosen;
            On.RoR2.GrantItemsToTeam.GrantItemsServer += GrantItemsToTeam_GrantItemsServer;
            On.RoR2.ItemCatalog.SetItemDefs += ItemCatalog_SetItemDefs; // potentially temporary hackjob for missing CanBeRental tags for items

            On.RoR2.DLC4TrialSelector.Awake += DLC4TrialSelector_Awake;
            On.RoR2.DLC4TrialSelector.CanAddTrial += DLC4TrialSelector_CanAddTrial;
            On.RoR2.DLC4TrialSelector.RemoveTrialSceneFromCollectionServer += DLC4TrialSelector_RemoveTrialSceneFromCollectionServer;
        }

        // timer editors
        private void DLC4TrialSelector_Awake(On.RoR2.DLC4TrialSelector.orig_Awake orig, DLC4TrialSelector self)
        {
            orig.Invoke(self);
            activeTrialSelector = self;

            if (NetworkServer.active)
            {
                // Log.Info($"The Reckoning will now happen in {startingTimer.Value} seconds.");
                self.SetRemainingTrialTimeServer(startingTimer.Value);
            }
        }

        private void Run_AdvanceStage(On.RoR2.Run.orig_AdvanceStage orig, Run self, SceneDef nextScene)
        {
            int stagePrev = self.stageClearCount;
            int loopPrev = self.loopClearCount;

            orig.Invoke(self, nextScene);

            if (!NetworkServer.active || !activeTrialSelector)
                return;

            float bonus = 0f;
            if (self.stageClearCount > stagePrev)
                bonus += timeAddedStage.Value;
            if (self.loopClearCount > loopPrev)
                bonus += timeAddedLoop.Value;

            if (bonus <= 0f)
                return;

            float remaining = activeTrialSelector.GetRemainingTrialTimeServer();

            // ritual portal disables at 0s in vanilla, it probably is due to some flag or something
            if (remaining <= 0f)
                return;

            activeTrialSelector.SetRemainingTrialTimeServer(remaining + bonus);
            // Log.Info($"TIME EXTENDED +{bonus}s ({remaining} -> {remaining + bonus})");
        }

        private void DoomTimer_HideTimersServer(On.RoR2.DoomTimer.orig_HideTimersServer orig, DoomTimer self)
        {
            if (timeAddedBossRush.Value && NetworkServer.active)
            {
                var deadlineBoxed = timeTilDoomsday.GetValue(self);
                if (deadlineBoxed != null)
                {
                    var deadline = (Run.FixedTimeStamp)deadlineBoxed;
                    var timePaused = (Run.FixedTimeStamp)timePause.GetValue(self);

                    float timeLeftover;
                    if (timePaused.isNegativeInfinity)
                        timeLeftover = deadline.timeUntilClamped;
                    else
                        timeLeftover = deadline.t - timePaused.t;

                    if (timeLeftover > 0f)
                        bossRushLeftoverTime = timeLeftover;
                }
            }

            orig(self);
        }

        private void ChampRush_CompleteReaperFight(On.RoR2.ChampRushMissionController.orig_CompleteReaperFight orig, ChampRushMissionController self)
        {
            orig(self);

            float timeLeftover = bossRushLeftoverTime;
            bossRushLeftoverTime = 0f;

            if (!timeAddedBossRush.Value || timeLeftover <= 0f || !NetworkServer.active || !activeTrialSelector)
                return;

            float trialTimer = activeTrialSelector.GetRemainingTrialTimeServer();
            if (trialTimer > 0f)
                activeTrialSelector.SetRemainingTrialTimeServer(trialTimer + timeLeftover);
        }

        // trial starting items
        private static void ItemCatalog_SetItemDefs(On.RoR2.ItemCatalog.orig_SetItemDefs orig, ItemDef[] newItemDefs)
        {
            foreach (var def in newItemDefs)
            {
                if (!def || def.hidden || !def.canRemove || def.isConsumed)
                    continue;
                if (!IsRentalTier(def) || def.tags.Contains(ItemTag.CanBeRental))
                    continue;

                def.tags = def.tags.Append(ItemTag.CanBeRental).ToArray();
            }
            orig(newItemDefs);
        }

        private static bool IsRentalTier(ItemDef def)
        {
            if (def.tags == null)
                def.tags = Array.Empty<ItemTag>();

            if (vanillaTiers.Contains(def.tier))
                return true;

            var tierDef = ItemTierCatalog.GetItemTierDef(def.tier);
            return tierDef && customTiers.Contains(tierDef.name);
        }

        private void TrialVariantEventRelay_OnVariantChosen(On.RoR2.TrialVariantEventRelay.orig_OnVariantChosen orig, TrialVariantEventRelay self)
        {
            currentTrial = self.customName;
            trialRentalGrant = false;
            try
            {
                orig(self);

                if (NetworkServer.active && !trialRentalGrant && trialStartItems.ContainsKey(self.customName))
                    GrantConfigItems(self.customName, TeamIndex.Player, true, true);
            }

            finally
            {
                currentTrial = null;
            }
        }

        private void GrantItemsToTeam_GrantItemsServer(On.RoR2.GrantItemsToTeam.orig_GrantItemsServer orig, GrantItemsToTeam self)
        {
            if (!NetworkServer.active || currentTrial == null || !trialStartItems.ContainsKey(currentTrial) || !enableCustomStartItems.Value)
            {
                orig(self);
                return;
            }

            trialRentalGrant = true;
            GrantConfigItems(currentTrial, self.affectedTeam, self.playerControlledOnly, self.isPickup);
        }

        private static void GrantConfigItems(string variant, TeamIndex team, bool playerOnly, bool isPickup)
        {
            if (!enableCustomStartItems.Value)
                return;

            var items = ParseItems(trialStartItems[variant].Value, variant);
            if (items.Count == 0)
                return;

            foreach (var master in CharacterMaster.readOnlyInstancesList)
            {
                if (!master || master.teamIndex != team)
                    continue;
                if (playerOnly && !master.playerCharacterMasterController)
                    continue;

                var body = master.GetBody();
                foreach (var (itemIndex, count) in items)
                {
                    master.inventory.GiveItemRented(itemIndex, count);

                    if (isPickup && body)
                        GlobalEventManager.OnCharacterPickupItem(body, PickupCatalog.FindPickupIndex(itemIndex), count);
                }
            }
        }

        private static List<(ItemIndex item, int count)> ParseItems(string raw, string label)
        {
            var result = new List<(ItemIndex item, int count)>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            foreach (var part in raw.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                    continue;

                var bits = trimmed.Split(':');
                var name = bits[0].Trim();

                int count = 1;
                if (bits.Length > 1 && !string.IsNullOrWhiteSpace(bits[1]) && !int.TryParse(bits[1].Trim(), out count))
                    continue;

                var idx = ItemCatalog.FindItemIndex(name);
                if (idx == ItemIndex.None)
                    continue;

                var itemDef = ItemCatalog.GetItemDef(idx);
                if (itemDef.requiredExpansion && (!Run.instance || !Run.instance.IsExpansionEnabled(itemDef.requiredExpansion)))
                    continue;

                if (count < 1)
                    continue;

                result.Add((idx, count));
            }
            return result;
        }

        // trial blacklist
        private static bool IsVariantEnabled(string customName)
        {
            return !trialVariantToggle.TryGetValue(customName, out var entry) || entry.Value;
        }

        private bool DLC4TrialSelector_CanAddTrial(On.RoR2.DLC4TrialSelector.orig_CanAddTrial orig, DLC4TrialSelector self, SceneDef sceneDef)
        {
            if (!orig.Invoke(self, sceneDef))
                return false;

            if (SceneVariants.TryGetValue(sceneDef.cachedName, out var scene))
                return scene.variants.Any(v => IsVariantEnabled(v.id));

            return true;
        }

        private bool TrialVariantEventRelay_IsAvailableServer(On.RoR2.TrialVariantEventRelay.orig_IsAvailableServer orig, TrialVariantEventRelay self)
        {
            return orig.Invoke(self) && IsVariantEnabled(self.customName);
        }


        // stage refresh
        private void DLC4TrialSelector_RemoveTrialSceneFromCollectionServer(On.RoR2.DLC4TrialSelector.orig_RemoveTrialSceneFromCollectionServer orig, DLC4TrialSelector self, TrialController trialController)
        {
            orig.Invoke(self, trialController);

            if (!NetworkServer.active || !Run.instance)
                return;

            // if you put the stages back but don't reset the event flags that tag their completion, you just get softlocked into an empty map
            foreach (var variant in trialController.trialVariants)
            {
                if (variant.exhaustOnSuccess)
                    Run.instance.ResetEventFlag($"{variant.customName}_Exhausted");
            }

            if (self.IsAnyTrialAvailableServer())
                return;

            var selection = (WeightedSelection<SceneDef>)trialSelectionField.GetValue(self);
            var filter = (Func<SceneDef, bool>)Delegate.CreateDelegate(typeof(Func<SceneDef, bool>), self, canAddTrialMethod);

            self.trialsSceneCollection.AddToWeightedSelection(selection, filter);
            // Log.Info($"All trial objectives have been refreshed.");
        }
    }

    public static class RiskOfOptionsCompat
    {
        private static bool? _enabled;
        public static bool Enabled => _enabled ??= Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AddCheckbox(ConfigEntry<bool> entry, bool restartRequired = false) => ModSettingsManager.AddOption(new CheckBoxOption(entry, restartRequired));

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AddFloatField(ConfigEntry<float> entry) => ModSettingsManager.AddOption(new FloatFieldOption(entry));

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AddStringField(ConfigEntry<string> entry) => ModSettingsManager.AddOption(new StringInputFieldOption(entry));
    }
}
