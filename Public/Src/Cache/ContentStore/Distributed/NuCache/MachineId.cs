// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Utilities.Serialization;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache;

/// <summary>
/// Represents id of a machine.
/// </summary>
public readonly record struct MachineId(int Index) : IEquatable<int>
{
    /// <summary>
    /// The minimum valid MachineId.
    /// </summary>
    public const int MinValue = 1;

    /// <summary>
    /// The de facto invalid MachineId.
    /// </summary>
    public static MachineId Invalid { get; } = new(0);

    /// <summary>
    /// Whether the MachineId is a valid instance.
    /// </summary>
    /// <remarks>
    /// Must be >= 1
    /// </remarks>
    public bool Valid => Index >= MinValue;

    /// <nodoc />
    public static MachineId Deserialize(BinaryReader reader)
    {
        var index = reader.ReadInt32();
        return new MachineId(index);
    }

    /// <nodoc />
    public static MachineId Deserialize(ref SpanReader reader)
    {
        var index = reader.ReadInt32();
        return new MachineId(index);
    }

    /// <nodoc />
    public void Serialize(ref SpanWriter writer)
    {
        writer.Write(Index);
    }

    /// <nodoc />
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Index);
    }

    /// <inheritdoc />
    public bool Equals(int other)
    {
        return Index == other;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Index.ToString();
    }
}
