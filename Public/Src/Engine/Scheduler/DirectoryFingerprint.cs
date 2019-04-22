// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// A fingerprint of the membership of a directory.
    /// </summary>
    /// <remarks>
    /// This fingerprint can be computed for any directory based on either its statically-known contents or contents discovered
    /// at runtime. This fingerprint accounts for 'membership' (i.e., the set of filenames, or set of metadata entries) but not
    /// the precise content of member files.
    /// </remarks>
    public struct DirectoryFingerprint : IEquatable<DirectoryFingerprint>
    {
        private readonly ContentHash m_hash;

        /// <summary>
        /// SHA-1 of all zeros to use instead of default(DirectoryFingerprint)
        /// </summary>
        public static readonly DirectoryFingerprint Zero = new DirectoryFingerprint(ContentHashingUtilities.ZeroHash);

        /// <summary>
        /// Creates a directory fingerprint from the given hash.
        /// The hash should have been constructed according to the definition of a directory fingerprint.
        /// </summary>
        public DirectoryFingerprint(ContentHash hash)
        {
            m_hash = hash;
        }

        /// <nodoc />
        public ContentHash Hash => m_hash;

        /// <summary>
        /// Tests if the <paramref name="other" /> fingerprint represents the same hash.
        /// </summary>
        public bool Equals(DirectoryFingerprint other)
        {
            return m_hash.Equals(other.m_hash);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_hash.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return m_hash.ToHex();
        }

        /// <nodoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(DirectoryFingerprint left, DirectoryFingerprint right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(DirectoryFingerprint left, DirectoryFingerprint right)
        {
            return !left.Equals(right);
        }
    }
}
