# Army type handler

## Problem

Purchased units spawn, draw, hit and score — but the general type-handler switch for mobile and fighting units is not connected. They do not move and do not attack.

## Evidence

- Missing according to `docs/pa-playable-roadmap.md:142-168`.
- Retail: `8002AE54.c` (type dispatch), `D_8003015C` as an example of a type handler.

## Task

Wire the type switch from `8002AE54.c` and implement the handlers of the PA types, at least 5, 9 and 28/0x1C.

## Acceptance

- A purchased unit walks its route and fires.
- Existing spawn, hit and scoring tests stay green.
