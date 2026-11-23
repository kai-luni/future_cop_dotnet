using System.Runtime.InteropServices;
using System.IO.Pipes;

namespace FutureCop.Assets;

/// <summary>Limits the desktop player's pre-mixed pipe backlog on Linux.</summary>
public static class LinuxAudioPipe
{
    private const int SetPipeSize = 1031;
    private const int TargetBytes = 4096;

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int command, int size);

    public static bool TryLimit(Stream stream)
    {
        if (!OperatingSystem.IsLinux() || stream is not PipeStream pipe) return false;
        return Fcntl(checked((int)pipe.SafePipeHandle.DangerousGetHandle()), SetPipeSize, TargetBytes) > 0;
    }
}
