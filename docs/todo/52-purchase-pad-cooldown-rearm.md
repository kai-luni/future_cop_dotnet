# The purchase pads gate on a ScriptVars cooldown that nothing re-arms

## Problem

Both base purchase pads require `ScriptVars[18] > 1` (pad 47) / `ScriptVars[20] > 1`
(pad 51) before they fire, and then set that word to exactly 1. Neither the mission's RPNS
pool nor its Cfun timer bytecode contains a write that ever raises those words above 1, so
after the first attempt the pad is permanently locked — in retail too, unless something
outside `CONFT.MIS` arms it. The port executes the same bytecode verbatim, so the behaviour
is inherited but never analysed.

## Evidence

- RPNS `0x63F`: `push 18; push var[a]; push 1; >` followed by `push 1; push 18; var[a]=b`.
- Scan of the CONFT RPNS pool (file 0x68, 0x710 bytes) for `push N` plus var-op pairs: no
  `push 18` / `push 20` with `var++`, `var--` or `var=`.
- Scan of the Cfun `tEXT` blob (file 0xA862C, 1968 bytes): the only ScriptVars writes are
  for indices 0–15, 17, 22, 23, 24, 26, 27, 28 — none for 18 or 20.
- Port executes the same script verbatim: `PurchasePadSystem.cs:930` `FireMapZone`,
  `:938-940` (`Script0Offset == 0` → skip) and the shared `RunPadScript`.
- Unproven: where retail gets the `> 1` value from (an engine write, another mission file,
  or the pads being intentionally one-shot). Resolve this before changing behaviour.

## Task

Find the writer of `ScriptVars[18]` / `[20]` in the decompilation, or prove that none
exists, and document it with `file.c:line`. Then align the port: either arm the cooldown the
way retail does, or drop the gate with the C reference that justifies it.

## Acceptance

- Two purchases from Cact 14/47 (and 14/51) in one session each spawn a unit; or, if retail
  is proven one-shot, a test asserts the second attempt yields `PurchaseResult(false, …)`
  and the cited C source of that decision is recorded here.
- Manual: walking onto a pad always reacts (spawn or an explained refusal), never silently
  nothing.
