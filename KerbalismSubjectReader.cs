using System;
using System.Collections.Generic;
using System.Reflection;
using ContractConfigurator.Parameters;

namespace KerbalismContractScienceBridge
{
    /// <summary>
    /// Matches one Kerbalism stock subject ID against the constraints stored in
    /// Contract Configurator's CollectScienceCustom parameter.
    ///
    /// Contract Configurator 2.13.1.0 stores these as protected properties:
    /// - targetBody
    /// - biome
    /// - situation
    /// - location
    /// - experiment
    /// - recoveryMethod
    ///
    /// Matching reuses Contract Configurator's own private CheckSubject method
    /// whenever possible. This is substantially safer than independently
    /// duplicating its string rules for KSC biomes, situations and locations.
    ///
    /// REVIEW FIX (see CHANGES.md item 1): Kerbalism only registers a
    /// ScienceSubject in the persistent stock R&D database once science is
    /// actually credited, which can happen long after an experiment starts
    /// (or never, for a partially-completed run). Relying solely on
    /// ResearchAndDevelopment.GetSubjectByID() meant "complete on start"
    /// evidence was silently dropped for every subject R&D had not seen yet
    /// -- i.e. almost always, since evidence fires at experiment start. We
    /// now fall back to constructing a transient, non-persisted
    /// ScienceSubject purely to drive CheckSubject()'s comparison. This
    /// mirrors the pattern Kerbalism's own legacy science code has used
    /// (new ScienceSubject(id, title, dataScale, subjectValue, scienceCap)),
    /// and the object is never added to ResearchAndDevelopment's subject
    /// list, so it cannot affect R&D totals or persist across a reload.
    /// </summary>
    internal static class ContractScienceMatcher
    {
        internal static bool TryApplyEvidence(
            CollectScienceCustom parameter,
            ScienceEvidence evidence)
        {
            if (parameter == null || evidence == null)
                return false;

            // Starting-work evidence is only meaningful when the bridge is
            // configured to consider that sufficient on its own. When the
            // person running the game sets ignoreRecoveryMethod = false in
            // Settings.cfg, we defer entirely to Contract Configurator's
            // stock recovery/transmission logic and do not mark anything
            // complete here. (REVIEW FIX, see CHANGES.md item 2 -- this
            // setting previously had no effect on matching at all.)
            if (!BridgeRuntime.Settings.IgnoreRecoveryMethod)
            {
                Log.DebugMessage(
                    "ignoreRecoveryMethod is false; deferring to stock " +
                    "Contract Configurator recovery logic for " + evidence);
                return false;
            }

            Vessel evidenceVessel = FlightGlobals.Vessels.Find(
                vessel => vessel != null && vessel.id == evidence.VesselId);

            if (evidenceVessel == null)
            {
                Log.DebugMessage(
                    "Evidence vessel is no longer available: " + evidence);
                return false;
            }

            if (!IsEvidenceVesselEligible(parameter, evidenceVessel))
                return false;

            ScienceSubject subject = ResolveSubject(evidence.StockSubjectId);

            if (subject == null)
            {
                Log.DebugMessage(
                    "ContractScienceMatcher: could not resolve a subject for " +
                    evidence.StockSubjectId + "; evidence cannot be safely matched.");
                return false;
            }

            Type type = typeof(CollectScienceCustom);

            PropertyInfo experimentProperty =
                ReflectionUtil.FindProperty(type, "experiment");
            FieldInfo recoveryDoneField =
                ReflectionUtil.FindField(type, "recoveryDone");
            MethodInfo checkSubjectMethod =
                ReflectionUtil.FindMethod(type, "CheckSubject");
            MethodInfo updateDelegatesMethod =
                ReflectionUtil.FindMethod(type, "UpdateDelegates");

            // REVIEW FIX (see CHANGES.md, 0.2.1): CanCheckVesselMeetsCondition
            // and CheckVessel are not confirmed against the real Contract
            // Configurator 2.13.1.0 binary -- their names, and CheckVessel's
            // exact argument count, are best-effort guesses. Treating them as
            // hard requirements meant a wrong guess would silently disable
            // the entire bridge, including the parts (CheckSubject-based
            // matching) that do not depend on them at all. They are now
            // best-effort: used when found, skipped when not, so a naming
            // mismatch only loses the extra vessel-binding safety rather
            // than all completion tracking.
            MethodInfo canCheckMethod =
                ReflectionUtil.FindMethod(type, "CanCheckVesselMeetsCondition", 1);

            // CheckVessel is commonly overloaded (e.g. CheckVessel(Vessel) in
            // stock KSP's VesselParameter vs. a possible two-argument variant
            // in Contract Configurator's own subclass). Try the most likely
            // signatures explicitly rather than guessing a single one.
            MethodInfo checkVesselMethod1Arg =
                ReflectionUtil.FindMethod(type, "CheckVessel", 1);
            MethodInfo checkVesselMethod2Arg =
                ReflectionUtil.FindMethod(type, "CheckVessel", 2);

            if (experimentProperty == null ||
                recoveryDoneField == null ||
                checkSubjectMethod == null)
            {
                WarnAboutMissingMembersOnce();
                return false;
            }

            var experiments =
                experimentProperty.GetValue(parameter, null) as List<string>;
            var recoveryDone =
                recoveryDoneField.GetValue(parameter) as Dictionary<string, bool>;

            if (experiments == null || recoveryDone == null)
                return false;

            // Always visible, not gated behind debugLogging: this is the
            // ground truth needed to see whether a given piece of evidence
            // is even being compared against the right parameter, and if
            // so, why CheckSubject accepts or rejects it. Bounded in volume
            // by Contract Configurator's own updateFrequency throttle on
            // OnUpdate (0.25s by default) and by how much evidence is
            // pending at once (typically a handful).
            //
            // matchingSubjects is CollectScienceCustom's own precomputed
            // Dictionary<string, ScienceSubject> of subjects considered
            // valid for this parameter (built at contract generation time).
            // CheckSubject very likely keys off subject.id against this
            // dictionary, so if our Kerbalism-derived subject id string
            // doesn't exactly match a key here -- even if it "looks"
            // identical -- CheckSubject will silently return false. Dumping
            // the actual keys is the most direct way to see a formatting
            // difference (extra suffix, different casing, etc.) without
            // guessing further.
            FieldInfo matchingSubjectsField =
                ReflectionUtil.FindField(type, "matchingSubjects");
            var matchingSubjects = matchingSubjectsField?.GetValue(parameter)
                as Dictionary<string, ScienceSubject>;

            Log.DebugMessage(
                "ContractScienceMatcher: parameter wants experiments [" +
                string.Join(", ", experiments) + "]; comparing against " +
                "evidence subject '" + evidence.StockSubjectId +
                "' (experimentId='" + evidence.ExperimentId + "', " +
                evidence.Kind + "); matchingSubjects keys = [" +
                (matchingSubjects != null
                    ? string.Join(", ", matchingSubjects.Keys)
                    : "<field not found>") + "]");

            // Do not consume evidence while Contract Configurator says this
            // parameter cannot yet be evaluated (for example because it is a
            // later member of a Sequence). Otherwise recoveryDone would retain
            // the observation and could complete at an unrelated later time.
            //
            // Best-effort: if CanCheckVesselMeetsCondition could not be
            // resolved, or invoking it fails (e.g. because our guessed
            // 1-argument signature is wrong), we fall back to the 0.1.1
            // behaviour of proceeding without this extra gate rather than
            // refusing to match at all.
            if (canCheckMethod != null)
            {
                try
                {
                    bool canCheck = (bool)canCheckMethod.Invoke(
                        parameter, new object[] { evidenceVessel });

                    if (!canCheck)
                    {
                        Log.DebugMessage(
                            "CollectScience is not ready to evaluate vessel " +
                            evidenceVessel.id + "; evidence left unconsumed.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.DebugMessage(
                        "CanCheckVesselMeetsCondition call failed (" +
                        ex.Message + "); proceeding without the sequence " +
                        "readiness gate for this evidence.");
                }
            }

            bool changed = false;

            // REVIEW FIX (0.5.0): a full play session's log showed
            // CheckSubject returning false for every single comparison,
            // always -- including an exact experiment-id and subject-id
            // match. The reason: CheckSubject keys off Contract
            // Configurator's own `matchingSubjects` cache
            // (Dictionary<string, ScienceSubject>), which is populated by
            // scanning the vessel for *stock* ModuleScienceExperiment parts
            // (via GetVesselSubjects). Kerbalism removes those parts
            // entirely and replaces them with its own KERBALISM.Experiment
            // modules, so that scan finds nothing and matchingSubjects stays
            // permanently empty -- meaning CheckSubject can structurally
            // never return true while Kerbalism manages science, regardless
            // of timing or which vessel/subject is involved.
            //
            // MatchesOwnCriteria() bypasses that broken cache and compares
            // the parameter's own declared targetBody/situation/location/
            // biome criteria directly against the vessel's real, current
            // state using only stock KSP APIs (ScienceUtil,
            // CelestialBody.BiomeMap) that Kerbalism does not touch or
            // replace. CheckSubject is still tried first and still counts
            // as a match if it ever succeeds (e.g. a mixed install where
            // some stock science parts remain, or a future Contract
            // Configurator version fixes this) -- this is additive, not a
            // replacement of working behaviour.
            bool ownCriteriaMatch = MatchesOwnCriteria(
                parameter, type, evidenceVessel, evidence);

            Log.DebugMessage(
                "ContractScienceMatcher: own-criteria match (targetBody/" +
                "situation/location/biome vs. the live vessel, bypassing " +
                "CheckSubject's broken matchingSubjects cache) => " +
                ownCriteriaMatch);

            foreach (string experimentId in experiments)
            {
                // Check this first: no point calling CheckSubject or
                // evaluating criteria for a candidate experiment id that
                // isn't even the one this evidence is about.
                if (!string.IsNullOrEmpty(experimentId) &&
                    !string.IsNullOrEmpty(evidence.ExperimentId) &&
                    !string.Equals(experimentId, evidence.ExperimentId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                bool checkSubjectMatch;
                try
                {
                    checkSubjectMatch = (bool)checkSubjectMethod.Invoke(
                        parameter, new object[] { experimentId, subject });
                }
                catch (Exception ex)
                {
                    checkSubjectMatch = false;
                    Log.DebugMessage(
                        "ContractScienceMatcher: CheckSubject('" +
                        experimentId + "') threw: " + ex.Message);
                }

                bool match = checkSubjectMatch || ownCriteriaMatch;

                Log.DebugMessage(
                    "ContractScienceMatcher: experiment '" + experimentId +
                    "' vs subject '" + subject.id + "' => CheckSubject=" +
                    checkSubjectMatch + ", ownCriteria=" + ownCriteriaMatch +
                    ", match=" + match);

                if (!match)
                    continue;

                if (!recoveryDone.ContainsKey(experimentId))
                {
                    recoveryDone[experimentId] = true;
                    changed = true;

                    Log.Info(
                        "ContractScienceMatcher: satisfied CollectScience " +
                        "experiment '" + experimentId + "' from " + evidence);
                }
            }

            if (changed)
            {
                if (updateDelegatesMethod != null)
                    updateDelegatesMethod.Invoke(parameter, null);

                // Crucial: CollectScienceCustom is a VesselParameter. Updating
                // recoveryDone alone changes shared parameter data but does not
                // associate completion with the vessel that produced the
                // evidence. Invoke Contract Configurator's own CheckVessel so
                // VesselParameterGroup tracking, ignored vessel types,
                // ReadyToComplete(), sequence handling and global state updates
                // remain authoritative where possible.
                //
                // Best-effort: neither the exact overload nor its parameter
                // types are confirmed against the real 2.13.1.0 binary. We
                // try the 1-argument form first (matches stock KSP's
                // VesselParameter.CheckVessel(Vessel)), then the 2-argument
                // form some Contract Configurator versions may expose, and
                // fall back to the 0.1.1 behaviour (recoveryDone + delegates
                // only, no CheckVessel call) if neither invocation succeeds.
                // That fallback still leaves the parameter correctly
                // completed for the common non-grouped, single-vessel case;
                // it only loses the extra VesselParameterGroup bookkeeping
                // this release is trying to add.
                if (!TryInvokeCheckVessel(
                        checkVesselMethod1Arg, checkVesselMethod2Arg,
                        parameter, evidenceVessel))
                {
                    Log.DebugMessage(
                        "CheckVessel could not be invoked (unresolved or " +
                        "wrong signature); recoveryDone was still updated " +
                        "directly, matching 0.1.1 behaviour.");
                }
            }

            return changed;
        }

        /// <summary>
        /// Attempts CollectScienceCustom.CheckVessel(vessel) first, then
        /// CheckVessel(vessel, false), swallowing any invocation failure so a
        /// wrong guess about the signature degrades gracefully instead of
        /// throwing out of TryApplyEvidence.
        /// </summary>
        private static bool TryInvokeCheckVessel(
            MethodInfo oneArgMethod,
            MethodInfo twoArgMethod,
            CollectScienceCustom parameter,
            Vessel evidenceVessel)
        {
            if (oneArgMethod != null)
            {
                try
                {
                    oneArgMethod.Invoke(parameter, new object[] { evidenceVessel });
                    return true;
                }
                catch (Exception ex)
                {
                    Log.DebugMessage(
                        "CheckVessel(Vessel) invocation failed: " + ex.Message);
                }
            }

            if (twoArgMethod != null)
            {
                try
                {
                    twoArgMethod.Invoke(
                        parameter, new object[] { evidenceVessel, false });
                    return true;
                }
                catch (Exception ex)
                {
                    Log.DebugMessage(
                        "CheckVessel(Vessel, bool) invocation failed: " +
                        ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// Logs the "internals differ" warning at most once per session
        /// instead of on every CollectScienceCustom.OnUpdate call (which can
        /// run many times per second), so a genuine API mismatch produces a
        /// clear diagnostic rather than flooding KSP.log.
        /// </summary>
        private static bool hasWarnedAboutMissingMembers;

        private static void WarnAboutMissingMembersOnce()
        {
            if (hasWarnedAboutMissingMembers)
                return;

            hasWarnedAboutMissingMembers = true;
            Log.Warning(
                "Contract Configurator internals differ from 2.13.1.0; " +
                "CollectScience integration disabled for this parameter.");
        }

        /// <summary>
        /// Ensures that evidence is applied only to the vessel context that
        /// Contract Configurator itself would evaluate. Without this guard, an
        /// observation from vessel A could alter recoveryDone for a parameter
        /// currently tracking vessel B.
        /// </summary>
        private static bool IsEvidenceVesselEligible(
            CollectScienceCustom parameter, Vessel evidenceVessel)
        {
            MethodInfo groupMethod = ReflectionUtil.FindMethod(
                typeof(CollectScienceCustom), "GetParameterGroupHost");

            // GetParameterGroupHost() was confirmed present via reflection
            // against the real 2.13.1.0 binary (see docs/VALIDATION_3_32.md),
            // so this branch should not be reachable in practice. It is kept
            // as a defensive fallback in case a different Contract
            // Configurator build is in use. REVIEW FIX (0.3.3): no longer
            // restricted to the active vessel, for the same reason as the
            // confirmed-ungrouped branch below -- see that comment.
            if (groupMethod == null)
                return true;

            object group = groupMethod.Invoke(parameter, null);
            if (group != null)
            {
                object tracked = ReflectionUtil.Get(group, "TrackedVessel");
                Vessel trackedVessel = tracked as Vessel;

                // A group without a tracked vessel may still be establishing
                // its vessel. Let CheckVessel make that decision. Once a
                // tracked vessel exists, require exact identity.
                if (trackedVessel != null && trackedVessel.id != evidenceVessel.id)
                {
                    Log.DebugMessage(
                        "ContractScienceMatcher: ignoring evidence from vessel " +
                        evidenceVessel.id + " because the parameter group " +
                        "tracks vessel " + trackedVessel.id + ".");
                    return false;
                }

                return true;
            }

            // Confirmed ungrouped (Contract Configurator itself reports no
            // VesselParameterGroup host). Contract Configurator's own model
            // for an ungrouped CollectScience parameter is "any vessel that
            // performs the action satisfies it" -- there is no single
            // vessel this parameter is tied to, so there is nothing to
            // compare the evidence's vessel against.
            //
            // REVIEW FIX (0.3.3): this used to also require
            // evidenceVessel.isActiveVessel here, on the theory that this
            // mirrors how an ungrouped VesselParameter is normally checked.
            // In practice that made the bridge discard perfectly valid
            // evidence the moment the player switched focus away from the
            // vessel -- which, for anything that takes time to finish (a
            // transmission in progress, for example) is the common case,
            // not an edge case. Reported symptom: a scanning-arm experiment
            // never completed its "transmission started" objective, even
            // though the log showed the evidence being recorded correctly
            // every time -- it was recorded, then immediately discarded by
            // this check on the very next line. CheckVessel(evidenceVessel,
            // ...) below is Contract Configurator's own method and remains
            // the actual authority on whether this vessel satisfies the
            // parameter (situation, biome, experiment id, etc. via
            // CheckSubject); it does not need our own extra, incorrect
            // active-vessel gate on top of it.
            return true;
        }

        /// <summary>
        /// Compares the parameter's own declared targetBody/situation/
        /// location/biome criteria directly against the real, current state
        /// of the vessel that produced the evidence -- entirely independent
        /// of Contract Configurator's CheckSubject/matchingSubjects, which
        /// cannot see Kerbalism-managed vessels (see the review-fix comment
        /// at the call site for why).
        ///
        /// Every field here is optional/nullable on the real parameter (a
        /// contract may only constrain some of them), so an unset field is
        /// treated as "matches anything" -- the same semantics Contract
        /// Configurator's own CheckSubject is documented to use.
        /// </summary>
        private static bool MatchesOwnCriteria(
            CollectScienceCustom parameter,
            Type type,
            Vessel vessel,
            ScienceEvidence evidence)
        {
            if (vessel == null || vessel.mainBody == null)
            {
                Log.DebugMessage(
                    "ContractScienceMatcher: MatchesOwnCriteria => false " +
                    "(vessel or vessel.mainBody is null)");
                return false;
            }

            FieldInfo targetBodyField = ReflectionUtil.FindField(type, "targetBody");
            var targetBody = targetBodyField?.GetValue(parameter) as CelestialBody;

            bool targetBodyOk = targetBody == null || targetBody == vessel.mainBody;

            ExperimentSituations actualSituation =
                ScienceUtil.GetExperimentSituation(vessel);

            PropertyInfo situationProperty = ReflectionUtil.FindProperty(type, "situation");
            object situationValue = situationProperty?.GetValue(parameter, null);
            ExperimentSituations? wantedSituation =
                situationValue as ExperimentSituations?;

            bool situationOk = !wantedSituation.HasValue ||
                wantedSituation.Value == actualSituation;

            PropertyInfo locationProperty = ReflectionUtil.FindProperty(type, "location");
            object locationValue = locationProperty?.GetValue(parameter, null);
            Contracts.BodyLocation? wantedLocation =
                locationValue as Contracts.BodyLocation?;

            bool isSurface =
                actualSituation == ExperimentSituations.SrfLanded ||
                actualSituation == ExperimentSituations.SrfSplashed;
            Contracts.BodyLocation actualLocation = isSurface
                ? Contracts.BodyLocation.Surface
                : Contracts.BodyLocation.Space;

            bool locationOk = !wantedLocation.HasValue ||
                wantedLocation.Value == actualLocation;

            PropertyInfo biomeProperty = ReflectionUtil.FindProperty(type, "biome");
            string wantedBiome = biomeProperty?.GetValue(parameter, null) as string;
            string actualBiome = GetVesselBiomeName(vessel);

            bool biomeOk = string.IsNullOrEmpty(wantedBiome) ||
                string.Equals(wantedBiome, actualBiome, StringComparison.OrdinalIgnoreCase);

            bool result = targetBodyOk && situationOk && locationOk && biomeOk;

            // Always visible: this is the exact, field-by-field breakdown
            // needed to see which single criterion (if any) is rejecting an
            // otherwise-correct-looking match, instead of only knowing the
            // aggregate true/false result.
            Log.DebugMessage(
                "ContractScienceMatcher: MatchesOwnCriteria detail -- " +
                "targetBody: wanted='" + (targetBody?.name ?? "<any>") +
                "' actual='" + vessel.mainBody.name + "' ok=" + targetBodyOk +
                " | situation: wanted='" +
                (wantedSituation.HasValue ? wantedSituation.Value.ToString() : "<any>") +
                "' actual='" + actualSituation + "' ok=" + situationOk +
                " | location: wanted='" +
                (wantedLocation.HasValue ? wantedLocation.Value.ToString() : "<any>") +
                "' actual='" + actualLocation + "' ok=" + locationOk +
                " | biome: wanted='" + (wantedBiome ?? "<any>") +
                "' actual='" + (actualBiome ?? "<null>") + "' ok=" + biomeOk +
                " || overall=" + result);

            return result;
        }

        /// <summary>
        /// Stock KSP biome lookup by lat/lon -- unaffected by Kerbalism,
        /// which does not touch CelestialBody.BiomeMap.
        ///
        /// REVIEW FIX (0.5.2): CBAttributeMapSO.GetAtt(lat, lon) expects
        /// RADIANS, while Vessel.latitude/.longitude are in DEGREES. The
        /// previous version passed degrees directly, which silently looks
        /// up an unrelated point on the map instead of throwing -- there is
        /// no type system protection against this, since both are plain
        /// doubles. Confirmed against real-world usage in an established,
        /// widely-used KSP science mod (DMagic Orbital Science's
        /// DMModuleScienceAnimate.cs), which does exactly this conversion:
        /// vessel.mainBody.BiomeMap.GetAtt(vessel.latitude * Mathf.Deg2Rad,
        /// vessel.longitude * Mathf.Deg2Rad). Confirmed as the actual bug
        /// via a real screenshot/log comparison: the game (and MechJeb)
        /// reported "The Mun's Highlands" at coordinates 2°14'11"S,
        /// 91°24'31"E, while the unconverted lookup returned "Poles" for
        /// that same position.
        /// </summary>
        private static string GetVesselBiomeName(Vessel vessel)
        {
            if (vessel?.mainBody?.BiomeMap == null)
                return null;

            const double DegToRad = Math.PI / 180.0;

            var attribute = vessel.mainBody.BiomeMap.GetAtt(
                vessel.latitude * DegToRad, vessel.longitude * DegToRad);

            return attribute?.name;
        }

        /// <summary>
        /// Resolves a ScienceSubject for CheckSubject() to compare against.
        ///
        /// Tries the persistent R&D database first, so an already-registered
        /// subject (e.g. one credited earlier in the same save) keeps using
        /// its real, authoritative data. Only when R&D has never seen the
        /// subject do we build a transient stand-in from the id string plus
        /// whatever the stock experiment definition can tell us.
        ///
        /// The transient object is deliberately never passed to
        /// ResearchAndDevelopment.AddSubjectToUnlocked() or any other R&D
        /// mutator: it exists only for the duration of this method call, so
        /// it cannot change science totals, unlock states or save data.
        /// </summary>
        internal static ScienceSubject ResolveSubject(string stockSubjectId)
        {
            if (string.IsNullOrEmpty(stockSubjectId))
                return null;

            ScienceSubject subject =
                ResearchAndDevelopment.GetSubjectByID(stockSubjectId);

            if (subject != null)
                return subject;

            // Stock subject IDs are "<experimentId>@<situationKey>". We only
            // need the experiment id to look up dataScale/scienceCap; the
            // full id is passed through unchanged so CheckSubject's own
            // body/situation/biome parsing still works exactly as it does
            // for a real R&D subject.
            int atIndex = stockSubjectId.IndexOf('@');
            string experimentId = atIndex > 0
                ? stockSubjectId.Substring(0, atIndex)
                : stockSubjectId;

            ScienceExperiment experimentDef =
                ResearchAndDevelopment.GetExperiment(experimentId);

            // Conservative fallbacks if even the experiment definition is
            // missing (e.g. a Kerbalism-only experiment with no stock
            // ScienceExperiment counterpart). These numbers are never
            // shown to the player and never persisted -- CheckSubject is
            // expected to key off the subject id string, not these values.
            float dataScale = experimentDef != null ? experimentDef.dataScale : 1f;
            float scienceCap = experimentDef != null ? experimentDef.scienceCap : 1f;
            string title = experimentDef != null
                ? experimentDef.experimentTitle
                : experimentId;

            try
            {
                subject = new ScienceSubject(
                    stockSubjectId, title, dataScale, /*subjectValue*/ 1f, scienceCap);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "Failed to build a transient ScienceSubject for " +
                    stockSubjectId + ": " + ex.Message);
                return null;
            }

            Log.DebugMessage(
                "Built transient (non-persisted) ScienceSubject for " +
                stockSubjectId + " because R&D has not registered it yet.");

            return subject;
        }
    }
}
