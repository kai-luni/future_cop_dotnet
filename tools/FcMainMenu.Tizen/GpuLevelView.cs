#nullable enable
using System;
using FutureCop.Gpu;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

namespace FcMainMenu;

/// <summary>Owns the NUI view on the UI thread and binds its context callbacks to the session.</summary>
internal sealed class GpuLevelView : IDisposable
{
    private GlEs2Api? api;
    internal GLView View { get; }
    internal GpuLevelSession Session { get; }

    internal GpuLevelView(TvLevelControl control)
    {
        Session = new GpuLevelSession(control);
        try
        {
            View = new GLView(GLView.ColorFormat.RGBA8888)
            {
                Name = "mission-gl",
                WidthResizePolicy = ResizePolicyType.Fixed,
                HeightResizePolicy = ResizePolicyType.Fixed,
            };
            if (!View.SetGraphicsConfig(false, false, 0, GLESVersion.Version20))
                throw new InvalidOperationException("GLView could not configure an OpenGL ES 2.0 context.");
            View.RenderingMode = GLRenderingMode.OnDemand;
            View.SetResizeCallback(Session.Resize);
            View.RegisterGLCallbacks(Initialize, Draw, Terminate);
        }
        catch
        {
            Session.Stop();
            View?.Dispose();
            throw;
        }
    }

    private void Initialize()
    {
        bool initialized = false;
        try
        {
            initialized = Session.Initialize(() => api ??= GlEs2Api.Create());
            if (initialized)
                MenuApp.Log($"mission GL context {api!.GetString(GlEnums.Version)} / {api.GetString(GlEnums.Renderer)}");
        }
        catch (Exception ex) { MenuApp.Log("mission GL context callback failed " + ex); }
        finally { if (!initialized) ReleaseApi(); }
    }

    private int Draw() => Session.Draw() ? 1 : 0;

    private void Terminate()
    {
        try { Session.Terminate(); }
        catch (Exception ex) { MenuApp.Log("mission GL teardown failed " + ex); }
        finally { ReleaseApi(); }
    }

    private void ReleaseApi()
    {
        var previous = api;
        api = null;
        try { previous?.Dispose(); }
        catch (Exception ex) { MenuApp.Log("mission GL library release failed " + ex); }
    }

    public void Dispose()
    {
        Session.Stop();
        // View.Dispose unparents the actor. GL resources are released by its terminate callback.
        View.Dispose();
    }
}
