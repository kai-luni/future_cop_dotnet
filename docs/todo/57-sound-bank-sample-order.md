# Todo 57: Test sound-bank sample order against retail

## Problem

Managed sound tests cover decoding, mixing and playback, but do not directly pin the per-bank sample cursor order used for playback.

## Evidence

- PS1 `e057e56`: `tests/host/leaf_boot_r3_harness.c:534` tests `gPlayNextBankSample` (`8004772C`), including rejection of an empty bank, cursor advance and wrap before selection. The C function passed all 40 harness checks under QEMU.
- .NET `fd5abf3`: `src/FutureCop.Audio/SoundMixer.cs:105-111,303-309` rejects empty banks and selects samples through the mixer cursor. The SPU submission and byte return in the C harness are not equivalent to the managed PCM mixer API.

## Task

Drive the managed one-shot playback path with distinguishable sample data and adapt the original empty-bank, advance and wrap cases. Observe which sample is played through the public mixer path rather than reimplementing the cursor in the test.

## Acceptance

The C harness and .NET tests agree on empty-bank rejection without cursor advance, advancing to the next sample and wrapping to index zero before selection. Report the SPU-only assertions separately; do not claim parity for them.
