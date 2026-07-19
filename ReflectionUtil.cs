using System;
using UnityEngine;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Immutable runtime settings loaded from the single
    /// KERBALISM_CONTRACT_SCIENCE_BRIDGE configuration node.
    ///
    /// Defaults are deliberately conservative:
    /// - the bridge only supplements stock behaviour;
    /// - missing or malformed settings never disable stock Contract Configurator;
    /// - no setting can grant science or mutate Kerbalism data.
    /// </summary>
    internal sealed class BridgeSettings
    {
        internal bool DebugLogging { get; private set; } = false;
        internal bool CompleteOnExperimentStart { get; private set; } = true;
        internal bool CompleteOnTransmissionStart { get; private set; } = true;
        internal bool IgnoreRecoveryMethod { get; private set; } = true;
        internal double TransmissionPollSeconds { get; private set; } = 0.50;
        internal double EvidenceLifetimeSeconds { get; private set; } = 30.0;

        internal static BridgeSettings Load()
        {
            var result = new BridgeSettings();

            try
            {
                ConfigNode[] nodes = GameDatabase.Instance
                    .GetConfigNodes("KERBALISM_CONTRACT_SCIENCE_BRIDGE");

                if (nodes == null || nodes.Length == 0)
                    return result;

                // The last node wins, matching the normal expectation after
                // ModuleManager has applied :NEEDS/:AFTER patches.
                ConfigNode node = nodes[nodes.Length - 1];

                result.DebugLogging =
                    ReadBool(node, "debugLogging", result.DebugLogging);
                result.CompleteOnExperimentStart =
                    ReadBool(node, "completeOnExperimentStart",
                        result.CompleteOnExperimentStart);
                result.CompleteOnTransmissionStart =
                    ReadBool(node, "completeOnTransmissionStart",
                        result.CompleteOnTransmissionStart);
                result.IgnoreRecoveryMethod =
                    ReadBool(node, "ignoreRecoveryMethod",
                        result.IgnoreRecoveryMethod);

                result.TransmissionPollSeconds =
                    Math.Max(0.10, ReadDouble(node, "transmissionPollSeconds",
                        result.TransmissionPollSeconds));

                result.EvidenceLifetimeSeconds =
                    Math.Max(1.0, ReadDouble(node, "evidenceLifetimeSeconds",
                        result.EvidenceLifetimeSeconds));
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[KerbalismContractScienceBridge] Failed to load settings. " +
                    "Using defaults.\n" + ex);
            }

            return result;
        }

        private static bool ReadBool(ConfigNode node, string key, bool fallback)
        {
            string raw = node.GetValue(key);
            bool parsed;
            return raw != null && bool.TryParse(raw, out parsed) ? parsed : fallback;
        }

        private static double ReadDouble(ConfigNode node, string key, double fallback)
        {
            string raw = node.GetValue(key);
            double parsed;
            return raw != null && double.TryParse(raw, out parsed) ? parsed : fallback;
        }
    }
}
