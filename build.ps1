using System;
using System.Collections.Generic;
using System.Linq;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Central, bounded evidence store.
    ///
    /// Evidence is intentionally ephemeral. Contract Configurator persists its
    /// own parameter completion state, so this bridge does not need to write
    /// custom save-game nodes once a parameter has consumed an observation.
    /// </summary>
    internal static class EvidenceStore
    {
        private static readonly object Sync = new object();
        private static readonly List<ScienceEvidence> Items =
            new List<ScienceEvidence>();

        internal static void Add(ScienceEvidence evidence)
        {
            if (evidence == null || string.IsNullOrEmpty(evidence.StockSubjectId))
                return;

            lock (Sync)
            {
                // Avoid unbounded duplicates from repeated update calls.
                Items.RemoveAll(e =>
                    e.VesselId == evidence.VesselId &&
                    e.Kind == evidence.Kind &&
                    string.Equals(e.StockSubjectId, evidence.StockSubjectId,
                        StringComparison.Ordinal));

                Items.Add(evidence);
            }

            Log.DebugMessage("Recorded evidence: " + evidence);
        }

        internal static IList<ScienceEvidence> Snapshot(double lifetimeSeconds)
        {
            double now = Planetarium.GetUniversalTime();

            lock (Sync)
            {
                Items.RemoveAll(e => now - e.UniversalTime > lifetimeSeconds);
                return Items.ToList();
            }
        }

        internal static void Clear()
        {
            lock (Sync)
                Items.Clear();
        }
    }
}
