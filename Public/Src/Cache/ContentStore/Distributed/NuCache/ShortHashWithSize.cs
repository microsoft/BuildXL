// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache;

/// <summary>
/// Pairing of content hash and size of the corresponding content.
/// </summary>
public readonly record struct ShortHashWithSize(ShortHash Hash, long Size)
{
    /// <nodoc />
    public static implicit operator ShortHashWithSize(ContentHashWithSize value)
    {
        return new ShortHashWithSize(value.Hash, value.Size);
    }

    /// <nodoc />
    public static implicit operator ShortHashWithSize((ShortHash hash, long size) value)
    {
        return new ShortHashWithSize(value.hash, value.size);
    }

    /// <nodoc />
    public static implicit operator ShortHashWithSize((ContentHash hash, long size) value)
    {
        return new ShortHashWithSize(value.hash, value.size);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[ContentHash={Hash} Size={Size}]";
    }
}
