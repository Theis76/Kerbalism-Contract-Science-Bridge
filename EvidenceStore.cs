using System;
using System.Reflection;
using HarmonyLib;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Dynamic Harmony patch for KERBALISM.Experiment.State.set.
    ///
    /// Why patch the setter?
    /// Kerbalism 3.32 centralizes state transitions in the State property and
    /// notifies its own API there. Patching that exact transition avoids
    /// per-frame scanning and catches user actions consistently.
    ///
    /// The postfix only records evidence when:
    /// - the resulting state is Running or Forced;
    /// - the module belongs to a vessel;
    /// - Kerbalism has resolved a concrete SubjectData;
    /// - that SubjectData exposes a stock-compatible subject ID.
    ///
    /// Waiting/Issue states are deliberately not accepted as a start here.
    /// They can mean the switch is on while no science is being produced.
    /// </summary>
    internal static class KerbalismExperimentPatch
    {
        internal static bool Install(Harmony harmony, Assembly kerbalismAssembly)
        {
            Type experimentType =
                kerbalismAssembly.GetType("KERBALISM.Experiment", false);

            if (experimentType == null)
            {
                Log.Warning("KERBALISM.Experiment was not found.");
                return false;
            }

            PropertyInfo stateProperty = experimentType.GetProperty(
                "State", ReflectionUtil.InstanceFlags);

            MethodInfo setter = stateProperty != null
                ? stateProperty.GetSetMethod(true)
                : null;

            if (setter == null)
            {
                Log.Warning("KERBALISM.Experiment.State setter was not found.");
                return false;
            }

            MethodInfo postfix = typeof(KerbalismExperimentPatch).GetMethod(
                nameof(StateSetterPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            harmony.Patch(setter, postfix: new HarmonyMethod(postfix));
            Log.Info("Patched Kerbalism experiment state changes.");
            return true;
        }

        private static void StateSetterPostfix(object __instance)
        {
            try
            {
                if (!BridgeRuntime.Settings.CompleteOnExperimentStart)
                    return;

                object state = ReflectionUtil.Get(__instance, "State");
                string stateName = state != null ? state.ToString() : string.Empty;

                if (!string.Equals(stateName, "Running", StringComparison.Ordinal) &&
                    !string.Equals(stateName, "Forced", StringComparison.Ordinal))
                {
                    return;
                }

                PartModule module = __instance as PartModule;
                Vessel vessel = module != null ? module.vessel : null;
                if (vessel == null)
                    return;

                object subjectData = ReflectionUtil.Get(__instance, "Subject");
                string stockSubjectId;
                string experimentId;

                if (!KerbalismSubjectReader.TryRead(
                    subjectData, out stockSubjectId, out experimentId))
                {
                    Log.DebugMessage(
                        "Experiment entered " + stateName +
                        " without a resolved SubjectData; no evidence recorded.");
                    return;
                }

                if (string.IsNullOrEmpty(experimentId))
                {
                    experimentId = ReflectionUtil.Get<string>(
                        __instance, "experiment_id", null);
                }

                EvidenceStore.Add(new ScienceEvidence
                {
                    VesselId = vessel.id,
                    StockSubjectId = stockSubjectId,
                    ExperimentId = experimentId,
                    Kind = ScienceEvidence.EvidenceKind.ExperimentStarted,
                    UniversalTime = Planetarium.GetUniversalTime()
                });
            }
            catch (Exception ex)
            {
                Log.Warning("Experiment state postfix failed safely: " + ex);
            }
        }
    }
}
