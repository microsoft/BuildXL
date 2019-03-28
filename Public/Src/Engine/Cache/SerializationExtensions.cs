// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Storage;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Extension methods for serializing fingerprints
    /// </summary>
    public static class SerializationExtensions
    {
        /// <summary>
        /// Write the weak fingerprint
        /// </summary>
        public static void Write(this BinaryWriter writer, WeakContentFingerprint fingerprint)
        {
            writer.Write(fingerprint.Hash);
        }

        /// <summary>
        /// Write the strong fingerprint
        /// </summary>
        public static void Write(this BinaryWriter writer, StrongContentFingerprint fingerprint)
        {
            writer.Write(fingerprint.Hash);
        }

        /// <summary>
        /// Write the content hash
        /// </summary>
        public static void Write(this BinaryWriter writer, ContentHash hash)
        {
            Contract.Requires(hash.HashType == ContentHashingUtilities.HashInfo.HashType);
            hash.SerializeHashBytes(writer);
        }

        /// <summary>
        /// Reads the weak fingerprint
        /// </summary>
        public static WeakContentFingerprint ReadWeakFingerprint(this BinaryReader reader)
        {
            return new WeakContentFingerprint(ReadFingerprint(reader));
        }

        /// <summary>
        /// Reads the strong fingerprint
        /// </summary>
        public static StrongContentFingerprint ReadStrongFingerprint(this BinaryReader reader)
        {
            return new StrongContentFingerprint(ReadFingerprint(reader));
        }

        /// <summary>
        /// Reads the content hash
        /// </summary>
        public static ContentHash ReadContentHash(this BinaryReader reader)
        {
            return new ContentHash(ContentHashingUtilities.HashInfo.HashType, reader);
        }

        private static void Write(this BinaryWriter writer, Fingerprint fingerprint)
        {
            FingerprintUtilities.WriteTo(fingerprint, writer);
        }

        private static Fingerprint ReadFingerprint(this BinaryReader reader)
        {
            return FingerprintUtilities.CreateFrom(reader);
        }
    }
}
