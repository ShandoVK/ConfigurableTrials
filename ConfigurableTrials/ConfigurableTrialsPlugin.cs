using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.ExpansionManagement;
using RiskOfOptions;
using RiskOfOptions.Options;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Networking;

/* todo
 * 6. figure out how to get umbras with toggle on to look like Umbras
 * 8. Test mods that edit similar parameters (Karma Time Up)
 */

namespace ConfigurableTrialPlugin
{

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]

    public class ConfigurableTrials : BaseUnityPlugin
    {
        public const string PluginGUID = "ShandoIsSad.ConfigurableTrials";
        public const string PluginAuthor = "ShandoVK";
        public const string PluginName = "ConfigurableTrials";
        public const string PluginVersion = "0.5.0";

        // logic vars
        private static readonly FieldInfo trialSelectionField = typeof(DLC4TrialSelector).GetField("_trialSelection", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo canAddTrialMethod = typeof(DLC4TrialSelector).GetMethod("CanAddTrial", BindingFlags.NonPublic | BindingFlags.Instance);
        private static DLC4TrialSelector activeTrialSelector;

        // config vars
        public static ConfigEntry<float> startingTimer;
        public static ConfigEntry<float> timeAddedStage;
        public static ConfigEntry<float> timeAddedLoop;
        public static ConfigEntry<bool> moreUmbra;

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

        // item & survivor blacklist pool
        private static readonly HashSet<string> umbraSurvivorBL = new HashSet<string>
        {
            "ToolbotMonsterMasterVariant",
            "TreebotMonsterMaster",
            "HereticMonsterMaster",
            "VoidSurvivorMonsterMaster",
            "FalseSonMonsterMaster",
            "AcolyteMonsterMaster"
        };

        private static readonly (string name, int count)[] umbraItems =
        {
            ("SecondarySkillMagazine", 1),
            ("ArmorReductionOnHit", 1),
            ("AlienHead", 2),
            ("HealWhileSafe", 6),
            ("Phasing", 1),
            ("AutoCastEquipment", 5),
            ("Medkit", 1),
            ("Seed", 5),
            ("Feather", 10),
            ("EquipmentMagazine", 1),
            ("ArmorPlate", 3),
            ("Syringe", 1),
            ("Mushroom", 10),
        };

        private static readonly (string name, float weight, string dlc, bool loopOnly)[] umbraEquipment =
        {
            ("TeamWarCry", 1f, null, false),
            ("Jetpack", 1f, null, false),
            ("VendingMachine", 1f, "DLC1", false),
            ("PassiveHealing", 1f, null, false),
            ("QuestVolatileBattery", 0.01f, null, false),
            ("EliteFireEquipment", 0.5f, null, false),
            ("EliteIceEquipment", 0.5f, null, false),
            ("EliteLightningEquipment", 0.25f, null, false),
            ("EliteEarthEquipment", 0.25f, "DLC1", false),
            ("ElitePoisonEquipment", 0.1f, null, true),
            ("EliteHauntedEquipment", 0.05f, null, true),
            ("EliteBeadEquipment", 0.1f, "DLC2", true),
            ("EliteCollectiveEquipment", 0.05f, "DLC3", true),
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

            foreach (var scene in SceneVariants.Values)
            {
                foreach (var (trialId, trialName) in scene.variants)
                {
                    trialVariantToggle[trialId] = Config.Bind(
                        scene.section,
                        trialId,
                        true,
                        $"Allows the '{trialName}' to appear");
                }
            }

            moreUmbra = Config.Bind(
                "WTF was that",
                "More varied umbra spawns",
                false,
                "When enabled, more survivors, items, and equipment will be added to the Trial of the Lesser Echoes. May make the trial noticeably harder");

            // options compat
            if (RiskOfOptionsCompat.Enabled)
            {
                RiskOfOptionsCompat.AddFloatField(startingTimer);
                RiskOfOptionsCompat.AddFloatField(timeAddedStage);
                RiskOfOptionsCompat.AddFloatField(timeAddedLoop);

                foreach (var toggle in trialVariantToggle.Values)
                    RiskOfOptionsCompat.AddCheckbox(toggle);

                RiskOfOptionsCompat.AddCheckbox(moreUmbra);
            }

            On.RoR2.Run.AdvanceStage += Run_AdvanceStage;
            On.RoR2.DirectorCore.TrySpawnObject += DirectorCore_TrySpawnObject;
            On.RoR2.TrialVariantEventRelay.IsAvailableServer += TrialVariantEventRelay_IsAvailableServer;

            On.RoR2.DLC4TrialSelector.Awake += DLC4TrialSelector_Awake;
            On.RoR2.DLC4TrialSelector.CanAddTrial += DLC4TrialSelector_CanAddTrial;
            On.RoR2.DLC4TrialSelector.RemoveTrialFromSelectionServer += DLC4TrialSelector_RemoveTrialFromSelectionServer;
            On.RoR2.DLC4TrialSelector.RemoveTrialSceneFromCollectionServer += DLC4TrialSelector_RemoveTrialSceneFromCollectionServer;
        }

        // timer
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

            // ritual portal disables at 0s in vanilla, it probably is due to some flag and can't be arsed to change it
            if (remaining <= 0f)
                return;

            activeTrialSelector.SetRemainingTrialTimeServer(remaining + bonus);
            // Log.Info($"TIME EXTENDED +{bonus}s ({remaining} -> {remaining + bonus})");
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


        // stage refresh, happens when all available trial stages are done
        private void DLC4TrialSelector_RemoveTrialFromSelectionServer(On.RoR2.DLC4TrialSelector.orig_RemoveTrialFromSelectionServer orig, DLC4TrialSelector self, SceneDef trialScene)
        {
            orig.Invoke(self, trialScene);

            if (!NetworkServer.active)
                return;

            if (self.IsAnyTrialAvailableServer())
                return;

            var selection = (WeightedSelection<SceneDef>)trialSelectionField.GetValue(self);
            var filter = (Func<SceneDef, bool>)Delegate.CreateDelegate(typeof(Func<SceneDef, bool>), self, canAddTrialMethod);

            self.trialsSceneCollection.AddToWeightedSelection(selection, filter);
        }

        // I don't know why Gearbox also yeeted the objectives (scene variants) per stage separately through event flags but this'll ensure variants aren't just empty maps that softlock you
        private void DLC4TrialSelector_RemoveTrialSceneFromCollectionServer(On.RoR2.DLC4TrialSelector.orig_RemoveTrialSceneFromCollectionServer orig, DLC4TrialSelector self, TrialController trialController)
        {
            orig.Invoke(self, trialController);

            if (!NetworkServer.active || !Run.instance)
                return;

            foreach (var variant in trialController.trialVariants)
            {
                if (variant.exhaustOnSuccess)
                    Run.instance.ResetEventFlag($"{variant.customName}_Exhausted");
            }
            // Log.Info($"All trial objectives have been refreshed.");
        }


        // all of this is for the funny umbra monster mash
        private static bool DLCRequired(string name)
        {
            if (name == null)
                return true;

            foreach (var def in ExpansionCatalog.expansionDefs)
            {
                if (def.name == name && Run.instance.IsExpansionEnabled(def))
                    return true;
            }

            return false;
        }

        private static bool IsMasterAllowed(CharacterMaster master)
        {
            if (!master || !master.bodyPrefab)
                return false;

            if (umbraSurvivorBL.Contains(master.name))
                return false;

            if (!SurvivorCatalog.FindSurvivorDefFromBody(master.bodyPrefab))
                return false;

            return IsExpansionRequirementMet(master.gameObject) && IsExpansionRequirementMet(master.bodyPrefab);
        }

        private static bool IsExpansionRequirementMet(GameObject obj)
        {
            var requirement = obj.GetComponent<ExpansionRequirementComponent>();

            if (!requirement || !requirement.requiredExpansion)
                return true;

            return Run.instance.IsExpansionEnabled(requirement.requiredExpansion);
        }

        // you may call this easy mode, I call it "I got tired of CHEF roasting me alive in 0.5 seconds"
        private static void scaleUmbra(CharacterBody body)
        {
            if (!body) return;
            body.levelMaxHealth *= 0.8f;
            body.baseDamage *= 0.5f;
            body.levelDamage *= 0.1f;
            body.statsDirty = true;
        }

        private static GameObject pickUmbraSurvivor()
        {
            var pool = MasterCatalog.allMasters.Where(IsMasterAllowed).ToList();
            return pool.Count == 0 ? null : pool[UnityEngine.Random.Range(0, pool.Count)].gameObject;
        }

        private static (string name, int count) pickUmbraItems() => umbraItems[UnityEngine.Random.Range(0, umbraItems.Length)];

        private static string pickUmbraEquipment()
        {
            bool looped = Run.instance.loopClearCount > 0;
            var options = umbraEquipment.Where(e => (!e.loopOnly || looped) && DLCRequired(e.dlc)).ToList();
            if (options.Count == 0) return null;

            float roll = UnityEngine.Random.value * options.Sum(e => e.weight);
            foreach (var e in options)
                if ((roll -= e.weight) <= 0f) return e.name;
            return options[options.Count - 1].name;
        }

        private GameObject DirectorCore_TrySpawnObject(On.RoR2.DirectorCore.orig_TrySpawnObject orig, DirectorCore self, DirectorSpawnRequest request)
        {
            var card = request?.spawnCard as CharacterSpawnCard;
            var scene = SceneInfo.instance ? SceneInfo.instance.sceneDef : null;

            if (!moreUmbra.Value || !NetworkServer.active || !Run.instance || !card || !scene || scene.cachedName != "surv2" || !card.name.StartsWith("cscCommandoUmbraMaster"))
                return orig.Invoke(self, request);

            var masterPrefab = pickUmbraSurvivor();
            if (!masterPrefab)
                return orig.Invoke(self, request);

            string equipment = pickUmbraEquipment();
            var (itemName, itemCount) = pickUmbraItems();

            var swapped = UnityEngine.Object.Instantiate(card);
            swapped.name = card.name;
            swapped.prefab = masterPrefab;
            var body = masterPrefab.GetComponent<CharacterMaster>().bodyPrefab.GetComponent<CharacterBody>();
            if (body) swapped.hullSize = body.hullClassification;

            var originalCallback = request.onSpawnedServer;
            request.spawnCard = swapped;
            request.onSpawnedServer = result =>
            {
                originalCallback?.Invoke(result);

                var master = result.spawnedInstance ? result.spawnedInstance.GetComponent<CharacterMaster>() : null;
                if (!master) return;

                if (equipment != null && master.inventory)
                    master.inventory.GiveEquipmentString(equipment);

                var itemIndex = ItemCatalog.FindItemIndex(itemName);
                if (itemIndex != ItemIndex.None && master.inventory)
                    master.inventory.GiveItemPermanent(itemIndex, itemCount);

                var spawnedBody = master.GetBody();
                scaleUmbra(spawnedBody);
            };

            // Log.Info($"Spawned {masterPrefab.name} with {equipment ?? "nothing"}");

            try
            {
                return orig.Invoke(self, request);
            }

            finally
            {
                request.spawnCard = card;
                request.onSpawnedServer = originalCallback;
                UnityEngine.Object.Destroy(swapped);
            }
        }
    }

    public static class RiskOfOptionsCompat
    {
        private static bool? _enabled;
        public static bool Enabled => _enabled ??= Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AddCheckbox(ConfigEntry<bool> entry) => ModSettingsManager.AddOption(new CheckBoxOption(entry));

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void AddFloatField(ConfigEntry<float> entry) => ModSettingsManager.AddOption(new FloatFieldOption(entry));
    }
}
