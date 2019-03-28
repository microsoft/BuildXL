// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    /// Container for a individual memmber of BulkPlace call
    /// </summary>
    public readonly struct ContentHashWithPath : IEquatable<ContentHashWithPath>
    {
        /// <summary>
        /// Gets the content hash
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>
        /// Gets the path for placing file
        /// </summary>
        public AbsolutePath Path { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHashWithPath"/> struct.
        /// </summary>
        public ContentHashWithPath(ContentHash hash, AbsolutePath path)
        {
            Hash = hash;
            Path = path;
        }

        /// <inheritdoc />
        public bool Equals(ContentHashWithPath other)
        {
            return Hash.Equals(other.Hash) && Path.Equals(other.Path);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Hash.GetHashCode() ^ Path.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(ContentHashWithPath left, ContentHashWithPath right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ContentHashWithPath left, ContentHashWithPath right)
        {
            return !left.Equals(right);
        }
    }
}
