using System;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// A short-lived observation that Kerbalism started producing or
    /// transmitting one exact stock-compatible science subject.
    ///
    /// Kerbalism SubjectData exposes StockSubjectId. Keeping that complete ID
    /// is safer than rebuilding IDs from experiment/body/situation/biome names:
    /// it preserves Kerbalism's handling of body restrictions, virtual biomes,
    /// asteroid subjects and non-standard experiment definitions.
    /// </summary>
    internal sealed class ScienceEvidence
    {
        internal enum EvidenceKind
        {
            ExperimentStarted,
            TransmissionStarted
        }

        internal Guid VesselId { get; set; }
        internal string StockSubjectId { get; set; }
        internal string ExperimentId { get; set; }
        internal EvidenceKind Kind { get; set; }
        internal double UniversalTime { get; set; }

        public override string ToString()
        {
            return Kind + ": " + StockSubjectId + " on " + VesselId;
        }
    }
}
