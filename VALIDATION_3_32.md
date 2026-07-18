# Independent review checklist

## Binary/API verification

All items below were confirmed on 0.3.1 via a real reflection load of the
user's own `Assembly-CSharp.dll`, `ContractConfigurator.dll`, `0Harmony.dll`
and `Kerbalism112.kbin` -- see `docs/VALIDATION_3_32.md` for the full dump.
They are kept here, checked off, so future changes can be diffed against a
known-good baseline instead of re-guessing from scratch.

- [x] Kerbalism assembly is located by the presence of the `KERBALISM.Experiment`
      type (not by assembly simple name, which is not assumed to be `Kerbalism`).
- [x] `KERBALISM.Experiment.State` is `RunningState State { get; set; }` -- has a setter.
- [x] `KERBALISM.Experiment.Subject` is `SubjectData Subject { get; }`.
- [x] `KERBALISM.SubjectData.StockSubjectId` exists as `String StockSubjectId { get; set; }`.
- [x] `KERBALISM.SubjectData.ExpInfo.ExperimentId` exists (`ExpInfo`: `ExperimentInfo { get; set; }`; `ExperimentId`: `String { get; set; }`).
- [x] `KERBALISM.File.transmitRate` exists as `Double transmitRate` (field).
- [x] `KERBALISM.HardDrive.drive` exists as a `Drive` field (not `files`/`Files`
      directly on HardDrive -- confirms the 0.3.0 fix); `KERBALISM.Drive.files`
      is `Dictionary<string, File>`-shaped and enumerates as `KeyValuePair`.
- [x] Contract Configurator's real type is
      `ContractConfigurator.Parameters.CollectScienceCustom`.
- [x] Its members are `experiment` (`List<string>`), `recoveryDone`
      (`Dictionary<string, bool>`), `CheckSubject(string, ScienceSubject)`,
      `UpdateDelegates()`, and `OnUpdate()` -- all confirmed present with
      these exact names and signatures.
- [x] `CheckVessel` has exactly one overload,
      `CheckVessel(Vessel vessel, bool forceStateChange = false)` -- there is
      no 1-argument overload. `CanCheckVesselMeetsCondition(Vessel)` and
      `GetParameterGroupHost()` are both confirmed present as written.
- [x] The stock `ScienceSubject(string, string, float, float, float)`
      constructor used by `ResolveSubject()`'s fallback is confirmed to
      exist with exactly that parameter order (id, title, dataScale,
      subjectValue, scienceCap). `ScienceExperiment.dataScale`,
      `.scienceCap` and `.experimentTitle` are confirmed public fields.

Not confirmable by reflection (behavioural, not structural -- still open):

- [ ] Confirm `CheckSubject(experimentId, ScienceSubject)` only reads the
      subject's id/body/situation/biome, not `science`/`scienceCap` values
      that a transient (non-R&D) subject would leave at defaults.
- [ ] Confirm `ResearchAndDevelopment.GetExperiment(experimentId)` returns a
      valid `ScienceExperiment` for the experiments used by the target
      contract pack, so the transient-subject fallback gets a real
      `dataScale`/`scienceCap` rather than the 1f/1f default.

## Behaviour tests

These require a running KSP/Unity process and cannot be done from source
or reflection alone. None of them are satisfied by the 0.3.1 build/reflection
verification above.

- [ ] A stock experiment still completes normally without Kerbalism evidence.
- [ ] Starting the right experiment in the right biome completes the objective.
- [ ] Starting it in a wrong biome does not complete the objective.
- [ ] Starting it in a wrong situation does not complete the objective.
- [ ] Starting a different experiment with a similar ID does not complete it.
- [ ] Merely toggling an experiment that has no resolved SubjectData does not
      complete it.
- [ ] A matching sample experiment completes when started.
- [ ] Beginning actual transmission completes a matching file objective.
- [ ] Marking a file for transmission while disconnected does not complete it.
- [ ] Evidence from one vessel does not survive a scene change.
- [ ] A completed Contract Configurator parameter remains complete after save/load.
- [ ] Multiple experiments in one `CollectScience` parameter are tracked
      independently.
- [ ] Evidence from vessel A cannot complete a VesselParameterGroup tracking vessel B.
- [ ] Evidence from an inactive vessel cannot later complete an ungrouped parameter after switching vessels.
- [ ] Evidence occurring before a Sequence reaches the parameter is not retained as premature `recoveryDone` state.
- [ ] Field Research five-subject contracts do not cross-complete between biomes.
- [ ] With `ignoreRecoveryMethod = false`, starting an experiment or
      transmission no longer completes the objective early; stock recovery/
      transmission requirements still apply.
- [ ] A subject with no prior R&D entry (fresh save, first time that
      experiment/situation/biome combination is ever run) still completes
      the objective on experiment start.

## Performance tests

- [ ] Transmission polling allocates acceptably with 50+ vessels.
- [ ] Loaded HardDrive scanning remains below a negligible frame-time budget.
- [ ] Debug logging is disabled by default.
