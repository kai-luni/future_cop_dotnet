using System.Runtime.CompilerServices;

// The tests drive the loader seam of GlEs2Api (missing library, missing export) without touching a
// real GL implementation, so they must see its internal factory.
[assembly: InternalsVisibleTo("FutureCop.Gpu.Tests")]
