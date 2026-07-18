# Validation against supplied installation

## How this was actually verified (0.3.1)

The 0.3.0 version of this document claimed several Contract Configurator
and Kerbalism members were "confirmed" while simultaneously stating this
environment had no .NET compiler. Those two statements are in tension: name
matching against strings in a binary cannot establish an exact method
overload (argument count/types), only a real reflection load or IL
disassembly can. That version's claims should have been treated as
unverified guesses, not confirmations, despite the wording used.

This revision replaces those claims with an actual result: the user
uploaded the real `Assembly-CSharp.dll`, `UnityEngine*.dll`,
`ContractConfigurator.dll`, `0Harmony.dll` and `Kerbalism112.kbin` from
their own KSP installation. A small reflection tool
(`Assembly.LoadFrom` + `Type.GetType`/`GetMembers`, run under Mono 6.8 via
`mono`/`mcs` installed from Ubuntu's package archive) loaded each assembly
and enumerated the exact members this bridge depends on. The plugin source
was then compiled directly against these real DLLs with `mcs`
(0 errors, 0 warnings, including with `-warnaserror+`), and the resulting
`KerbalismContractScienceBridge.dll` was reflection-loaded again to confirm
its `[KSPAddon]` and `[HarmonyPatch]` classes are intact.

Kerbalism112.kbin was confirmed to be a genuine PE32+ .NET assembly
(`file` reports "Mono/.Net assembly"), consistent with it being a
renamed/versioned build artifact loaded by KerbalismBootstrap.dll, as
0.3.0 described.

## Verified Kerbalism 3.32 members (real reflection dump)

- `KERBALISM.Experiment.State` — `RunningState State { get; set; }` (has a setter)
- `KERBALISM.Experiment.Subject` — `SubjectData Subject { get; }`
- `KERBALISM.SubjectData.StockSubjectId` — `String StockSubjectId { get; set; }`
- `KERBALISM.SubjectData.ExpInfo` — `ExperimentInfo ExpInfo { get; set; }`
- `KERBALISM.ExperimentInfo.ExperimentId` — `String ExperimentId { get; set; }`
- `KERBALISM.HardDrive.drive` — field, type `Drive`
- `KERBALISM.Drive.files` — field, type `Dictionary<string, File>`-shaped
  (`Dictionary`2`; enumerated as `KeyValuePair`, which
  `KerbalismTransmissionScanner.UnwrapValue()` already handles)
- `KERBALISM.File.subjectData` — field, type `SubjectData`
- `KERBALISM.File.transmitRate` — field, type `Double`

## Verified Contract Configurator 2.13.1.0 members (real reflection dump)

All on `ContractConfigurator.Parameters.CollectScienceCustom` or its base
`ContractConfigurator.Parameters.VesselParameter` (Contract Configurator's
own base class, not stock KSP's):

- `recoveryDone` — field, `Dictionary<string, bool>` (generic args confirmed exactly)
- `experiment` — property, `List<string>` (generic arg confirmed exactly)
- `CheckSubject(string exp, ScienceSubject subject)` — returns `bool`
- `UpdateDelegates()` — no arguments
- `CheckVessel(Vessel vessel, bool forceStateChange = false)` — **confirmed
  as the only overload**; there is no 1-argument `CheckVessel`. This means
  `ContractScienceMatcher.TryInvokeCheckVessel()`'s first attempt (the
  1-argument lookup) will correctly find nothing and fall through to the
  2-argument call, which is the one that actually exists.
- `CanCheckVesselMeetsCondition(Vessel vessel)` — returns `bool`, confirmed
  as written
- `GetParameterGroupHost()` — returns `VesselParameterGroup`, confirmed as written

Every reflection target this bridge uses -- across `ContractScienceMatcher`,
`KerbalismSubjectReader`, `KerbalismExperimentPatch` and
`KerbalismTransmissionScanner` -- now has a confirmed match against the
user's actual installed files. No guessed member name or signature in the
current source is contradicted by this dump.

## Verified stock (Assembly-CSharp) members backing `ResolveSubject()`'s fallback

- `ScienceSubject(string id, string title, float dataScale, float subjectValue, float scienceCap)`
  — confirmed as an exact, existing overload with this parameter order
- `ScienceExperiment.dataScale`, `.scienceCap`, `.experimentTitle` — all
  confirmed as public fields with these exact names
- `ResearchAndDevelopment.GetSubjectByID(string)` and
  `ResearchAndDevelopment.GetExperiment(string)` — confirmed static methods

## What is still not verified

Reflection can confirm a member exists with a given signature. It cannot
confirm runtime *behaviour* -- for example, whether `CheckSubject()`'s
internal logic reads only id/body/situation/biome (as assumed) or also
consults `science`/`scienceCap` in a way that would behave differently for
the transient, non-R&D `ScienceSubject` built by `ResolveSubject()`. That
still needs an in-game test, per `REVIEW_CHECKLIST.md`.

The compiled DLL has been reflection-loaded successfully but has **not**
been run inside a live KSP process in this environment -- there is no
Unity runtime here to actually instantiate `BridgeRuntime` as a
`MonoBehaviour`, load a save, or accept a contract. That remains the one
verification step that requires your machine.

