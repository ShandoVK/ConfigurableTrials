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
        public const string PluginVersion = "0.5.1";

        private static readonly FieldInfo trialSelectionField = typeof(DLC4TrialSelector).GetField("_trialSelection", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo canAddTrialMethod = typeof(DLC4TrialSelector).GetMethod("CanAddTrial", BindingFlags.NonPublic | BindingFlags.Instance);
        private static DLC4TrialSelector activeTrialSelector;

        public static ConfigEntry<float> startingTimer;
        public static ConfigEntry<float> timeAddedStage;
        public static ConfigEntry<float> timeAddedLoop;

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

            // options compat
            if (RiskOfOptionsCompat.Enabled)
            {
                RiskOfOptionsCompat.AddFloatField(startingTimer);
                RiskOfOptionsCompat.AddFloatField(timeAddedStage);
                RiskOfOptionsCompat.AddFloatField(timeAddedLoop);

                foreach (var toggle in trialVariantToggle.Values)
                    RiskOfOptionsCompat.AddCheckbox(toggle);
            }

            On.RoR2.Run.AdvanceStage += Run_AdvanceStage;
            On.RoR2.TrialVariantEventRelay.IsAvailableServer += TrialVariantEventRelay_IsAvailableServer;

            On.RoR2.DLC4TrialSelector.Awake += DLC4TrialSelector_Awake;
            On.RoR2.DLC4TrialSelector.CanAddTrial += DLC4TrialSelector_CanAddTrial;
            On.RoR2.DLC4TrialSelector.RemoveTrialFromSelectionServer += DLC4TrialSelector_RemoveTrialFromSelectionServer;
            On.RoR2.DLC4TrialSelector.RemoveTrialSceneFromCollectionServer += DLC4TrialSelector_RemoveTrialSceneFromCollectionServer;
        }

        // timer editor
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

        // trial blacklist handlers
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


        // stage refresh handlers
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
            // Log.Info($"All trial objectives have been refreshed.");
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
