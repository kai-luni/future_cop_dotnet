using FcMainMenu;
using FutureCop.Assets;
using Xunit;

public sealed class BitmapPanelTests
{
    private static bool HasColorInBand(byte[] rgb, int yMin, int yMax, byte r, byte g, byte b)
    {
        for (int y = yMin; y <= yMax; y++)
            for (int x = 0; x < BitmapPanel.Width; x++)
            {
                int i = (y * BitmapPanel.Width + x) * 3;
                if (rgb[i] == r && rgb[i + 1] == g && rgb[i + 2] == b) return true;
            }
        return false;
    }

    [Fact]
    public void RenderDrawsHeadingEntriesSelectionAndHintInExpectedColors()
    {
        var rgb = new byte[BitmapPanel.Width * BitmapPanel.Height * 3];
        var panel = new MenuPanel("Precinct Assault", new[] { "Ready", "Name: PLAYER", "Skill: Normal" }, 1, "ARROWS: SELECT / CHANGE   ENTER: ACCEPT   ESC: BACK");
        BitmapPanel.Render(rgb, panel);

        Assert.Equal(0x10, rgb[0]);
        Assert.Equal(0x18, rgb[1]);
        Assert.Equal(0x2e, rgb[2]);
        Assert.True(HasColorInBand(rgb, 95, 125, 0x73, 0xc5, 0xff));
        Assert.True(HasColorInBand(rgb, 165, 190, 0xff, 0xff, 0xff));
        Assert.True(HasColorInBand(rgb, 195, 225, 0xff, 0xff, 0x80));
        Assert.True(HasColorInBand(rgb, 430, 460, 0xc0, 0xc0, 0xc0));
    }

    [Fact]
    public void RenderHandlesLongHintAndNonAsciiFallbackAndWritesBmp24()
    {
        var rgb = new byte[BitmapPanel.Width * BitmapPanel.Height * 3];
        var panel = new MenuPanel("Ümlaut", new[] { "> CONFT" }, 0, new string('A', 80));
        BitmapPanel.Render(rgb, panel);
        Assert.True(HasColorInBand(rgb, 430, 460, 0xc0, 0xc0, 0xc0));

        string bmp = Path.Combine(Path.GetTempPath(), $"fcpanel-{Guid.NewGuid():N}.bmp");
        try
        {
            TextureBank.WriteBmp24(bmp, BitmapPanel.Width, BitmapPanel.Height, rgb);
            byte[] bytes = File.ReadAllBytes(bmp);
            Assert.Equal((byte)'B', bytes[0]);
            Assert.Equal((byte)'M', bytes[1]);
            Assert.Equal(54 + BitmapPanel.Width * BitmapPanel.Height * 3, bytes.Length);
        }
        finally { if (File.Exists(bmp)) File.Delete(bmp); }
    }
}
