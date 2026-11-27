# Todo 43: Remaining retail environment vectors

## Problem

The managed environment suite uses shared PS1 inputs, but does not exercise all operations in the C harness directly. A passing model projection or bone composition is not a standalone test of those operations.

## Beleg

At PS1 commit `0a82cd1`, `tests/host/env_harness.c:316-348,349-367` checks `TRANSPOSE` (`80019470`), `MULMV` (`8001A81C`), `VIEW` (`80018860`), `LOADROT` (`80010820`), `XF6C` (`8001006C`) and `XF1D4` (`800101D4`). Compare with `tests/FutureCop.Assets.Tests/EnvironmentVectorTests.cs`. The similarly named `XF10C` and `XF234` exercise different output formats or translation modes.

## Aufgabe

Trace each operation to its managed gameplay or rendering caller, or identify the missing port. Reuse the corresponding rows from `tests/host/vectors/env.vectors` for that path; do not add an implementation used only by tests. Keep the two rotation conventions separate.

## Akzeptanz

For each of the six tags, document the managed caller and run its shared vectors on both implementations, or identify the concrete missing caller that blocks the test. Check untouched outputs or register state where the managed API exposes them; distinguish per-vertex from batch coverage.
