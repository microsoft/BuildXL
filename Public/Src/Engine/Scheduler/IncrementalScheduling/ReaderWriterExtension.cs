// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Storage.Fingerprints;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Reader/writer extension for structures/types used by incremental scheduling.
    /// </summary>
    internal static class ReaderWriterExtension
    {
        /// <summary>
        /// Writes <see cref="PipStableId"/>.
        /// </summary>
        internal static void Write(this BinaryWriter writer, PipStableId pipStableId) => writer.Write((int)pipStableId);

        /// <summary>
        /// Reads <see cref="PipStableId"/>.
        /// </summary>
        internal static PipStableId ReadPipStableId(this BinaryReader reader) => (PipStableId)reader.ReadInt32();

        /// <summary>
        /// Writes fingerprints.
        /// </summary>
        internal static void Write(this BinaryWriter writer, ContentFingerprint fingerprint, byte[] buffer)
        {
            Array.Clear(buffer, 0, buffer.Length);
            fingerprint.Hash.Serialize(buffer);
            writer.Write(buffer);
        }

        /// <summary>
        /// Reads fingerprints.
        /// </summary>
        internal static ContentFingerprint ReadContentFingerprint(this BinaryReader reader, byte[] buffer)
        {
            Array.Clear(buffer, 0, buffer.Length);
            reader.Read(buffer, 0, FingerprintUtilities.FingerprintLength);
            return new ContentFingerprint(FingerprintUtilities.CreateFrom(buffer));
        }
    }
}
