// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Serialization;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

/// <summary>
///     Pairing of a content hash list and corresponding determinism guarantee.
/// </summary>
public readonly record struct ContentHashListWithDeterminism(ContentHashList? ContentHashList, CacheDeterminism Determinism)
{
    /// <summary>
    ///     Serializes an instance into a binary stream.
    /// </summary>
    public void Serialize(BuildXLWriter writer)
    {
        writer.Write(ContentHashList != null);
        ContentHashList?.Serialize(writer);

        var determinism = Determinism.Serialize();
        writer.Write(determinism.Length);
        writer.Write(determinism);
    }

    /// <summary>
    ///     Serializes an instance into a binary stream.
    /// </summary>
    public void Serialize(ref SpanWriter writer)
    {
        writer.Write(ContentHashList != null);
        ContentHashList?.Serialize(ref writer);

        var determinism = Determinism.Serialize();
        writer.Write(determinism.Length);
        writer.Write(determinism);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ContentHashListWithDeterminism"/> struct from its binary
    ///     representation.
    /// </summary>
    public static ContentHashListWithDeterminism Deserialize(BuildXLReader reader)
    {
        var writeContentHashList = reader.ReadBoolean();
        var contentHashList = writeContentHashList ? ContentHashList.Deserialize(reader) : null;

        var length = reader.ReadInt32();
        var determinismBytes = reader.ReadBytes(length);
        var determinism = CacheDeterminism.Deserialize(determinismBytes);

        return new ContentHashListWithDeterminism(contentHashList, determinism);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ContentHashListWithDeterminism"/> struct from its binary
    ///     representation.
    /// </summary>
    public static ContentHashListWithDeterminism Deserialize(ref SpanReader reader)
    {
        var writeContentHashList = reader.ReadBoolean();
        var contentHashList = writeContentHashList ? ContentHashList.Deserialize(ref reader) : null;

        var length = reader.ReadInt32();
        var determinismBytes = reader.ReadBytes(length);
        var determinism = CacheDeterminism.Deserialize(determinismBytes);

        return new ContentHashListWithDeterminism(contentHashList, determinism);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"ContentHashList=[{ContentHashList}], Determinism={Determinism}";
    }

    /// <nodoc />
    public string? ToTraceString()
    {
        if (ContentHashList == null)
        {
            return null;
        }

        var hashes = ContentHashList.Hashes;
        return $"Determinism=[{Determinism}] Count={hashes.Count}" + (hashes.Count != 0 ? $" FirstHash={hashes[0]}" : string.Empty);
    }
}
