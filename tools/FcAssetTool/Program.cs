using FutureCop.Assets;

// CLI: list models in a .MIS archive, dump one model's polygons/vertices, or list/export Cbmp textures.
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: FcAssetTool list <file.MIS> | dump <file.MIS> <modelId> | textures <file.MIS> [outdir]");
    return 2;
}

if (args[0] == "textures")
{
    var bank = TextureBank.FromMisFile(args[1]);
    string? outDir = args.Length >= 3 ? args[2] : null;
    if (outDir != null) Directory.CreateDirectory(outDir);
    Console.WriteLine($"{args[1]}: {bank.Resources.Count} Cbmp texture resources");
    foreach (var r in bank.Resources)
    {
        var c = r.Container;
        string pixTag = c.IsPix4 ? "PIX4" : "PDAT";
        Console.WriteLine($"  id {r.Id,2} @0x{r.RecordOffset:X6}: payload {r.PayloadSize}B, " +
                          $"CCB {c.CcbLength}B, {pixTag} {c.PdatLength}B, PLUT {c.PlutLength}B, " +
                          $"vram ({r.PageX},{r.PageY} {r.PageW}x{r.PageH}), clut ({r.ClutX},{r.ClutY} {r.ClutW}x{r.ClutH}), " +
                          $"tpage 0x{r.Tpage:X4}, clut 0x{r.Clut:X4}");
        if (outDir != null)
        {
            string bmpPath = Path.Combine(outDir, $"page_{r.Id:D2}.bmp");
            TextureBank.WriteBmp24(bmpPath, 256, 256, bank.ExportPageRgb(r.Id));
        }
    }
    return 0;
}

var mis = new MisFile(args[1]);
Console.WriteLine($"{mis.Path}: {mis.Models.Count} Cobj models, sections [{string.Join(", ", mis.SectionOffsets.Select(o => "0x" + o.ToString("X")))}]");

if (args[0] == "list")
{
    foreach (var m in mis.Models)
    {
        int verts = m.Vertices.Values.FirstOrDefault()?.Count ?? 0;
        var types = m.Polygons.GroupBy(p => p.PrimitiveType).OrderBy(g => g.Key)
            .Select(g => $"t{g.Key}x{g.Count()}");
        Console.WriteLine($"  id {m.Id,3} @0x{m.RecordOffset:X6}: polys {m.Polygons.Count,4}, verts {verts,4}, " +
                          $"frames {m.Vertices.Count}, tex {m.TextureRecordCount}B, hier {m.HierarchyEntries}, [{string.Join(" ", types)}]");
    }
    return 0;
}

if (args[0] == "dump" && args.Length >= 3 && int.TryParse(args[2], out int id))
{
    var m = mis.Models.FirstOrDefault(x => x.Id == id);
    if (m == null) { Console.Error.WriteLine($"model {id} not found"); return 1; }
    Console.WriteLine($"model {m.Id} chunks: {string.Join(" ", m.ChunkTags)}");
    Console.WriteLine($"header u32: {string.Join(" ", m.Header.Select(h => h.ToString("X8")))}");
    var vl = m.Vertices.Values.FirstOrDefault() ?? new List<Vertex>();
    Console.WriteLine($"vertices frame1: {vl.Count}");
    for (int i = 0; i < Math.Min(8, vl.Count); i++) Console.WriteLine($"  v{i}: {vl[i].X} {vl[i].Y} {vl[i].Z}");
    Console.WriteLine($"polygons: {m.Polygons.Count} (first 12)");
    foreach (var p in m.Polygons.Take(12))
        Console.WriteLine($"  type {p.PrimitiveType} group {p.PartGroup} verts {p.VertexCount} " +
                          $"mode {p.ShadingMode} semi {p.SemiTransparent} extra {p.ExtraPass} " +
                          $"tex 0x{p.TextureOffset:X4} idx [{p.V0},{p.V1},{p.V2},{p.V3}] nrm [{p.N0},{p.N1},{p.N2},{p.N3}]");
    // Sanity: every polygon vertex index must fit the frame-1 vertex list.
    int bad = m.Polygons.Count(p => p.PrimitiveType is 3 or 4 &&
        (p.V0 >= vl.Count || p.V1 >= vl.Count || p.V2 >= vl.Count || (p.PrimitiveType == 4 && p.V3 >= vl.Count)));
    Console.WriteLine($"index sanity: {bad} out-of-range tri/quad polygons");
    return bad == 0 ? 0 : 1;
}

Console.Error.WriteLine("unknown command");
return 2;