using System;
using System.Collections.Generic;
using System.Reflection;
using Contracts;
using Expansions.Serenity.Contracts;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Bridges Kerbalism evidence (experiment started / transmission started)
    /// to the STOCK Breaking Ground scanning-arm contract parameter,
    /// Expansions.Serenity.Contracts.CollectROCScienceArm.
    ///
    /// This is a different problem from ContractScienceMatcher.cs, not a
    /// variation of it: CollectROCScienceArm is not a Contract Configurator
    /// parameter. It ships in the base game (Assembly-CSharp.dll) and
    /// completes itself via a *private* OnScience(float, ScienceSubject,
    /// ProtoVessel, bool) method that is meant to run as a handler for the
    /// stock GameEvents.OnScienceReceived event.
    ///
    /// Per the Kerbalism project's own issue tracker
    /// (github.com/Kerbalism/Kerbalism, issue #588), Kerbalism trickles
    /// science into the player's inventory as an experiment runs, and only
    /// fires that stock event once an experiment's data reaches roughly 95%
    /// completion internally. For a background experiment that can take a
    /// very long time, and never happens at all if the goal is "started
    /// transmitting", as requested. Confirmed by the reporter directly: the
    /// stock contract still did not complete even after transmission had
    /// fully finished.
    ///
    /// This class reuses the exact same Kerbalism evidence
    /// (ScienceEvidence/EvidenceStore) that ContractScienceMatcher consumes
    /// for Contract Configurator, and the same ResolveSubject() fallback for
    /// building a ScienceSubject when R&D hasn't registered one yet. Rather
    /// than re-deriving CollectROCScienceArm's internal completion math (not
    /// visible via reflection, only its signature is), it calls the real,
    /// private OnScience() method directly with the subject's full science
    /// value -- trusting the stock game's own logic to decide completion,
    /// just triggered early instead of waiting for Kerbalism's ~95% event.
    ///
    /// Related, NOT addressed here: Expansions.Serenity.Contracts.
    /// CollectROCScienceRetrieval (the "bring the rock home" variant) has a
    /// separate, structural incompatibility -- Kerbalism/Kerbalism#476 -- it
    /// looks for stock ScienceData on a ProtoPartModuleSnapshot, which
    /// Kerbalism never populates because it removes ModuleScienceContainer
    /// entirely. That is a different parameter type and a different failure
    /// mode from the one reported (a scanning arm, i.e. the transmit/analyze-
    /// in-place variant), so it is out of scope for this class.
    /// </summary>
    internal static class RocScienceArmBridge
    {
        private static readonly HashSet<string> CompletedKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool resolvedOnScience;
        private static MethodInfo onScienceMethod;

        internal static void ProcessAllEvidence()
        {
            if (ContractSystem.Instance == null)
                return;

            IList<ScienceEvidence> evidence = EvidenceStore.Snapshot(
                BridgeRuntime.Settings.EvidenceLifetimeSeconds);

            if (evidence.Count == 0)
                return;

            if (!resolvedOnScience)
                ResolveOnScience();

            if (onScienceMethod == null)
                return;

            int activeContracts = 0;
            int rocParametersSeen = 0;

            foreach (Contract contract in ContractSystem.Instance.Contracts)
            {
                if (contract == null || contract.ContractState != Contract.State.Active)
                    continue;

                activeContracts++;

                try
                {
                    rocParametersSeen += ScanContract(contract, evidence);
                }
                catch (Exception ex)
                {
                    Log.DebugMessage(
                        "RocScienceArmBridge: skipped a contract after an " +
                        "error: " + ex.Message);
                }
            }

            // Always visible, not just under debugLogging: this is the one
            // line that tells us whether the bridge is even looking at the
            // right contracts at all, independent of any subject-id match.
            Log.DebugMessage(
                "RocScienceArmBridge: polled with " + evidence.Count +
                " evidence item(s) pending; " + activeContracts +
                " active contract(s) scanned; " + rocParametersSeen +
                " incomplete CollectROCScienceArm parameter(s) found.");
        }

        /// <summary>
        /// OnScience is deliberately private on the real class (confirmed by
        /// reflection against the user's own Assembly-CSharp.dll) -- it is
        /// only ever meant to be called by KSP itself as a
        /// GameEvents.OnScienceReceived handler. Reflection is required to
        /// invoke it at all; there is no public alternative to call instead.
        /// </summary>
        private static void ResolveOnScience()
        {
            resolvedOnScience = true;

            onScienceMethod = typeof(CollectROCScienceArm).GetMethod(
                "OnScience", BindingFlags.Instance | BindingFlags.NonPublic);

            if (onScienceMethod == null)
            {
                Log.Warning(
                    "CollectROCScienceArm.OnScience was not found; the " +
                    "stock scanning-arm bridge is disabled for this session.");
            }
        }

        private static int ScanContract(
            Contract contract, IList<ScienceEvidence> evidence)
        {
            int found = 0;

            foreach (ContractParameter parameter in contract.AllParameters)
            {
                CollectROCScienceArm rocParam = parameter as CollectROCScienceArm;
                if (rocParam == null)
                    continue;

                if (rocParam.State != ParameterState.Incomplete)
                    continue;

                found++;

                // Always visible: the ground truth needed to diagnose a
                // subject-id mismatch between what the contract expects and
                // what Kerbalism actually credits science under. Cheap: at
                // most a handful of ROC parameters are ever active at once.
                Log.DebugMessage(
                    "RocScienceArmBridge: incomplete ROC parameter on \"" +
                    contract.Title + "\" wants subject '" +
                    (rocParam.SubjectId ?? "<null>") + "'");

                foreach (ScienceEvidence item in evidence)
                {
                    Log.DebugMessage(
                        "RocScienceArmBridge: comparing against evidence '" +
                        item.StockSubjectId + "' (" + item.Kind + " on " +
                        item.VesselId + ")");

                    try
                    {
                        if (TryComplete(rocParam, item))
                            break; // this parameter is done; move to the next one
                    }
                    catch (Exception ex)
                    {
                        Log.DebugMessage(
                            "RocScienceArmBridge: evidence application " +
                            "failed safely: " + ex.Message);
                    }
                }
            }

            return found;
        }

        private static bool TryComplete(
            CollectROCScienceArm rocParam, ScienceEvidence evidence)
        {
            string wantedSubjectId = rocParam.SubjectId;
            if (string.IsNullOrEmpty(wantedSubjectId) ||
                !string.Equals(wantedSubjectId, evidence.StockSubjectId,
                    StringComparison.Ordinal))
                return false;

            // Guards against calling OnScience repeatedly for a parameter
            // that is already complete or that we already drove to
            // completion this session; ContractGuid + subject id uniquely
            // identifies "this specific objective on this specific contract".
            string key = rocParam.Root.ContractGuid + "|" + wantedSubjectId;
            if (CompletedKeys.Contains(key))
                return true;

            Vessel vessel = FlightGlobals.Vessels.Find(
                v => v != null && v.id == evidence.VesselId);

            if (vessel == null || vessel.protoVessel == null)
            {
                Log.DebugMessage(
                    "RocScienceArmBridge: subject matched (" + wantedSubjectId +
                    ") but vessel " + evidence.VesselId + " could not be " +
                    "resolved; evidence left unconsumed.");
                return false;
            }

            // Same fallback ContractScienceMatcher uses: prefer a real,
            // R&D-registered subject when one exists (Kerbalism does
            // register ROC subjects directly -- see the "CreateSubjectInRnD"
            // log line -- so this usually succeeds immediately for ROC
            // subjects), otherwise build a transient, non-persisted one.
            ScienceSubject subject = ContractScienceMatcher.ResolveSubject(
                wantedSubjectId);

            if (subject == null)
            {
                Log.DebugMessage(
                    "RocScienceArmBridge: could not resolve a subject for " +
                    wantedSubjectId + "; evidence left unconsumed.");
                return false;
            }

            // Pass the subject's full value so that whatever internal
            // percentage/threshold OnScience checks is unambiguously met in
            // one call -- we are not trying to reproduce that math
            // ourselves, only to trigger the real logic early.
            float science = subject.scienceCap > 0f
                ? subject.scienceCap
                : subject.science;

            if (science <= 0f)
                science = 1f;

            Log.DebugMessage(
                "RocScienceArmBridge: subject matched (" + wantedSubjectId +
                "); calling OnScience(science=" + science +
                ", subject.id=" + subject.id + ", subject.scienceCap=" +
                subject.scienceCap + ", subject.science=" + subject.science +
                ")");

            onScienceMethod.Invoke(
                rocParam,
                new object[] { science, subject, vessel.protoVessel, false });

            Log.DebugMessage(
                "RocScienceArmBridge: after OnScience(), parameter State = " +
                rocParam.State + " (ScienceCollected=" +
                rocParam.ScienceCollected + ", SciencePercentage=" +
                rocParam.SciencePercentage + ")");

            if (rocParam.State == ParameterState.Incomplete)
            {
                // OnScience ran without throwing but did not consider this
                // enough -- leave the evidence available for a later poll
                // rather than assuming completion.
                return false;
            }

            CompletedKeys.Add(key);
            Log.Info(
                "RocScienceArmBridge: completed " + wantedSubjectId +
                " on contract \"" + rocParam.Root.Title + "\" via OnScience().");
            return true;
        }
    }
}
