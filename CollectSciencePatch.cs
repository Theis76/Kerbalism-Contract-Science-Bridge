using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Detects actual Kerbalism 3.32 transmission by requiring File.transmitRate > 0.
    ///
    /// REVIEW FIX (0.3.2): the previous version only looked at drives reachable
    /// through a "KERBALISM.HardDrive" PartModule (HardDrive.drive). That
    /// misses any experiment that stores its file on a *private* drive --
    /// Kerbalism gives every experiment module a private drive
    /// (Experiment.privateHdId) for vessels/parts with no dedicated storage
    /// part, and several experiment types (reported: a scanning-arm-style
    /// experiment; sample and crew-report experiments were unaffected because
    /// those complete on experiment start, not on transmission) use exactly
    /// that private drive rather than a visible HardDrive part. Because the
    /// scanner never looked at private drives, their files' transmitRate was
    /// never observed and the "start transmission" objective could never
    /// fire for them.
    ///
    /// Fixed by calling Kerbalism's own KERBALISM.Drive.GetDrives(Vessel, bool)
    /// / GetDrives(ProtoVessel, bool) with includePrivate: true, which is the
    /// same lookup Kerbalism's own UI and background processing use to find
    /// every drive belonging to a vessel, public or private. This also lets a
    /// loaded/unloaded vessel be scanned the same way (GetDrives has a
    /// ProtoVessel overload), which removes the previous "unloaded vessel
    /// transmission not supported" limitation for the same reason.
    ///
    /// Verified 3.32 object graph:
    ///   Drive.GetDrives(Vessel|ProtoVessel, includePrivate:true) -> Drive.files
    ///     -> File.transmitRate / File.subjectData
    /// </summary>
    internal static class KerbalismTransmissionScanner
    {
        private static readonly HashSet<string> ActiveKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool membersResolved;
        private static bool resolutionFailed;
        private static MethodInfo getDrivesByVesselMethod;
        private static MethodInfo getDrivesByProtoVesselMethod;

        internal static void ScanAllVessels(Assembly kerbalismAssembly)
        {
            if (!BridgeRuntime.Settings.CompleteOnTransmissionStart)
                return;

            if (!membersResolved)
                ResolveMembers(kerbalismAssembly);

            if (resolutionFailed)
                return;

            var stillActive = new HashSet<string>(StringComparer.Ordinal);

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel == null)
                    continue;

                try
                {
                    ScanVessel(vessel, stillActive);
                }
                catch (Exception ex)
                {
                    Log.DebugMessage("Transmission scan skipped vessel " +
                        vessel.id + ": " + ex.Message);
                }
            }

            ActiveKeys.Clear();
            foreach (string key in stillActive)
                ActiveKeys.Add(key);
        }

        /// <summary>
        /// Resolves KERBALISM.Drive.GetDrives() once. Both overloads take an
        /// explicit (Vessel, bool)/(ProtoVessel, bool) signature, so
        /// GetMethod(name, Type[]) finds the exact overload directly -- no
        /// ambiguity between them despite both being named "GetDrives".
        /// </summary>
        private static void ResolveMembers(Assembly kerbalismAssembly)
        {
            membersResolved = true;

            Type driveType = kerbalismAssembly.GetType("KERBALISM.Drive", false);
            if (driveType == null)
            {
                resolutionFailed = true;
                Log.Warning(
                    "KERBALISM.Drive type not found; transmission detection disabled.");
                return;
            }

            getDrivesByVesselMethod = driveType.GetMethod(
                "GetDrives", new[] { typeof(Vessel), typeof(bool) });
            getDrivesByProtoVesselMethod = driveType.GetMethod(
                "GetDrives", new[] { typeof(ProtoVessel), typeof(bool) });

            if (getDrivesByVesselMethod == null)
            {
                resolutionFailed = true;
                Log.Warning(
                    "KERBALISM.Drive.GetDrives(Vessel, bool) not found; " +
                    "transmission detection disabled.");
            }
        }

        private static void ScanVessel(Vessel vessel, HashSet<string> stillActive)
        {
            IEnumerable drives;

            if (vessel.loaded)
            {
                drives = (IEnumerable)getDrivesByVesselMethod.Invoke(
                    null, new object[] { vessel, true });
            }
            else if (getDrivesByProtoVesselMethod != null &&
                     vessel.protoVessel != null)
            {
                // Same lookup, but for a vessel that is not currently loaded.
                // Kerbalism keeps processing experiments and transmission in
                // the background, so evidence should not be limited to only
                // the active/loaded vessel.
                drives = (IEnumerable)getDrivesByProtoVesselMethod.Invoke(
                    null, new object[] { vessel.protoVessel, true });
            }
            else
            {
                return;
            }

            if (drives == null)
                return;

            foreach (object drive in drives)
                ScanDrive(vessel, drive, stillActive);
        }

        private static void ScanDrive(
            Vessel vessel, object drive, HashSet<string> stillActive)
        {
            object files = ReflectionUtil.Get(drive, "files");
            IEnumerable enumerable = ReflectionUtil.AsEnumerable(files);
            if (enumerable == null)
                return;

            foreach (object entry in enumerable)
            {
                object file = UnwrapValue(entry);
                if (file == null || file.GetType().FullName != "KERBALISM.File")
                    continue;

                double transmitRate =
                    ReflectionUtil.Get<double>(file, "transmitRate", 0.0);

                if (transmitRate <= 0.0)
                    continue;

                object subjectData = ReflectionUtil.Get(file, "subjectData");
                string subjectId;
                string experimentId;

                if (!KerbalismSubjectReader.TryRead(
                    subjectData, out subjectId, out experimentId))
                    continue;

                string key = vessel.id + "|" + subjectId;
                stillActive.Add(key);

                if (ActiveKeys.Contains(key))
                    continue;

                EvidenceStore.Add(new ScienceEvidence
                {
                    VesselId = vessel.id,
                    StockSubjectId = subjectId,
                    ExperimentId = experimentId,
                    Kind = ScienceEvidence.EvidenceKind.TransmissionStarted,
                    UniversalTime = Planetarium.GetUniversalTime()
                });
            }
        }

        private static object UnwrapValue(object entry)
        {
            if (entry == null)
                return null;

            if (entry is DictionaryEntry)
                return ((DictionaryEntry)entry).Value;

            PropertyInfo valueProperty = entry.GetType().GetProperty(
                "Value", ReflectionUtil.InstanceFlags);

            return valueProperty != null
                ? valueProperty.GetValue(entry, null)
                : entry;
        }
    }
}
