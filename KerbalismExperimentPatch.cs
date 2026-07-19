using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Single KSP lifecycle component.
    ///
    /// Starts at MainMenu because:
    /// - GameDatabase and ModuleManager results are available;
    /// - Contract Configurator types have been loaded;
    /// - Kerbalism has initialized its assembly;
    /// - Harmony patches are installed before a career flight begins.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal sealed class BridgeRuntime : MonoBehaviour
    {
        internal static BridgeSettings Settings { get; private set; }

        private Harmony harmony;
        private float nextTransmissionPoll;
        private Assembly kerbalismAssembly;

        private void Awake()
        {
            DontDestroyOnLoad(this);

            Settings = BridgeSettings.Load();
            Log.Verbose = Settings.DebugLogging;

            kerbalismAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetType("KERBALISM.Experiment", false) != null);

            if (kerbalismAssembly == null)
            {
                Log.Warning(
                    "Kerbalism assembly was not found. Bridge remains inactive.");
                enabled = false;
                return;
            }

            try
            {
                harmony = new Harmony(
                    "dk.theishansen.kerbalism-contract-science-bridge");

                // Attribute patch: Contract Configurator CollectScience.
                harmony.PatchAll(typeof(BridgeRuntime).Assembly);

                // Dynamic patch: no compile-time Kerbalism reference.
                KerbalismExperimentPatch.Install(harmony, kerbalismAssembly);

                GameEvents.onGameStateLoad.Add(OnGameStateLoad);
                GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);

                Log.Info("Bridge initialized.");
            }
            catch (Exception ex)
            {
                Log.Error("Initialization failed; patches removed.\n" + ex);
                SafeUnpatch();
                enabled = false;
            }
        }

        private void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            if (Time.realtimeSinceStartup < nextTransmissionPoll)
                return;

            nextTransmissionPoll = Time.realtimeSinceStartup +
                (float)Settings.TransmissionPollSeconds;

            if (Settings.CompleteOnTransmissionStart)
                KerbalismTransmissionScanner.ScanAllVessels(kerbalismAssembly);

            // Unlike Contract Configurator's CollectScienceCustom (patched via
            // Harmony postfix on its own OnUpdate), the stock
            // CollectROCScienceArm parameter is not patched -- OnScience is
            // only invoked directly when we choose to. Polling here on the
            // same cadence as transmission scanning, using whatever evidence
            // is currently in the store, is simpler and safer than adding a
            // second Harmony patch for a private method.
            try
            {
                RocScienceArmBridge.ProcessAllEvidence();
            }
            catch (Exception ex)
            {
                Log.DebugMessage(
                    "RocScienceArmBridge polling failed safely: " + ex.Message);
            }
        }

        private void OnGameStateLoad(ConfigNode node)
        {
            EvidenceStore.Clear();
        }

        private void OnSceneLoadRequested(GameScenes scene)
        {
            // Prevent old evidence from one flight satisfying a parameter after
            // switching vessels/scenes. Contract completion already persists in
            // Contract Configurator itself.
            EvidenceStore.Clear();
        }

        private void OnDestroy()
        {
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
            SafeUnpatch();
        }

        private void SafeUnpatch()
        {
            if (harmony == null)
                return;

            try
            {
                harmony.UnpatchAll(harmony.Id);
            }
            catch (Exception ex)
            {
                Log.Warning("Unpatch failed during shutdown: " + ex.Message);
            }
        }
    }
}
