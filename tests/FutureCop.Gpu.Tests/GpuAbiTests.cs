using System.Reflection;
using Xunit;

namespace FutureCop.Gpu.Tests;

/// <summary>
/// The softfp/hardfp ABI guard: on Tizen ARMv7 a GL entry point that takes a float by value
/// receives garbage from our hardfp CoreCLR, so no such parameter may exist on the interface.
/// </summary>
public class GpuAbiTests
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
    public void InterfaceHasNoFloatingPointParameters()
    {
        foreach (MethodInfo method in typeof(IGpuApi).GetMethods())
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.True(AllowedParameters.Contains(parameter.ParameterType),
                    $"{method.Name}({parameter.Name}) has parameter type {parameter.ParameterType.Name}; " +
                    "IGpuApi must only use int/uint/bool/IntPtr/byte[]/string.");
            }
            Assert.True(AllowedReturns.Contains(method.ReturnType),
                $"{method.Name} returns {method.ReturnType.Name}.");
            Assert.DoesNotContain("float", method.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("double", method.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("ClearColor")]
    [InlineData("BlendColor")]
    [InlineData("DepthRangef")]
    [InlineData("LineWidth")]
    [InlineData("TexParameterf")]
    [InlineData("Uniform1f")]
    [InlineData("Uniform2f")]
    [InlineData("Uniform3f")]
    [InlineData("Uniform4f")]
    [InlineData("VertexAttrib1f")]
    [InlineData("VertexAttrib2f")]
    [InlineData("VertexAttrib3f")]
    [InlineData("VertexAttrib4f")]
    public void ForbiddenEntryPointsAreAbsent(string name)
    {
        Assert.DoesNotContain(typeof(IGpuApi).GetMethods(), m => m.Name == name);
    }

    [Theory]
    [InlineData("Uniform1fv")]
    [InlineData("Uniform4fv")]
    [InlineData("UniformMatrix4fv")]
    [InlineData("TexParameteri")]
    [InlineData("BlendFunc")]
    [InlineData("BlendEquation")]
    [InlineData("DrawArrays")]
    [InlineData("BufferData")]
    [InlineData("BufferSubData")]
    [InlineData("VertexAttribPointer")]
    public void RequiredEntryPointsExist(string name)
    {
        Assert.Contains(typeof(IGpuApi).GetMethods(), m => m.Name == name);
    }

    [Fact]
    public void FloatDataTravelsThroughPointers()
    {
        foreach (MethodInfo method in typeof(IGpuApi).GetMethods())
        {
            if (!method.Name.StartsWith("Uniform", StringComparison.Ordinal)) continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (method.Name.EndsWith("v", StringComparison.Ordinal) && method.Name != "Uniform1i")
                Assert.Contains(parameters, p => p.ParameterType == typeof(IntPtr));
        }
    }
}
