// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using Google.Protobuf;
using static BuildXL.Utilities.Core.FormattableStringEx;

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
        public bool IsCorrupted => DataBlob.IsEmpty;

        /// <summary>
        /// Deserializes an <see cref="IPipFingerprintEntryData"/> of the appropriate concrete type (or null if the type is unknown).
        /// <see cref="PipFingerprintEntry"/> is a tagged polymorphic container (see <see cref="Kind"/>),
        /// and this operation unwraps it. The returned instance contains all relevant data for the entry.
        /// </summary>
        public IPipFingerprintEntryData Deserialize(CancellationToken cancellationToken, CacheQueryData cacheQueryData = null)
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
                IPipFingerprintEntryData deserialized;

                switch (Kind)
                {
                    case PipFingerprintEntryKind.DescriptorV2:
                        deserialized = CacheGrpcExtensions.Deserialize<PipCacheDescriptorV2Metadata>(DataBlob);
                        break;
                    case PipFingerprintEntryKind.GraphDescriptor:
                        deserialized = CacheGrpcExtensions.Deserialize<PipGraphCacheDescriptor>(DataBlob);
                        break;
                    case PipFingerprintEntryKind.FileDownload:
                        deserialized = CacheGrpcExtensions.Deserialize<FileDownloadDescriptor>(DataBlob);
                        break;
                    case PipFingerprintEntryKind.PackageDownload:
                        deserialized = CacheGrpcExtensions.Deserialize<PackageDownloadDescriptor>(DataBlob);
                        break;
                    case PipFingerprintEntryKind.GraphInputDescriptor:
                        deserialized = CacheGrpcExtensions.Deserialize<PipGraphInputDescriptor>(DataBlob);
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
                    // Include in the log the hash of this instance so that we can trace it in the cache log, and obtain the file in the CAS.
                    ContentHash valueHash = ContentHashingUtilities.HashBytes(this.ToByteArray());

                    const string Unspecified = "<Unspecified>";
                    string actualEntryBlob = Unspecified;
                    string actualEntryHash = Unspecified;

                    if (cacheQueryData != null && cacheQueryData.ContentCache != null)
                    {
                        var maybeActualContent = cacheQueryData.ContentCache.TryLoadContent(
                            cacheQueryData.MetadataHash,
                            cancellationToken,
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
        /// Helper for <see cref="IPipFingerprintEntryData.ToEntry"/>. Note that data must be serialized from protobuf message type.
        /// </summary>
        internal static PipFingerprintEntry CreateFromData(PipFingerprintEntryKind kind, ByteString data)
        {
            var entry = new PipFingerprintEntry
            {
                Kind = kind,
                DataBlob = data,
            };

            if (entry.IsCorrupted)
            {
                Logger.Log.SerializingToPipFingerprintEntryResultInCorruptedData(Events.StaticContext, entry.Kind.ToString(), entry.ToDataBlobString());

                // We don't expect this to happen, so throw an exception to crash the build.
                throw new InvalidDataException(I($"Serializing '{kind.ToString()}' results in corrupted {nameof(PipFingerprintEntry)}"));
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
                // TODO [pgunasekara]: Add a cancellation token here
                // We are robust to Kind.Unknown here since this properties is sometimes used in string-ifying for e.g. logs.
                IPipFingerprintEntryData data = Deserialize(CancellationToken.None);
                return data?.Id ?? 0;
            }
        }

        /// <nodoc />
        public IEnumerable<ByteString> OutputContentHashes
        {
            get
            {
                // TODO [pgunasekara]: Add a cancellation token here
                // We are robust to Kind.Unknown here since this properties is sometimes used in string-ifying for e.g. logs.
                IPipFingerprintEntryData data = Deserialize(CancellationToken.None);
                return data == null ? CollectionUtilities.EmptyArray<ByteString>() : data.ListRelatedContent();
            }
        }

        /// <summary>
        /// Gets the string representation of the data blob.
        /// </summary>
        private string ToDataBlobString()
        {
            return Kind + "(" + ToByteArrayString(DataBlob.ToByteArray()) + ")";
        }

        private static string ToByteArrayString(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}
