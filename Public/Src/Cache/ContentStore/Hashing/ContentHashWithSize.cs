// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Cache.ContentStore.Hashing;

/// <summary>
/// Pairing of content hash and size of the corresponding content.
/// </summary>
public readonly record struct ContentHashWithSize(ContentHash Hash, long Size)
{
    /// <nodoc />
    public static implicit operator ContentHash(ContentHashWithSize hashWithSize)
    {
        return hashWithSize.Hash;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[ContentHash={Hash} Size={Size}]";
    }
}
