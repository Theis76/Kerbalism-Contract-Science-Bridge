# Kerbalism Contract Science Bridge

Version 0.5.3 — a compatibility plugin for:

- Kerbalism 3.32
- Contract Configurator 2.13.1.0
- Kerbal Space Program 1.12.x
- Breaking Ground / Serenity's stock scanning-arm ("ROC") contracts

## Purpose

This plugin bridges Kerbalism's science model to two independent, unrelated contract systems that both assume the stock KSP science pipeline:

1. **Contract Configurator's `CollectScience` parameter** (`ContractScienceMatcher.cs` / `CollectSciencePatch.cs`), built around:
   - `ScienceData`
   - `ScienceSubject`
   - `GameEvents.OnExperimentDeployed`
   - `GameEvents.OnScienceRecieved`
2. **The stock Breaking Ground / Serenity scanning-arm contract**, `Expansions.Serenity.Contracts.CollectROCScienceArm` (`RocScienceArmBridge.cs`) -- a base-game class, entirely unrelated to Contract Configurator, that completes itself via a private `OnScience(...)` handler for `GameEvents.OnScienceRecieved`.

Kerbalism replaces most of the stock runtime pipeline both of these rely on with its own experiment, file, sample and transmission systems, and (per Kerbalism/Kerbalism#588) only fires the stock `OnScienceRecieved` event once an experiment reaches roughly 95% completion internally. This plugin supplements both systems so they can complete when:

1. the matching Kerbalism experiment is actually started for the requested body/situation/biome; or
2. transmission of a matching Kerbalism file actually begins.

No per-contract-pack configuration rewrite is required for completion tracking. Existing `type = CollectScience` nodes remain unchanged, and the stock ROC contract needs no `.cfg` at all -- it is patched at the class level.

**Not addressed:** `Expansions.Serenity.Contracts.CollectROCScienceRetrieval` (the "bring the rock home" variant, as opposed to the scanning-arm/transmit variant) has a separate, structural incompatibility tracked as Kerbalism/Kerbalism#476 -- it looks for stock `ScienceData` on a `ProtoPartModuleSnapshot` that Kerbalism never populates. That is a different failure mode and out of scope here.

## Important scope distinction

There are two independent compatibility problems:

1. **Completion tracking** — handled by this plugin.
2. **Dynamic contract generation through `AllScienceSubjects*()`** — not safely replaceable from an external plugin without patching Contract Configurator's expression functions or publishing a Contract Configurator change.

This package deliberately solves the first problem globally and includes an experimental generation hook scaffold, but does not silently inject synthetic science subjects into KSP's R&D database. Doing that incorrectly can corrupt science values or generate impossible contracts, especially with Kerbalism virtual biomes.

Field Research uses both mechanisms. Consequently:

- already generated or otherwise valid `CollectScience` parameters can be completed by this bridge;
- contracts rejected during generation because `AllScienceSubjects*()` returns no candidates still need the optional Contract Configurator source patch documented in `docs/GENERATION_PATCH.md`.

## Completion semantics

By default, a matching Contract Configurator science objective is completed when either:

- Kerbalism changes the experiment to `Running` or `Forced` and exposes a concrete `SubjectData`; or
- a matching Kerbalism science file has a positive `transmitRate`.

The bridge intentionally ignores Contract Configurator's original `recoveryMethod` once a Kerbalism start/transmission match is observed. This matches the requested gameplay rule: beginning the experiment or beginning transmission is sufficient.

## Safety properties

- It never awards science.
- It never creates or deletes Kerbalism files.
- It never changes experiment state.
- It never changes contract configuration nodes.
- For Contract Configurator: it only adds completion evidence to an existing `CollectScienceCustom` parameter.
- For the stock ROC scanning-arm contract: it calls the real, private `CollectROCScienceArm.OnScience(...)` method directly -- the same method the game itself would call, just triggered by Kerbalism evidence instead of waiting for Kerbalism's internal ~95% event. It does not set contract state directly and does not bypass `OnScience`'s own logic.
- It uses Kerbalism's stock-compatible `SubjectData.StockSubjectId` whenever available.
- It fails closed: reflection/API mismatch logs a warning and leaves stock behavior unchanged.

## Installation after compiling

Copy the **`GameData/` folder** (and only that folder) from this package into your KSP installation's `GameData` directory, so you end up with `<KSP install>/GameData/KerbalismContractScienceBridge/...`.

Do **not** copy the `development/` folder into `GameData` -- it contains source code, docs, and a test contract (`development/test-data/GeologicalStudy.cfg`) that Contract Configurator will load as a real, live contract if it ends up anywhere under `GameData`. (Earlier package versions put `test-data/` next to `GameData/` inside a single wrapper folder with the same name, which made it easy to copy the wrong thing; this layout no longer has that ambiguity.) If you installed an earlier version this way, check for and delete any `GameData/KerbalismContractScienceBridge/test-data/` or `GameData/KerbalismContractScienceBridge/src/` folder in your actual KSP install.

Required runtime dependencies:

- Kerbalism
- Contract Configurator
- HarmonyKSP

## Building

1. Set the environment variable `KSP_ROOT` to the KSP installation directory.
2. Ensure the following files exist:
   - `$KSP_ROOT/KSP_x64_Data/Managed/Assembly-CSharp.dll`
   - `$KSP_ROOT/KSP_x64_Data/Managed/UnityEngine.dll`
   - `$KSP_ROOT/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll`
   - `$KSP_ROOT/KSP_x64_Data/Managed/UnityEngine.UI.dll`
   - `$KSP_ROOT/GameData/ContractConfigurator/ContractConfigurator.dll`
   - `$KSP_ROOT/GameData/000_Harmony/0Harmony.dll`
3. From `development/`, run:

```powershell
dotnet build .\src\KerbalismContractScienceBridge.csproj -c Release
```

The project intentionally does not reference `Kerbalism.dll` at compile time. Kerbalism interaction is reflection-based so a missing or changed Kerbalism assembly does not prevent KSP from loading the bridge. The stock `CollectROCScienceArm` bridge (`RocScienceArmBridge.cs`) *is* compiled directly against `Assembly-CSharp.dll` for its public members (it's base-game content, always present regardless of DLC ownership) but still uses reflection for the one member that's private (`OnScience`).

## Diagnostics

Set:

```cfg
KERBALISM_CONTRACT_SCIENCE_BRIDGE
{
    debugLogging = true
}
```

Logs are prefixed with:

```text
[KerbalismContractScienceBridge]
```

## Review status

0.3.1 was compiled directly against your real `Assembly-CSharp.dll`, `UnityEngine*.dll`, `ContractConfigurator.dll`, `0Harmony.dll` and `Kerbalism112.kbin` (0 errors, 0 warnings, `-warnaserror+` clean), and every reflection member the source depends on was independently confirmed via a real reflection load of those files -- not string matching, not guessing. See `development/docs/VALIDATION_3_32.md` for the full, itemized results. 0.4.0's new `RocScienceArmBridge.cs` was verified the same way: `Contract.AllParameters`, `Contract.ContractState`/`ContractGuid`, `ContractParameter.State`/`Root`, `ContractSystem.Instance.Contracts`, and `CollectROCScienceArm`'s members (including confirming `OnScience` is private, requiring reflection to call) were all confirmed by a real reflection load before being used, not assumed. The compiled DLL is included under `GameData/KerbalismContractScienceBridge/Plugins/`.

**Confirmed working in-game as of 0.5.2/0.5.3:** a Field Research ROC/scanning-arm objective ("Scan Mun Large Crater") now completes when transmission starts, confirmed via the user's own `KSP.log` ("satisfied CollectScience experiment 'ROCScience_MunLargeCrater' from TransmissionStarted: ..."). This went through `ContractScienceMatcher`'s `MatchesOwnCriteria()` path, not `RocScienceArmBridge` -- no contract encountered so far has actually used the stock `CollectROCScienceArm` class, so that code path remains unconfirmed in a live game (see `development/docs/REVIEW_CHECKLIST.md`). Plain, non-ROC `CollectScience` objectives (ordinary samples/reports/biome studies) have not yet been re-tested against the 0.5.0 matching rewrite specifically, though nothing in that rewrite is expected to regress them.

Fix history: 0.1.1 addressed the R&D-lookup and `ignoreRecoveryMethod` issues from independent review; 0.2.0 added vessel-binding via `CheckVessel`; 0.2.1 made the two new reflection lookups from 0.2.0 best-effort instead of hard requirements, since their exact signatures were unverified at the time; 0.3.0 fixed the transmission scanner's `HardDrive`/`Drive` object graph and the Harmony reference path; 0.3.1 is the first version verified and compiled against your actual installed files; 0.3.2 fixed transmission detection for experiments using a private (non-HardDrive) drive, such as scanning arms, and added unloaded-vessel transmission support as a side effect of the same fix; 0.3.3 fixed a bug from 0.2.0 that discarded valid evidence for any ungrouped `CollectScience` parameter unless the source vessel happened to be the currently active/focused one -- diagnosed directly from the user's `KSP.log`, which showed evidence being recorded correctly and then immediately discarded on the next line; 0.4.0 added `RocScienceArmBridge.cs` for the stock Breaking Ground scanning-arm contract, which turned out to be an entirely different, unpatched system from Contract Configurator, and repackaged the zip so `development/` (source, docs, and the test contract) can no longer be mistaken for `GameData/` and copied into a live KSP install; 0.4.1-0.4.4 progressively promoted diagnostic logging to always-visible to trace one specific contract's evidence through the whole pipeline, eventually finding that `CheckSubject` returned `false` for an exact experiment-id-and-subject-id match; 0.5.0 found and fixed the actual root cause -- `CheckSubject` depends on a vessel-part scan that can never succeed once Kerbalism has replaced the stock science parts it's looking for -- by adding an independent, stock-API-based criteria match (`MatchesOwnCriteria()`) that does not depend on `CheckSubject` at all; 0.5.1 added a per-field breakdown to that new matcher's diagnostic logging; 0.5.2 fixed a real bug it uncovered -- the biome lookup passed degrees where `CBAttributeMapSO.GetAtt()` requires radians, confirmed via the user's own screenshots showing the correct biome and coordinates against the log's incorrect one; 0.5.3 confirmed the fix in-game and dialed the diagnostic logging (added across 0.4.1-0.5.1) back down to `debugLogging`-gated, keeping only the two rare, high-value success-event log lines always-visible.

## How matching actually works (as of 0.5.0)

For each experiment id a `CollectScience` parameter declares as acceptable, a match now requires the experiment id to correspond to the evidence AND *either*:

- Contract Configurator's own `CheckSubject(experimentId, subject)` returns true (kept as a first attempt, since it costs nothing to try and covers non-Kerbalism-managed vessels or a future Contract Configurator fix), **or**
- `MatchesOwnCriteria()` independently confirms the parameter's declared `targetBody`/`situation`/`location`/`biome` against the vessel's real, current state via stock KSP APIs (`ScienceUtil.GetExperimentSituation`, `CelestialBody.BiomeMap`) that Kerbalism does not touch.

See the 0.5.0 entry in `CHANGES.md` for why `CheckSubject` alone was found to be structurally incapable of succeeding while Kerbalism manages science.
