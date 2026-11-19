using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace FutureCop.Gpu.Tests;

/// <summary>
/// Guards the two device measurements this API exists for: GL is reached only through function
/// pointers (never DllImport) and only through parameters that survive the softfp/hardfp boundary.
/// No test here loads a real GL library; the loader is injected.
/// </summary>
public class GlEs2ApiTests
{
    private static readonly HashSet<Type> AllowedParameters = new()
    {
        typeof(int),
        typeof(uint),
        typeof(bool),
        typeof(IntPtr),
        typeof(byte[]),
        typeof(string),
    };

    private static readonly HashSet<Type> AllowedReturns = new()
    {
        typeof(void),
        typeof(int),
        typeof(uint),
        typeof(bool),
        typeof(IntPtr),
        typeof(string),
    };

    [Fact]
    public void LibraryNamesAreTriedInTheDocumentedOrder()
    {
        var attempted = new List<string>();
        DllNotFoundException exception = Assert.Throws<DllNotFoundException>(() =>
            GlEs2Api.Create(
                (string name, out IntPtr handle) =>
                {
                    attempted.Add(name);
                    handle = IntPtr.Zero;
                    return false;
                },
                (IntPtr handle, string name) => IntPtr.Zero));

        Assert.Equal(new[] { "libGLESv2.so.2", "libGLESv2.so" }, attempted);
        Assert.Equal(attempted, GlEs2Api.LibraryNames);

        // The message has to name what was tried and where the device library lives: this exception
        // is the only diagnosis a black TV screen will offer.
        Assert.Contains("libGLESv2.so.2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("libGLESv2.so", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OpenGL ES 2.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondCandidateIsTriedWhenTheSonameIsMissing()
    {
        // Tizen ships the SONAME only, but a host install may have just the development symlink.
        var attempted = new List<string>();
        Assert.Throws<EntryPointNotFoundException>(() =>
            GlEs2Api.Create(
                (string name, out IntPtr handle) =>
                {
                    attempted.Add(name);
                    handle = name == "libGLESv2.so" ? (IntPtr)42 : IntPtr.Zero;
                    return name == "libGLESv2.so";
                },
                (IntPtr handle, string name) => IntPtr.Zero));

        Assert.Equal(new[] { "libGLESv2.so.2", "libGLESv2.so" }, attempted);
    }

    [Fact]
    public void MissingExportNamesTheEntryPointAndTheLibrary()
    {
        var exception = Assert.Throws<EntryPointNotFoundException>(() =>
            GlEs2Api.Create(
                (string name, out IntPtr handle) =>
                {
                    handle = (IntPtr)42;
                    return true;
                },
                (IntPtr handle, string name) => name == "glBlendEquation" ? IntPtr.Zero : (IntPtr)7));

        Assert.Contains("glBlendEquation", exception.Message, StringComparison.Ordinal);
        Assert.Contains("libGLESv2.so.2", exception.Message, StringComparison.Ordinal);

        // Exactly the missing function is reported, not the whole entry point table.
        Assert.DoesNotContain("glDrawArrays", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoDllImportAnywhereInTheGpuAssembly()
    {
        // Measured on the device: [DllImport] killed the process without a message on the first GL
        // call, with and without a DllImportResolver. A single reintroduced stub would bring that
        // failure back, so the whole assembly is checked, not just GlEs2Api.
        var offenders = new List<string>();
        foreach (Type type in typeof(GlEs2Api).Assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<DllImportAttribute>() is not null ||
                    (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    offenders.Add($"{type.Name}.{method.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiSurfaceHasNoFloatingPointParameters()
    {
        // glClearColor(0.1, 0.25, 0.45, 1) plus glClear produced a black frame on the device: every
        // float parameter arrives as 0.0 there. The concrete API must obey the same rule as
        // IGpuApi (which GpuAbiTests already enforces).
        foreach (MethodInfo method in typeof(GlEs2Api).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.True(AllowedParameters.Contains(parameter.ParameterType),
                    $"GlEs2Api.{method.Name}({parameter.Name}) has parameter type {parameter.ParameterType.Name}.");
            }
            Assert.True(AllowedReturns.Contains(method.ReturnType),
                $"GlEs2Api.{method.Name} returns {method.ReturnType.Name}.");
        }
    }

    [Theory]
    [InlineData("ClearColor")]
    [InlineData("Uniform1f")]
    [InlineData("Uniform4f")]
    [InlineData("BlendColor")]
    [InlineData("LineWidth")]
    public void ForbiddenEntryPointsAreNotEvenDeclared(string name)
    {
        Assert.DoesNotContain(typeof(GlEs2Api).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
            m => m.Name == name);
    }

    [Fact]
    public void ImplementsTheWholeRendererInterface()
    {
        Assert.True(typeof(IGpuApi).IsAssignableFrom(typeof(GlEs2Api)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(GlEs2Api)));

        foreach (MethodInfo method in typeof(IGpuApi).GetMethods())
        {
            MethodInfo? implementation = typeof(GlEs2Api).GetMethod(method.Name);
            Assert.NotNull(implementation);
            Assert.True(implementation!.IsPublic);
        }
    }
}