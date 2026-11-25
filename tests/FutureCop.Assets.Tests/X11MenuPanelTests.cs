using System.Text;
using FcMainMenu;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class X11MenuPanelTests
{
    [Fact]
    public void MissingLocalArchiveUsesTheInstalledLabels()
    {
        string root = Path.Combine(Path.GetTempPath(), "fc-menu-labels-" + Guid.NewGuid().ToString("N"));
        try
        {
            string program = Path.Combine(root, "tools", "FcMainMenu", "bin", "Release", "net10.0");
            string retail = Path.Combine(root, "build", "disc", "files", "CW");
            string launch = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(program);
            Directory.CreateDirectory(retail);
            Directory.CreateDirectory(launch);

            // A minimal SHOC/SHDR text resource followed by one SDAT packet with two labels.
            byte[] payload = { 1, 0, 2, 0, 0xb8, 0x0b, 8, 0, 0x48, 0x0d, 20, 0 };
            byte[] strings = Encoding.ASCII.GetBytes("SELECT\nZONE\0Urban\nJungle\0");
            int length = payload.Length + strings.Length;
            using (var writer = new BinaryWriter(File.Create(Path.Combine(retail, "FE.MIS"))))
            {
                writer.Write(ChunkReader.TagValue("SHOC"));
                writer.Write(60);
                writer.Write(0L);
                writer.Write(ChunkReader.TagValue("SHDR"));
                writer.Write(0);
                writer.Write(ChunkReader.TagValue("Ctrs"));
                writer.Write(1);
                writer.Write(length);
                writer.Write(new byte[24]);
                writer.Write(ChunkReader.TagValue("SHOC"));
                writer.Write(20 + length);
                writer.Write(0L);
                writer.Write(ChunkReader.TagValue("SDAT"));
                writer.Write(payload);
                writer.Write(strings);
            }

            string mission = Path.Combine(launch, "CONFT.MIS");
            File.WriteAllBytes(mission, Array.Empty<byte>());
            using var errors = new StringWriter();
            var label = MenuLabels.Load(mission, program, errors);

            Assert.Equal("SELECT ZONE", label(0x0bb8, "Select Zone"));
            Assert.Equal("Urban Jungle", label(0x0d48, "conft"));
            Assert.Equal("No entry", label(0xffff, "No\nentry"));
            Assert.Equal("", errors.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PanelRowsShareALeftEdgeWhileTheSelectedRowKeepsItsMarkerAndColor()
    {
        string[] entries = { "Urban Jungle", "> Proving Ground", "Hollywood Keys", "Venice Beach" };
        var rows = X11PanelLayout.Rows(entries, selection: 1, width: 640, text => text.Length * 9).ToArray();

        int left = (640 - "Proving Ground".Length * 9) / 2;
        Assert.Equal(new[]
        {
            ("Urban Jungle",   left,      84,  0xffffffUL),
            ("> ",             left - 18, 100, 0xffff80UL),
            ("Proving Ground", left,      100, 0xffff80UL),
            ("Hollywood Keys", left,      116, 0xffffffUL),
            ("Venice Beach",   left,      132, 0xffffffUL),
        }, rows);
    }
}
