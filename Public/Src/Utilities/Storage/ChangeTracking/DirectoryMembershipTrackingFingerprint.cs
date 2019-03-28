// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Fingerprint (hash) of an order-independent set of filenames with partial metadata. This can be used to compare the membership in a directory over time
    /// (i.e., has the set returned by <c>FindFirstFile</c> / <c>FindNextFile</c> changed?)
    /// </summary>
    /// <remarks>
    /// We assume that the set of filenames accumulated is unique, and more strongly that the set of *case normalized* immediate children is unique.
    /// </remarks>
    public readonly struct DirectoryMembershipTrackingFingerprint : IEquatable<DirectoryMembershipTrackingFingerprint>
    {
        /// <summary>
        /// Underlying hash (fully represents the fingerprint).
        /// </summary>
        public ContentHash Hash { get; }

        /// <summary>
        /// SHA-1 of all zeros to use instead of default(DirectoryMembershipTrackingFingerprint)
        /// </summary>
        public static readonly DirectoryMembershipTrackingFingerprint Zero =
            new DirectoryMembershipTrackingFingerprint(ContentHashingUtilities.ZeroHash);

        /// <summary>
        /// Special fingerprint indicating absent directory.
        /// </summary>
        public static readonly DirectoryMembershipTrackingFingerprint Absent =
            new DirectoryMembershipTrackingFingerprint(ContentHashingUtilities.CreateSpecialValue(1));

        /// <summary>
        /// Special fingerprint indicating that a directory was enumerated multiple times with different contents.
        /// </summary>
        /// <remarks>
        /// <see cref="FileChangeTrackingSet"/> uses this to force an enumeration invalidation on the next scan (there is no one
        /// fingerprint at which the directory could be silently re-tracked).
        /// </remarks>
        public static readonly DirectoryMembershipTrackingFingerprint Conflict =
            new DirectoryMembershipTrackingFingerprint(ContentHashingUtilities.CreateSpecialValue(2));

        /// <nodoc />
        public DirectoryMembershipTrackingFingerprint(ContentHash hash)
        {
            Hash = hash;
        }

        /// <summary>
        /// Creates a fingerprint calculator required for constructing the directory fingerprint.
        /// </summary>
        public static DirectoryMembershipTrackingFingerprintCalculator CreateCalculator()
        {
            return new DirectoryMembershipTrackingFingerprintCalculator(MurmurHash3.Zero);
        }

        /// <summary>
        /// Tests if the <paramref name="other" /> content fingerprint represents the same hash.
        /// </summary>
        public bool Equals(DirectoryMembershipTrackingFingerprint other)
        {
            return Hash.Equals(other.Hash);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Hash.ToHex();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(DirectoryMembershipTrackingFingerprint left, DirectoryMembershipTrackingFingerprint right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(DirectoryMembershipTrackingFingerprint left, DirectoryMembershipTrackingFingerprint right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Computes a directory membership fingerprint for a given set of directories.
        /// </summary>
        public readonly struct DirectoryMembershipTrackingFingerprintCalculator : IEquatable<DirectoryMembershipTrackingFingerprintCalculator>
        {
            private readonly MurmurHash3 m_currentHash;

            /// <nodoc />
            public DirectoryMembershipTrackingFingerprintCalculator(MurmurHash3 currentHash)
            {
                m_currentHash = currentHash;
            }

            /// <summary>
            /// Computes the hash for a given <paramref name="fileName"/> and <paramref name="attributes"/> and combines it with the current hash.
            /// </summary>
            public DirectoryMembershipTrackingFingerprintCalculator Accumulate(string fileName, FileAttributes attributes)
            {
                var hash = HashFileNameAndAttributes(fileName, attributes);
                if (m_currentHash.IsZero)
                {
                    return new DirectoryMembershipTrackingFingerprintCalculator(hash);
                }
                else
                {
                    return new DirectoryMembershipTrackingFingerprintCalculator(m_currentHash.CombineOrderIndependent(hash));
                }
            }

            /// <summary>
            /// Gets the directory fingerprint.
            /// </summary>
            public DirectoryMembershipTrackingFingerprint GetFingerprint()
            {
                return m_currentHash.IsZero ? Zero : new DirectoryMembershipTrackingFingerprint(ContentHashingUtilities.CreateFrom(m_currentHash));
            }

            /// <summary>
            /// Tests if the <paramref name="other" /> content fingerprint represents the same hash.
            /// </summary>
            public bool Equals(DirectoryMembershipTrackingFingerprintCalculator other)
            {
                return m_currentHash.Equals(other.m_currentHash);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return m_currentHash.GetHashCode();
            }

            /// <inheritdoc />
            public override string ToString()
            {
                return m_currentHash.ToString();
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return StructUtilities.Equals(this, obj);
            }

            /// <nodoc />
            public static bool operator ==(DirectoryMembershipTrackingFingerprintCalculator left, DirectoryMembershipTrackingFingerprintCalculator right)
            {
                return left.Equals(right);
            }

            /// <nodoc />
            public static bool operator !=(DirectoryMembershipTrackingFingerprintCalculator left, DirectoryMembershipTrackingFingerprintCalculator right)
            {
                return !left.Equals(right);
            }

            /// <summary>
            /// Computes the hash for a given <paramref name="name"/> and <paramref name="attributes"/>.
            /// </summary>
            private unsafe static MurmurHash3 HashFileNameAndAttributes(string name, FileAttributes attributes)
            {
                string caseNormalized = name.ToUpperInvariant();
                uint encodedLength = (uint)Encoding.Unicode.GetMaxByteCount(caseNormalized.Length);
                uint seed = (attributes & FileAttributes.Directory) == 0 ? (byte)0 : (byte)1;
                unsafe
                {
                    fixed (char* charPointer = caseNormalized)
                    {
                        byte* bytePointer = (byte*)charPointer;
                        return MurmurHash3.Create(bytePointer, encodedLength, seed);
                    }
                }
            }
        }
    }
}
