#if NETSTANDARD2_0
// Enables `init`-only setters and records when targeting netstandard2.0,
// where this compiler-required type is not part of the BCL.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;
}
#endif
