# Design notes

## Why Harmony is used

Contract Configurator does not expose a public hook for adding external evidence
to every existing `CollectScienceCustom` parameter. Harmony permits a postfix
that preserves the original method and only supplements it.

## Why Kerbalism is reflection-only

A hard assembly reference would make the bridge fail to load if Kerbalism is
missing or renamed. Reflection allows clean failure and makes version checks
explicit. It also prevents distribution of Kerbalism binaries.

## Why evidence uses full stock subject IDs

Contract Configurator's own `CheckSubject` already interprets its target body,
biome, situation, location and experiment constraints from a `ScienceSubject`.
Using Kerbalism's `StockSubjectId` lets the original matcher remain authoritative.

## Why the experiment patch requires SubjectData

The requested rule says "when the experiment starts", but Kerbalism can place an
experiment in a running-like state while blocked, waiting or lacking a current
subject. Requiring a concrete subject prevents a generic switch-on from
satisfying the wrong biome or situation.

## Known limitation: transmission on unloaded vessels

Kerbalism transmits unloaded-vessel data. The included scanner deliberately
supports loaded HardDrive objects first. Unloaded support should be added only
after verifying the public/semipublic vessel-data access path in the exact 3.32
binary. Guessing proto-node layouts would be fragile and could produce false
matches.

## Known limitation: dynamic contract generation

See `GENERATION_PATCH.md`. Runtime completion support cannot create a contract
that Contract Configurator rejected during expression evaluation.

## Fixed: R&D subject lookup returned null for almost all real evidence

The first reviewed version looked up `evidence.StockSubjectId` only through
`ResearchAndDevelopment.GetSubjectByID()`. Kerbalism does not register a
`ScienceSubject` in the persistent stock R&D database until science is
actually credited to it -- which for a "complete on start" rule happens
after the objective should already be satisfied, or not at all for a long
background experiment. In practice this meant `ContractScienceMatcher`
almost always found `subject == null` and silently skipped matching.

`ContractScienceMatcher.ResolveSubject()` now falls back to constructing a
transient `ScienceSubject` directly from the stock subject id string (plus
`dataScale`/`scienceCap` from the stock `ScienceExperiment` definition when
one exists). This object is never registered with R&D -- it is only passed
into Contract Configurator's own `CheckSubject()` for the duration of one
method call -- so it cannot affect science totals or be persisted. This is
the same construction pattern Kerbalism's own legacy science code has used
for subjects it manages outside stock R&D.

This fix should be verified in-game (see `REVIEW_CHECKLIST.md`): confirm
`CheckSubject()` only inspects the subject id/body/situation/biome and does
not require `subject.science`/`scienceCap` to already reflect real R&D data.

## Fixed: `ignoreRecoveryMethod` had no effect

The setting was read from `Settings.cfg` but nothing in
`ContractScienceMatcher` ever consulted it -- start/transmission evidence
was unconditionally treated as sufficient regardless of the value. The
matcher now checks `BridgeRuntime.Settings.IgnoreRecoveryMethod` first and,
when it is `false`, returns immediately without touching `recoveryDone`,
deferring entirely to Contract Configurator's stock recovery/transmission
logic. `ignoreRecoveryMethod = true` remains the default, matching the
originally requested gameplay rule.
