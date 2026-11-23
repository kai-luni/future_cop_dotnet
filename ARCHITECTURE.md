# Project structure

The port keeps PS1 game logic and file decoding independent of platform-specific frontends.
The current projects are:

- `FutureCop.Assets`: retail file formats, models, textures and movie decoding.
- `FutureCop.Audio`: PlayStation ADPCM and menu music decoding; audio output remains platform-specific.
- `FutureCop.Render`: GTE math, model projection and scripted model rendering; depends on Assets.
- `FutureCop.Game`: player assembly, game and menu state, weapons and projectiles; depends on Assets and Render.
- `FutureCop.View`: the shared viewer scene and software rasterizer; depends on Assets, Render and Game.
- `tools/`: command-line and platform frontends. They compose the libraries and handle input and output.
- `tests/FutureCop.Assets.Tests`: tests for the libraries and viewer. Shared C/.NET fixtures are in `tests/golden`.

The existing `FutureCop.Assets` namespace is retained for moved types so callers do not need source
changes; their assembly is determined by the project containing them. This is source compatibility,
not binary compatibility with a previously compiled assembly. References between projects point from
frontends toward the libraries; libraries must not depend on the frontends or the viewer.

`WireframeScene` still combines preview simulation and rasterization. If either grows substantially,
split it behind its existing interface, with behavior tests before extracting timing or simulation.
The renderer is a software implementation of the current PS1 path, not a complete 3D engine.

Run builds and tests in an isolated copy. When retail assets are available, set `FC_REQUIRE_RETAIL=1`
and `FC_RETAIL_MIS` to the extracted `CW/M1A1.MIS` path; set `FUTURECOP_MOVIES_DIR` to the extracted
`MOVIES` directory for movie tests. C parity fixtures must be checked with the corresponding host
harnesses in the C repository as well as the .NET tests.
