using FutureCop.Assets;
using FutureCop.Gpu;

namespace FcGpuProbe;

/// <summary>
/// Host-side probe for <see cref="GlEs2Renderer"/>: creates a real EGL/GLES2 context without a
/// display server, compiles the production shaders, renders with the production renderer and
/// compares the result against the software reference.
/// </summary>
internal static class Program
{
    private const int Width = 320;
    private const int Height = 240;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        int? reExecExit = MaybeReExec(args);
        if (reExecExit is int exit) return exit;

        string mode = args[0];
        string outDir = Option(args, "--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "out");

        try
        {
            switch (mode)
            {
                case "shaders":
                {
                    using HeadlessGlContext ctx = HeadlessGlContext.Create(Width, Height);
                    PrintContext(ctx);
                    PrintShaderLogs(ctx.Gl);
                    byte[] rgb = RenderShaders(ctx);
                    string path = Path.Combine(outDir, "shaders.ppm");
                    Ppm.Write(path, Width, Height, rgb);
                    Console.WriteLine($"wrote {path}");
                    return 0;
                }

                case "ps1":
                {
                    using HeadlessGlContext ctx = HeadlessGlContext.Create(Width, Height);
                    PrintContext(ctx);
                    float offset = ParseOffset(Option(args, "--offset"));
                    byte[] rgb = RenderScene(ctx, offset);
                    string path = Path.Combine(outDir, offset == 0f ? "ps1.ppm" : "ps1-offset.ppm");
                    Ppm.Write(path, Width, Height, rgb);
                    Console.WriteLine($"wrote {path} (vertex offset {offset})");
                    return 0;
                }

                case "ref":
                {
                    byte[] rgb = RenderReference(ReferenceRounding.Truncate);
                    string path = Path.Combine(outDir, "ref.ppm");
                    Ppm.Write(path, Width, Height, rgb);
                    Console.WriteLine($"wrote {path}");

                    byte[] rounded = RenderReference(ReferenceRounding.Round);
                    string roundedPath = Path.Combine(outDir, "ref-round.ppm");
                    Ppm.Write(roundedPath, Width, Height, rounded);
                    Console.WriteLine($"wrote {roundedPath}");
                    return 0;
                }

                case "uvmap":
                {
                    using HeadlessGlContext ctx = HeadlessGlContext.Create(Width, Height);
                    PrintContext(ctx);
                    byte[] gpu = RenderUv(ctx);
                    byte[] reference = new byte[Width * Height * 3];
                    Vram vram = SyntheticScene.BuildUvVram();
                    SoftwareReference.Render(vram, SyntheticScene.UvPrimitives(), Width, Height, reference,
                        ReferenceRounding.Round);

                    Console.WriteLine("  x   y | gpu R,G (u>>3,v>>3) | ref R,G | expected u,v at (x,y) | at (x+.5,y+.5)");
                    for (int y = 30; y <= 180; y += 30)
                    {
                        for (int x = 40; x <= 190; x += 30)
                        {
                            int o = (y * Width + x) * 3;
                            (double eu, double ev) = ExpectedUv(x, y);
                            (double hu, double hv) = ExpectedUv(x + 0.5, y + 0.5);
                            Console.WriteLine($"  {x,3} {y,3} | {gpu[o],3},{gpu[o + 1],3}            | " +
                                              $"{reference[o],3},{reference[o + 1],3} | {eu,7:F2},{ev,7:F2} | {hu,7:F2},{hv,7:F2}");
                        }
                    }
                    return 0;
                }

                case "diff":
                {
                    if (args.Length < 3)
                    {
                        PrintUsage();
                        return 2;
                    }
                    return RunDiff(args[1], args[2]);
                }

                case "abr3":
                {
                    using HeadlessGlContext ctx = HeadlessGlContext.Create(Width, Height);
                    PrintContext(ctx);
                    CheckAbr3Saturation(ctx);
                    return 0;
                }

                case "all":
                    return RunAll(outDir);

                default:
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAILED: {exception.GetType().Name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
            return 1;
        }
    }

    // ------------------------------------------------------------------ environment

    /// <summary>
    /// Mesa reads LIBGL_ALWAYS_SOFTWARE / MESA_GLES_VERSION_OVERRIDE when it loads the driver, i.e.
    /// before the first P/Invoke, so a managed SetEnvironmentVariable is too late. The probe
    /// therefore restarts itself once with the requested environment instead of guessing.
    /// </summary>
    private static int? MaybeReExec(string[] args)
    {
        var wanted = new Dictionary<string, string>();
        if (args.Contains("--software"))
        {
            wanted["LIBGL_ALWAYS_SOFTWARE"] = "1";
            wanted["MESA_LOADER_DRIVER_OVERRIDE"] = "llvmpipe";
            wanted["GALLIUM_DRIVER"] = "llvmpipe";
        }
        if (args.Contains("--gles2")) wanted["MESA_GLES_VERSION_OVERRIDE"] = "2.0";

        bool missing = wanted.Any(pair => Environment.GetEnvironmentVariable(pair.Key) != pair.Value);
        if (!missing || Environment.GetEnvironmentVariable("FCGPU_PROBE_REEXEC") == "1") return null;

        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var argv = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            argv.Insert(0, Environment.GetCommandLineArgs()[0]);

        var start = new System.Diagnostics.ProcessStartInfo(exe)
        {
            UseShellExecute = false,
        };
        foreach (string argument in argv) start.ArgumentList.Add(argument);
        foreach (var pair in wanted) start.Environment[pair.Key] = pair.Value;
        start.Environment["FCGPU_PROBE_REEXEC"] = "1";

        using var process = System.Diagnostics.Process.Start(start)
                            ?? throw new InvalidOperationException("Could not restart the probe.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static int RunAll(string outDir)
    {
        using HeadlessGlContext ctx = HeadlessGlContext.Create(Width, Height);
        PrintContext(ctx);
        PrintShaderLogs(ctx.Gl);

        byte[] shaders = RenderShaders(ctx);
        Ppm.Write(Path.Combine(outDir, "shaders.ppm"), Width, Height, shaders);
        Console.WriteLine($"wrote {Path.Combine(outDir, "shaders.ppm")}");

        byte[] gpu = RenderScene(ctx, 0f);
        Ppm.Write(Path.Combine(outDir, "ps1.ppm"), Width, Height, gpu);
        Console.WriteLine($"wrote {Path.Combine(outDir, "ps1.ppm")}");

        byte[] gpuOffset = RenderScene(ctx, 0.5f);
        Ppm.Write(Path.Combine(outDir, "ps1-offset.ppm"), Width, Height, gpuOffset);
        Console.WriteLine($"wrote {Path.Combine(outDir, "ps1-offset.ppm")}");

        byte[] reference = RenderReference(ReferenceRounding.Truncate);
        Ppm.Write(Path.Combine(outDir, "ref.ppm"), Width, Height, reference);
        byte[] referenceRounded = RenderReference(ReferenceRounding.Round);
        Ppm.Write(Path.Combine(outDir, "ref-round.ppm"), Width, Height, referenceRounded);
        byte[] referenceHalfUp = RenderReference(ReferenceRounding.RoundHalfUp);
        Ppm.Write(Path.Combine(outDir, "ref-halfup.ppm"), Width, Height, referenceHalfUp);
        Console.WriteLine($"wrote {Path.Combine(outDir, "ref.ppm")}, ref-round.ppm and ref-halfup.ppm");

        Console.WriteLine();
        Console.WriteLine("== pixel comparison (GPU vs software reference, LevelScene.Tri rules) ==");
        ImageDiff exact = ImageDiff.Compare(Width, Height, gpu, reference);
        Console.Write(exact.Describe("gpu vs ref (truncating modulation)"));
        ImageDiff rounded = ImageDiff.Compare(Width, Height, gpu, referenceRounded);
        Console.Write(rounded.Describe("gpu vs ref-round (rounded modulation)"));
        ImageDiff halfUp = ImageDiff.Compare(Width, Height, gpu, referenceHalfUp);
        Console.Write(halfUp.Describe("gpu vs ref-halfup (rounded, half-up ties) - isolates tie pixels"));
        ImageDiff offset = ImageDiff.Compare(Width, Height, gpuOffset, referenceRounded);
        Console.Write(offset.Describe("gpu with +0.5 vertex offset vs ref-round (coverage-only difference)"));

        ImageDiff.WriteVisualisation(Path.Combine(outDir, "diff.ppm"), Width, Height, gpu, reference);
        Console.WriteLine($"wrote {Path.Combine(outDir, "diff.ppm")}");

        Console.WriteLine();
        Console.WriteLine("== shader-mode checks ==");
        int background = shaders[0];
        Console.WriteLine($"top-left pixel (background): {Pixel(shaders, 0, 0)}, expected (0,0,0) " +
                          $"-> {(background == 0 && shaders[1] == 0 && shaders[2] == 0 ? "OK" : "MISMATCH")}");
        Console.WriteLine($"triangle interior (70,110): {Pixel(shaders, 70, 110)}");
        Console.WriteLine($"quad interior (250,120):    {Pixel(shaders, 250, 120)}");
        CheckAbr3Saturation(ctx);
        return 0;
    }

    private static void CheckAbr3Saturation(HeadlessGlContext ctx)
    {
        // STP texel with one channel above saturation after modulation and one below it.
        var vram = new Vram();
        vram.LoadRect(0, 0, 1, 1, new byte[] { 0x9f, 0x81 });
        TextureKey key = SyntheticScene.QuarterAddKey;
        var a = new Ps1Vertex(8, 8, 0, 0, 255, 255, 255);
        var b = new Ps1Vertex(56, 8, 0, 0, 255, 255, 255);
        var c = new Ps1Vertex(8, 56, 0, 0, 255, 255, 255);
        var primitives = new[] { new Ps1Primitive(key, true, in a, in b, in c, 0) };
        const byte red = 32, green = 48, blue = 64;
        var reference = new byte[Width * Height * 3];
        for (int i = 0; i < reference.Length; i += 3)
        {
            reference[i] = red;
            reference[i + 1] = green;
            reference[i + 2] = blue;
        }
        SoftwareReference.Render(vram, primitives, Width, Height, reference, ReferenceRounding.Truncate);

        using var renderer = new GlEs2Renderer(ctx.Gl);
        renderer.SetBackdropColor(red / 255f, green / 255f, blue / 255f);
        var decoded = new byte[VramTextureCache.BytesPerTexture];
        VramTextureCache.Decode(vram, key, decoded);
        renderer.UploadTexture(key, decoded);
        renderer.Render(primitives);
        int error = ctx.Gl.GetError();
        if (error != 0) throw new InvalidOperationException($"ABR3 draw returned GL error 0x{error:X4}.");

        byte[] gpu = ctx.ReadRgb();
        const int x = 24, y = 24;
        int offset = (y * Width + x) * 3;
        string result = $"ABR3 saturated pixel ({x},{y}): GPU {Pixel(gpu, x, y)}, CPU {Pixel(reference, x, y)}";
        for (int ch = offset; ch < offset + 3; ch++)
        {
            // An RGBA8 target rounds the quarter contribution while the CPU shifts it down.
            if (Math.Abs(gpu[ch] - reference[ch]) > 1) throw new InvalidOperationException(result);
        }
        Console.WriteLine(result + " (within 1 LSB)");
    }

    private static int RunDiff(string firstPath, string secondPath)
    {
        var a = Ppm.Read(firstPath);
        var b = Ppm.Read(secondPath);
        if (a.Width != b.Width || a.Height != b.Height)
        {
            Console.Error.WriteLine($"size mismatch: {a.Width}x{a.Height} vs {b.Width}x{b.Height}");
            return 1;
        }
        ImageDiff diff = ImageDiff.Compare(a.Width, a.Height, a.Rgb, b.Rgb);
        Console.Write(diff.Describe($"{Path.GetFileName(firstPath)} vs {Path.GetFileName(secondPath)}"));
        return 0;
    }

    // ------------------------------------------------------------------ rendering

    /// <summary>One plain triangle (white fallback texture) plus one textured quad.</summary>
    private static byte[] RenderShaders(HeadlessGlContext ctx)
    {
        GlApi gl = ctx.Gl;
        using var renderer = new GlEs2Renderer(gl);
        renderer.SetViewport(Width, Height);
        Vram vram = SyntheticScene.BuildVram();
        SyntheticScene.UploadTextures(renderer, vram);

        var tri0 = new Ps1Vertex(10, 20, 0, 0, 255, 16, 16);
        var tri1 = new Ps1Vertex(150, 60, 0, 0, 16, 255, 16);
        var tri2 = new Ps1Vertex(60, 210, 0, 0, 16, 16, 255);

        var q0 = new Ps1Vertex(170, 20, 0, 0, 128, 128, 128);
        var q1 = new Ps1Vertex(310, 20, 255, 0, 128, 128, 128);
        var q2 = new Ps1Vertex(170, 220, 0, 255, 128, 128, 128);
        var q3 = new Ps1Vertex(310, 220, 255, 255, 128, 128, 128);

        var primitives = new[]
        {
            new Ps1Primitive(new TextureKey(SyntheticScene.PageTpage, 0), false, in tri0, in tri1, in tri2, 0),
            new Ps1Primitive(SyntheticScene.OpaqueKey, false, in q0, in q1, in q2, 1),
            new Ps1Primitive(SyntheticScene.OpaqueKey, false, in q1, in q3, in q2, 2),
        };

        gl.ClearColorHostOnly(0, 0, 0, 255);
        gl.Clear(GlEnums.ColorBufferBit);
        CheckGl(gl, "before shaders render");

        // The triangle uses a page that was never uploaded -> the renderer's white fallback.
        // One Render call per frame: every call starts with the backdrop quad, so two calls would
        // paint over the first batch.
        renderer.Render(primitives);
        CheckGl(gl, "after shaders render");

        Console.WriteLine($"draws: {renderer.LastDrawCalls}, " +
                          $"triangles: {renderer.LastTriangleCount}, " +
                          $"textures: {renderer.TextureCount}");
        return ctx.ReadRgb();
    }

    /// <summary>The fixed primitive list through <see cref="GlEs2Renderer"/>.</summary>
    private static byte[] RenderScene(HeadlessGlContext ctx, float offset)
    {
        GlApi gl = ctx.Gl;
        using var renderer = new GlEs2Renderer(gl);
        renderer.SetViewport(Width, Height);

        Vram vram = SyntheticScene.BuildVram();
        SyntheticScene.UploadTextures(renderer, vram);
        Ps1Primitive[] primitives = SyntheticScene.Primitives(offset);

        gl.ClearColorHostOnly(0, 0, 0, 255);
        gl.Clear(GlEnums.ColorBufferBit);
        CheckGl(gl, "before scene render");

        renderer.Render(primitives);
        CheckGl(gl, "after scene render");

        Console.WriteLine($"scene offset {offset}: draws: {renderer.LastDrawCalls}, " +
                          $"triangles: {renderer.LastTriangleCount}, textures: {renderer.TextureCount}");
        return ctx.ReadRgb();
    }

    /// <summary>Renders the UV-encoding page so the sampled texel coordinate becomes readable.</summary>
    private static byte[] RenderUv(HeadlessGlContext ctx)
    {
        GlApi gl = ctx.Gl;
        using var renderer = new GlEs2Renderer(gl);
        renderer.SetViewport(Width, Height);
        Vram vram = SyntheticScene.BuildUvVram();
        SyntheticScene.UploadTextures(renderer, vram);

        gl.ClearColorHostOnly(0, 0, 0, 255);
        gl.Clear(GlEnums.ColorBufferBit);
        renderer.Render(SyntheticScene.UvPrimitives());
        CheckGl(gl, "after uv render");
        return ctx.ReadRgb();
    }

    /// <summary>Exact affine UV at a screen-space point for the diagnostic triangle.</summary>
    private static (double U, double V) ExpectedUv(double x, double y)
    {
        const double x0 = 24, y0 = 18, u0 = 8, v0 = 8;
        const double x1 = 200, y1 = 42, u1 = 112, v1 = 16;
        const double x2 = 64, y2 = 196, u2 = 24, v2 = 136;
        double area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        double w0 = (x1 - x) * (y2 - y) - (y1 - y) * (x2 - x);
        double w1 = (x2 - x) * (y0 - y) - (y2 - y) * (x0 - x);
        double w2 = (x0 - x) * (y1 - y) - (y0 - y) * (x1 - x);
        return ((w0 * u0 + w1 * u1 + w2 * u2) / area, (w0 * v0 + w1 * v1 + w2 * v2) / area);
    }

    private static byte[] RenderReference(ReferenceRounding rounding)
    {
        Vram vram = SyntheticScene.BuildVram();
        byte[] rgb = new byte[Width * Height * 3];
        SoftwareReference.Render(vram, SyntheticScene.Primitives(), Width, Height, rgb, rounding);
        return rgb;
    }

    // ------------------------------------------------------------------ diagnostics

    private static void PrintContext(HeadlessGlContext ctx)
    {
        foreach (string line in ctx.Log) Console.WriteLine(line);
        Console.WriteLine($"renderer target: {ctx.Width}x{ctx.Height}");
        Console.WriteLine();
    }

    /// <summary>Compiles both shader sources separately so the driver's logs are visible.</summary>
    private static void PrintShaderLogs(GlApi gl)
    {
        Console.WriteLine("== shader compilation ==");
        Report(gl, GlEnums.VertexShader, "vertex", Ps1Shaders.Vertex);
        Report(gl, GlEnums.FragmentShader, "fragment", Ps1Shaders.Fragment);
        CheckGl(gl, "after shader compilation");
        Console.WriteLine();
    }

    private static void Report(GlApi gl, int type, string label, string source)
    {
        int shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        int status = gl.GetShaderParameter(shader, GlEnums.CompileStatus);
        string log = gl.GetShaderInfoLog(shader).Trim();
        Console.WriteLine($"{label}: compile status {status} ({(status == 1 ? "ok" : "FAILED")})" +
                          $"{(log.Length == 0 ? ", empty log" : ", log: " + log)}");
        gl.DeleteShader(shader);
    }

    private static void CheckGl(GlApi gl, string stage)
    {
        int errors = 0;
        int error;
        while ((error = gl.GetError()) != 0)
        {
            Console.WriteLine($"  glGetError after {stage}: 0x{error:X4}");
            errors++;
            if (errors > 8) break;
        }
        if (errors == 0) Console.WriteLine($"  glGetError after {stage}: clean");
    }

    private static string Pixel(byte[] rgb, int x, int y)
    {
        int o = (y * Width + x) * 3;
        return $"({rgb[o]},{rgb[o + 1]},{rgb[o + 2]})";
    }

    private static string? Option(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    private static float ParseOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0f;
        string[] parts = text.Split(',');
        return float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void PrintUsage() => Console.WriteLine(
        "FcGpuProbe - headless GL ES 2.0 probe for FutureCop.Gpu\n\n" +
        "  FcGpuProbe shaders [--out DIR] [--software] [--gles2]   triangle + textured quad\n" +
        "  FcGpuProbe ps1     [--offset dx[,dy]] [--out DIR] [--software]\n" +
        "                                                  fixed Ps1Primitive list through the renderer\n" +
        "  FcGpuProbe ref     [--out DIR]                  same list through the LevelScene.Tri rules\n" +
        "  FcGpuProbe diff    A.ppm B.ppm                  pixel metrics between two images\n" +
        "  FcGpuProbe abr3    [--software] [--gles2]      saturated ABR3 pixel regression\n" +
        "  FcGpuProbe all     [--out DIR] [--software]     everything above plus the comparison\n\n" +
        "Runs without X/Wayland: surfaceless EGL, pbuffer or FBO.\n" +
        "--software renders on llvmpipe, --gles2 caps Mesa at GL ES 2.0 (GLSL ES 1.00).");
}
