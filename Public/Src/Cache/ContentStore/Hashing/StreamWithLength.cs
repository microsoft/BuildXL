// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Diagnostics.ContractsLight;
using System;

#nullable enable

namespace BuildXL.Cache.ContentStore.Hashing;

/// <summary>
/// Stream that is guaranteed to have a length.
/// </summary>
public readonly record struct StreamWithLength(Stream Stream, long Length) : IDisposable
{
    /// <summary>
    /// MemoryStream always has a length so it can be automatically wrapped.
    /// </summary>
    public static implicit operator StreamWithLength(MemoryStream s) => s.WithLength();

    /// <summary>
    /// FileStream always has a length so it can be automatically wrapped.
    /// </summary>
    public static implicit operator StreamWithLength(FileStream s) => s.WithLength();

    /// <summary>
    /// MemoryStream always has a length so it can be automatically wrapped.
    /// </summary>
    public static implicit operator StreamWithLength?(MemoryStream? s) => s?.WithLength();

    /// <summary>
    /// FileStream always has a length so it can be automatically wrapped.
    /// </summary>
    public static implicit operator StreamWithLength?(FileStream? s) => s?.WithLength();

    /// <summary>
    /// Implicitly expose stream for all operations on it.
    /// </summary>
    public static implicit operator Stream(StreamWithLength s) => s.Stream;

    /// <inheritdoc />
    public void Dispose()
    {
        Stream.Dispose();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (Stream is FileStream fs)
        {
            return $"Length={Length}, FileStream={fs.Name}";
        }

        return $"Length={Length}, StreamType={Stream.GetType()}";
    }
}

/// <summary>
/// Helpers for creating a StreamWithLength
/// </summary>
public static class StreamWithLengthExtensions
{
    /// <summary>
    /// Verify at runtime that stream has a Length.
    /// </summary>
    public static StreamWithLength AssertHasLength(this Stream s)
    {
        Contract.Requires(s != null);
        Contract.Requires(s.CanSeek);
        return new StreamWithLength(s, s.Length);
    }

    /// <summary>
    /// With an explicit length.
    /// </summary>
    public static StreamWithLength WithLength(this Stream s, long length)
    {
        Contract.Requires(length >= 0);
        return new StreamWithLength(s, length);
    }

    /// <summary>
    /// Helper for safely wrapping MemoryStream.
    /// </summary>
    public static StreamWithLength WithLength(this MemoryStream s)
    {
        return new StreamWithLength(s, s.Length);
    }

    /// <summary>
    /// Helper for safely wrapping FileStream.
    /// </summary>
    public static StreamWithLength WithLength(this FileStream s)
    {
        Contract.Requires(s != null);
        return new StreamWithLength(s, s.Length);
    }
}
