using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

namespace FcMainMenu;

/// <summary>A small UI-thread overlay that does not render through the mission scene.</summary>
internal sealed class FpsHud : IDisposable
{
    private readonly View panel;
    private readonly List<View> bars = new();
    private float scale = 1;

    internal FpsHud(Window window)
    {
        panel = new View { Name = "submitted-fps", BackgroundColor = new Color(0f, 0f, 0f, 0.75f) };
        window.GetDefaultLayer().Add(panel);
        SetText("SUB FPS: --");
    }

    internal void Layout(int x, int y, int width, float sceneScale)
    {
        scale = Math.Max(2f, sceneScale * 1.5f);
        panel.Size2D = new Size2D((int)(58 * scale), (int)(9 * scale));
        panel.Position2D = new Position2D(x + width - (int)(60 * scale), y + (int)(2 * scale));
    }

    internal void SetText(string text)
    {
        var runs = FpsHudLayout.Runs(text);
        while (bars.Count < runs.Count)
        {
            var bar = new View { BackgroundColor = new Color(1f, 1f, 1f, 1f) };
            panel.Add(bar);
            bars.Add(bar);
        }
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (i >= runs.Count) { bar.Hide(); continue; }
            var run = runs[i];
            bar.Position2D = new Position2D((int)((run.X + 2) * scale), (int)((run.Y + 2) * scale));
            bar.Size2D = new Size2D((int)(run.Width * scale), (int)scale);
            bar.Show();
        }
    }

    public void Dispose()
    {
        panel.Dispose();
        foreach (var bar in bars) bar.Dispose();
        bars.Clear();
    }
}
