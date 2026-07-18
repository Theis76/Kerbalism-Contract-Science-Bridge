# Dynamic `AllScienceSubjects*()` generation problem

## Why completion and generation are separate

A `CollectScience` parameter may exist only after a contract pack has generated a valid `ScienceSubject`.

Field Research commonly calls expression functions such as:

```cfg
AllScienceSubjectsByBodyExperiment([@targetBody], @experiments)
```

Contract Configurator evaluates these against stock science subjects. Kerbalism 3.32 maintains a richer subject catalogue and may not expose every candidate in the form expected by those functions. As a result, a contract can be rejected before any `CollectScience` parameter exists.

The runtime bridge cannot repair a contract that was never instantiated.

## Recommended upstream/source fix

The robust solution belongs in Contract Configurator's science expression provider:

1. Detect whether Kerbalism Science is active.
2. Query Kerbalism `ScienceDB.ExperimentInfos`.
3. Enumerate Kerbalism subjects for each requested experiment/body.
4. Return stock-compatible `ScienceSubject` objects using each
   `SubjectData.StockSubjectId`.
5. Do not add synthetic subjects to the persistent R&D database.
6. Exclude Kerbalism virtual-biome subjects unless the expression API is
   extended to represent those conditions correctly.
7. Preserve the existing stock implementation as fallback.

## Why this repository does not fake the list

Injecting fabricated subjects globally can:

- alter R&D science totals;
- make `CollectedScience()` report incorrect values;
- generate impossible contracts for virtual biomes;
- create IDs that Kerbalism will never produce;
- conflict with other science overhaul mods.

A reviewer can use this bridge's `KerbalismSubjectReader` and reflection style as the basis for an upstream Contract Configurator pull request, but the expression implementation should be compiled and tested in Contract Configurator itself.

## Field Research practical workaround

Until generation is fixed upstream, Field Research contract nodes that fail only their candidate-count requirement can be patched individually to use a curated experiment/body list. That is a configuration workaround, not a universal solution.
