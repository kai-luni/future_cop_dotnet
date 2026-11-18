# WVE demux golden vectors

`wve-demux.vectors` feeds both `WveDemuxTests` and the decomp repo's
`tests/host/wve_demux_test.sh`, which links unmodified `src/game/800879DC.c`.
The stream bytes, pad schedule, display mode, initial pad masks and injected
reservation fault are shared. Each row contains separate C and .NET expected
status/masks/ordered callback traces to document intentional differences.

The stub reserve clamps requests to remaining bytes and does not consume data;
`Advance` consumes. The first reserve is advisory. `pressLoop` is a zero-based
pair of pad polls, -1 means no press and -2 means held throughout. Pad bits and
masks are hexadecimal. Fault 1/2/3 makes initial/header/payload reservation
null; 4 makes the first payload reservation non-null with zero length.

Trace tokens: V/A/B are VLC/audio/split-audio dispatch at stream offsets, D is
video dispatch offset; S starts audio, E enables the clock, T is frame deadline
index, W pumps waiting, P presents, X advances. Stub deadline is 1 and clock
starts 0; the first wait advances it to 1. A callback pointer's offset refers
to the start of the synthetic stream. VLC data begins eight bytes into the
reservation, whereas audio/video receive the reservation including the header.

Expectations are derived from `800879DC.c`: the size limit is inclusive, an
unknown tag advances 4, a recognized tag advances the reserved length, and
count begins at zero (start occurs on video index 1). A new Cross/Start press
at count >=4 returns 0 without updating pad masks, setting abort only for Cross.
A null/zero reservation returns 1 in the actual code, not a distinct error.

C does not check for an eight-byte header or declared chunk completeness. The
harness allocates and zero-pads a large aligned backing array: it can safely
record the original function's branching on a short reservation without claiming
that real out-of-bounds reads are defined. The .NET loop instead returns
`Malformed` for partial headers or recognized chunks with size under 8 or a
partial reservation. It returns `Complete` for a null or zero-length reservation,
matching the C's return ambiguity; that does not imply a complete file. Unknown
chunks retain the C resynchronization behavior. These vectors verify the demux
and callback order, not validation of compressed contents, codecs, timing hardware,
real disc streaming, or actual PlayStation execution.
