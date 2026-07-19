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
