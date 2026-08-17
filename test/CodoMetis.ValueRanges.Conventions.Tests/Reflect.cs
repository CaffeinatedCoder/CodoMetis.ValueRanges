using System.Reflection;
using System.Runtime.ExceptionServices;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Calls a generic test helper closed over a discovered type pair.
/// </summary>
/// <remarks>
/// The discovery-driven suites here close a generic method over each set type they find, which
/// means reflection, which means a failed assertion arrives wrapped in a
/// <see cref="TargetInvocationException"/> — the message survives but the reader has to dig past a
/// frame that says nothing. Rethrowing the inner exception with its stack intact puts the assertion
/// back on the surface where a non-reflective test would have it.
/// </remarks>
internal static class Reflect
{
    internal static void InvokeGeneric(Type host, string method, params Type[] typeArguments)
    {
        try
        {
            host.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeArguments)
                .Invoke(null, null);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } actual)
        {
            ExceptionDispatchInfo.Capture(actual).Throw();
        }
    }
}
