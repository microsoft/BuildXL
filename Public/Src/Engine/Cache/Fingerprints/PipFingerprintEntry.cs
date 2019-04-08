// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using Bond;
using Bond.IO.Safe;
using Bond.Protocols;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Cache.Fingerprints
{
#pragma warning disable CA1067 // Override Object.Equals(object) when implementing IEquatable<T>
    /// <nodoc />
    public partial class PipFingerprintEntry : IEquatable<PipFingerprintEntry>
#pragma warning restore CA1067 // Override Object.Equals(object) when implementing IEquatable<T>
    {
        /// <summary>
        /// Per-thread RNG for unique ID generation.
        /// </summary>
        private static readonly ThreadLocal<Random> s_threadLocalRandom = new ThreadLocal<Random>(
            () => new Random(
                HashCodeHelper.Combine(Environment.TickCount, Thread.CurrentThread.ManagedThreadId)));

        private IPipFingerprintEntryData m_deserializedEntryData;

        /// <summary>
        /// Max number of retries on loading and deserializing <see cref="PipFingerprintEntry"/> from cache.
        /// </summary>
        public const int LoadingAndDeserializingRetries = 3;

        /// <summary>
        /// Generates a unique identifier suitable for <see cref="IPipFingerprintEntryData.Id"/>
        /// </summary>
        public static ulong CreateUniqueId()
        {
            Random r = s_threadLocalRandom.Value;
            return Bits.GetLongFromInts(
                unchecked((uint)r.Next(int.MinValue, int.MaxValue)),
                unchecked((uint)r.Next(int.MinValue, int.MaxValue)));
        }

        /// <summary>
        /// Returns true if the data blob is corrupted.
        /// </summary>
        public bool IsCorrupted => DataBlob.Count == 0 || DataBlob.Array[DataBlob.Offset] == 0;

        /// <summary>
        /// Deserializes an <see cref="IPipFingerprintEntryData"/> of the appropriate concrete type (or null if the type is unknown).
        /// <see cref="PipFingerprintEntry"/> is a tagged polymorphic container (see <see cref="Kind"/>),
        /// and this operation unwraps it. The returned instance contains all relevant data for the entry.
        /// </summary>
        public IPipFingerprintEntryData Deserialize(CacheQueryData cacheQueryData = null)
        {
            if (Kind == PipFingerprintEntryKind.Unknown)
            {
                return null;
            }

            if (m_deserializedEntryData != null)
            {
                return m_deserializedEntryData;
            }

            try
            {
                InputBuffer buffer = new InputBuffer(DataBlob);
                CompactBinaryReader<InputBuffer> reader = new CompactBinaryReader<InputBuffer>(buffer);
                IPipFingerprintEntryData deserialized;

                switch (Kind)
                {
                    case PipFingerprintEntryKind.DescriptorV1:
                        deserialized = Deserialize<PipCacheDescriptor>.From(reader);
                        break;
                    case PipFingerprintEntryKind.DescriptorV2:
                        deserialized = Deserialize<PipCacheDescriptorV2Metadata>.From(reader);
                        break;
                    case PipFingerprintEntryKind.GraphDescriptor:
                        deserialized = Deserialize<PipGraphCacheDescriptor>.From(reader);
                        break;
                    case PipFingerprintEntryKind.FileDownload:
                        deserialized = Deserialize<FileDownloadDescriptor>.From(reader);
                        break;
                    case PipFingerprintEntryKind.PackageDownload:
                        deserialized = Deserialize<PackageDownloadDescriptor>.From(reader);
                        break;
                    case PipFingerprintEntryKind.GraphInputDescriptor:
                        deserialized = Deserialize<PipGraphInputDescriptor>.From(reader);
                        break;
                    default:
                        throw Contract.AssertFailure("Unhandled PipFingerprintEntryKind");
                }

                Interlocked.CompareExchange(ref m_deserializedEntryData, deserialized, comparand: null);
                return Volatile.Read(ref m_deserializedEntryData);
            }
            catch (Exception exception)
            {
                if (IsCorrupted)
                {
                    OutputBuffer valueBuffer = new OutputBuffer(1024);
                    CompactBinaryWriter<OutputBuffer> writer = new CompactBinaryWriter<OutputBuffer>(valueBuffer);
                    Serialize.To(writer, this);

                    // Include in the log the hash of this instance so that we can trace it in the cache log, and obtain the file in the CAS.
                    ContentHash valueHash = ContentHashingUtilities.HashBytes(
                        valueBuffer.Data.Array,
                        valueBuffer.Data.Offset,
                        valueBuffer.Data.Count);

                    const string Unspecified = "<Unspecified>";
                    string actualEntryBlob = Unspecified;
                    string actualEntryHash = Unspecified;

                    if (cacheQueryData != null && cacheQueryData.ContentCache != null)
                    {
                        var maybeActualContent = cacheQueryData.ContentCache.TryLoadContent(
                            cacheQueryData.MetadataHash, 
                            failOnNonSeekableStream: true, 
                            byteLimit: 20 * 1024 * 1024).Result;

                        if (maybeActualContent.Succeeded && maybeActualContent.Result != null)
                        {
                            actualEntryBlob = ToByteArrayString(maybeActualContent.Result);
                            actualEntryHash = ContentHashingUtilities.HashBytes(maybeActualContent.Result).ToString();
                        }
                    }

                    Logger.Log.DeserializingCorruptedPipFingerprintEntry(
                        Events.StaticContext,
                        kind: Kind.ToString(),
                        weakFingerprint: cacheQueryData?.WeakContentFingerprint.ToString() ?? Unspecified,
                        pathSetHash: cacheQueryData?.PathSetHash.ToString() ?? Unspecified,
                        strongFingerprint: cacheQueryData?.StrongContentFingerprint.ToString() ?? Unspecified,
                        expectedHash: cacheQueryData?.MetadataHash.ToString() ?? Unspecified, // expected metadata hash
                        hash: valueHash.ToString(),                                           // re-computed hash
                        blob: ToDataBlobString(),                                             // blob of data carried by pip fingerprint entry
                        actualHash: actualEntryHash,                                          // actual pip fingerprint entry hash
                        actualEntryBlob: actualEntryBlob);                                    // actual pip fingerprint entry blob

                    throw new BuildXLException("Deserializing corrupted pip fingerprint entry", exception, ExceptionRootCause.CorruptedCache);
                }

                // We don't expect this to happen so rethrow the exception.
                throw;
            }
        }

        /// <summary>
        /// Helper for <see cref="IPipFingerprintEntryData.ToEntry"/>. Note that <typeparamref name="T"/> must
        /// be a concrete type known to Bond.
        /// </summary>
        internal static PipFingerprintEntry CreateFromData<T>(T data) where T : IPipFingerprintEntryData
        {
            // TODO: It would be nice to predict the buffer size accurately.
            OutputBuffer buffer = new OutputBuffer(1024);
            CompactBinaryWriter<OutputBuffer> writer = new CompactBinaryWriter<OutputBuffer>(buffer);

            Serialize.To(writer, data);

            var entry = new PipFingerprintEntry
                        {
                            Kind = data.Kind,
                            DataBlob = buffer.Data,
                        };

            if (entry.IsCorrupted)
            {
                Logger.Log.SerializingToPipFingerprintEntryResultInCorruptedData(Events.StaticContext, entry.Kind.ToString(), entry.ToDataBlobString());

                // We don't expect this to happen, so throw an exception to crash the build.
                throw new InvalidDataException(I($"Serializing '{data.Kind.ToString()}' results in corrupted {nameof(PipFingerprintEntry)}"));
            }

            return entry;
        }

        /// <summary>
        /// Unique identifier
        /// </summary>
        public ulong UniqueIdentifier
        {
            get
            {
                // We are robust to Kind.Unknown here since this properties is sometimes used in string-ifying for e.g. logs.
                IPipFingerprintEntryData data = Deserialize();
                return data?.Id ?? 0;
            }
        }

        /// <nodoc />
        public IEnumerable<BondContentHash> OutputContentHashes
        {
            get
            {
                // We are robust to Kind.Unknown here since this properties is sometimes used in string-ifying for e.g. logs.
                IPipFingerprintEntryData data = Deserialize();
                return data == null ? CollectionUtilities.EmptyArray<BondContentHash>() : data.ListRelatedContent();
            }
        }

        /// <inheritdoc />
        public bool Equals(PipFingerprintEntry other)
        {
            return Equals((object)other);
        }

        /// <summary>
        /// Gets the string representation of the data blob.
        /// </summary>
        private string ToDataBlobString()
        {
            return Kind + "(" + ToByteArrayString(DataBlob.ToArray()) + ")";
        }

        private static string ToByteArrayString(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}
