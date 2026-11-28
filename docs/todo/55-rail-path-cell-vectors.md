# Todo 55: Port rail path-cell lookup vectors

## Problem

The managed rail path-cell lookup has no direct regression test for the original signed cell boundaries, first-match behavior and counted list.

## Evidence

- PS1 `e057e56`: `tests/host/leaf_final_harness.c:366-389` checks five `gFindRailPathCell` (`func_80050DAC`) cases.
- .NET `fd5abf3`: `src/FutureCop.Game/PathNetwork.cs:72-82` is the matching lookup; `RailPathMover` calls it when seating a mover. All five cases passed under QEMU and against the managed production method in a scratch probe.

## Task

Bring the five original cases into the managed test suite, retaining the original node order, coordinates and expected indices. Represent the C record count as the length of the managed node array; do not add a test-only lookup.

## Acceptance

The unchanged C harness and .NET tests agree on the first of two equal cells, the Z boundary, a backing record beyond the active count, flooring below a negative X boundary, and an empty list.
