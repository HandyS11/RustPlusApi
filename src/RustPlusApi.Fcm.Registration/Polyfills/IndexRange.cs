#if NETSTANDARD2_0
// Enables the index (`^`) and range (`..`) operators when targeting netstandard2.0,
// where these compiler-required types are not part of the BCL. Only the members the C#
// compiler binds against are implemented; see the reference impl in dotnet/runtime.
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System;

/// <summary>Indexes a collection from the start (<c>i</c>) or the end (<c>^i</c>).</summary>
/// <remarks>Excluded from coverage: compiler scaffolding, never executed. The type only has to
/// exist for the C# compiler to accept <c>..</c>/<c>^</c> syntax on netstandard2.0; Roslyn lowers
/// every current use site to a direct <c>Substring</c> call, so no member here is reachable at
/// runtime. (<c>Justification</c> is unavailable on netstandard2.0 — see
/// <c>docs/development/testing.md</c>.)</remarks>
[ExcludeFromCodeCoverage]
internal readonly struct Index
{
    private readonly int _value;

    public Index(int value, bool fromEnd = false)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Non-negative number required.");
        }

        _value = fromEnd ? ~value : value;
    }

    private int Value => _value < 0 ? ~_value : _value;
    private bool IsFromEnd => _value < 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetOffset(int length) => IsFromEnd ? length - Value : Value;

    public static implicit operator Index(int value) => new(value);
}

/// <summary>Represents the range used to slice a collection with the <c>..</c> operator.</summary>
/// <param name="start">The inclusive start index of the range.</param>
/// <param name="end">The exclusive end index of the range.</param>
/// <remarks>Excluded from coverage: compiler scaffolding, never executed — see
/// <see cref="Index"/> above.</remarks>
[ExcludeFromCodeCoverage]
internal readonly struct Range(Index start, Index end)
{
    public Index Start { get; } = start;
    public Index End { get; } = end;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int Offset, int Length) GetOffsetAndLength(int length)
    {
        var startOffset = Start.GetOffset(length);
        var endOffset = End.GetOffset(length);

        if ((uint)endOffset > (uint)length || (uint)startOffset > (uint)endOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return (startOffset, endOffset - startOffset);
    }
}
#endif
