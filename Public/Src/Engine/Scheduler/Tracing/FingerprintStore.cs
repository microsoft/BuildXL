// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using KVP = System.Collections.Generic.KeyValuePair<string, string>;
using PipKVP = System.Collections.Generic.KeyValuePair<string, BuildXL.Scheduler.Tracing.FingerprintStore.PipFingerprintKeys>;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Fingerprint store constants used by multiple classes.
    /// </summary>
    public static class FingerprintStoreConstants
    {
        /// <summary>
        /// The label for <see cref="BuildXL.Engine.Cache.Fingerprints.WeakContentFingerprint"/>s.
        /// </summary>
        public const string WeakFingerprint = "WeakFingerprint";

        /// <summary>
        /// The label for <see cref="BuildXL.Engine.Cache.Fingerprints.StrongContentFingerprint"/>s.
        /// </summary>
        public const string StrongFingerprint = "StrongFingerprint";

        /// <summary>
        /// The label for <see cref="PipCacheMissInfo.PipId"/>s.
        /// </summary>
        public const string PipId = "PI";

        /// <summary>
        /// The label for <see cref="PipCacheMissInfo.CacheMissType"/>s.
        /// </summary>
        public const string CacheMissType = "CMT";
    }

    /// <summary>
    /// Test hooks for fingerprint store.
    /// These are used to modify or retrieve private state.
    /// </summary>
    public class FingerprintStoreTestHooks
    {
        /// <summary>
        /// Provides ability to modify entry TTL for testing.
        /// </summary>
        public TimeSpan MaxEntryAge;

        /// <summary>
        /// Resulting counters from fingerprint store.
        /// </summary>
        public CounterCollection<FingerprintStoreCounters> Counters;

        /// <summary>
        /// Where minimal IO should be performed. This may omit log files.
        /// </summary>
        public bool MinimalIO;
    }

    /// <summary>
    /// Cache miss info stored by the <see cref="FingerprintStore"/> for analysis.
    /// </summary>
    public struct PipCacheMissInfo
    {
        /// <summary>
        /// The pip ID.
        /// </summary>
        public PipId PipId;

        /// <summary>
        /// The cause of the cache miss.
        /// </summary>
        public PipCacheMissType CacheMissType;

        /// <summary>
        /// Constructor.
        /// </summary>
        public PipCacheMissInfo(PipId pipId, PipCacheMissType cacheMissType)
        {
            PipId = pipId;
            CacheMissType = cacheMissType;
        }

        /// <summary>
        /// Serializes to binary.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(PipId.Value);
            writer.Write(Convert.ToByte(CacheMissType));
        }

        /// <summary>
        /// Deserializes from binary.
        /// </summary>
        public static PipCacheMissInfo Deserialize(BinaryReader reader)
        {
            return new PipCacheMissInfo(new PipId(reader.ReadUInt32()), (PipCacheMissType)reader.ReadByte());
        }
    }

    /// <summary>
    /// A <see cref="IBuildXLKeyValueStore"/> used for storing pip fingerprint inputs build-over-build.
    /// Encapsulates the logic for reading fingerprint store entries serialized by <see cref="FingerprintStoreExecutionLogTarget"/>.
    /// </summary>
    public class FingerprintStore : IDisposable
    {
        /// <summary>
        /// Version for breaking changes in fingerprint store format (format of entries or where entries are being stored).
        /// The versioning should be updated whenever data that was stored in previous runs will mismatch
        /// data stored in new runs. Adding completely new data types to the fingerprint store may not need version incrementing.
        /// </summary>
        public enum FormatVersion
        {
            /// <summary>
            /// Invalid
            /// </summary>
            Invalid = 0,

            /// <summary>
            /// The version changes whenever the fingerprint store format changes or the 
            /// <see cref="BuildXL.Scheduler.Fingerprints.PipFingerprintingVersion"/> changes
            /// since a new fingerprint version inherently changes the contents of a fingerprint and what is stored in the fingerprint entry.
            /// 
            /// IMPORTANT: These identifiers must only always increase and never overlap with a prior value.
            /// </summary>
            /// <remarks>
            /// A change in the version number will cause the entire previous fingerprint store to be deleted.
            /// </remarks>
            Version = 8 + Fingerprints.PipFingerprintingVersion.TwoPhaseV2,
        }

        /// <summary>
        /// Encompasses all the components for a strong fingerprint entry.
        /// Parts of the entry are stored separately in the store, but conceptually the form
        /// one strong fingerprint entry.
        /// </summary>
        public struct StrongFingerprintEntry
        {
            /// <summary>
            /// { strong fingerprint hash: strong fingerprint inputs }.
            /// </summary>
            public KVP StrongFingerprintToInputs;

            /// <summary>
            /// { path set hash : path set inputs }.
            /// </summary>
            public KVP PathSetHashToInputs;
        }

        /// <summary>
        /// Encompasses all the components for a full fingerprint store entry for a single pip.
        /// Parts of the entry are stored separately in the store, but conceptually they form
        /// one entry.
        /// </summary>
        public class FingerprintStoreEntry
        {
            /// <summary>
            /// { pip semistable hash : pip fingerprint keys }.
            /// </summary>
            public PipKVP PipToFingerprintKeys;

            /// <summary>
            /// { weak fingerprint hash : weak fingerprint inputs }.
            /// </summary>
            public KVP WeakFingerprintToInputs;

            /// <summary>
            /// The relevant <see cref="StrongFingerprintEntry"/>.
            /// </summary>
            public StrongFingerprintEntry StrongFingerprintEntry = default;

            /// <summary>
            /// Returns a list of all the string key-value pairs with a JSON value.
            /// </summary>
            public KVP[] GetJsonFields()
            {
                return new KVP[]
                {
                    WeakFingerprintToInputs,
                    StrongFingerprintEntry.StrongFingerprintToInputs,
                    StrongFingerprintEntry.PathSetHashToInputs,
                };
            }

            /// <summary>
            /// Writes the <see cref="FingerprintStoreEntry"/> components directly into a <see cref="TextWriter"/>.
            /// This can help writing to a file for long fingerprints which hit an <see cref="OutOfMemoryException"/> when
            /// all components are appended together in memory, but can be printed separately successfully.
            /// </summary>
            public void Print(TextWriter writer)
            {
                writer.WriteLine(PipToFingerprintKeys.ToString());

                foreach (var field in GetJsonFields())
                {
                    var prettyField = PrettyFormatJsonField(field);
                    writer.WriteLine(prettyField.ToString());
                }
            }

            /// <inheritdoc />
            /// <remarks>
            /// Use <see cref="Print"/> for writing to files.
            /// </remarks>
            public override string ToString()
            {
                using (var sbPool = Pools.GetStringBuilder())
                {
                    var writer = sbPool.Instance;
                    writer.AppendLine(PipToFingerprintKeys.ToString());

                    foreach (var field in GetJsonFields())
                    {
                        var prettyField = PrettyFormatJsonField(field);
                        writer.AppendLine(prettyField.ToString());
                    }

                    return writer.ToString();
                }
            }
        }

        /// <summary>
        /// Names of metadata entries.
        /// </summary>
        private readonly struct MetadataNames
        {
            /// <summary>
            /// Name of entry for the cache miss list.
            /// </summary>
            public static readonly byte[] CacheMissList = System.Text.Encoding.UTF8.GetBytes("CacheMissListV2");

            /// <summary>
            /// Name of entry for the map of 
            /// { entry key : timestamp of build where entry was last touched }.
            /// </summary>
            public static readonly byte[] LruEntriesMap = System.Text.Encoding.UTF8.GetBytes("LruEntriesMapV2");
        }

        /// <summary>
        /// Known names of the JSON properties that will hold the keys
        /// for fingerprint store lookups for each of the fields of a
        /// <see cref="FingerprintStoreEntry"/>.
        /// </summary>
        private readonly struct PropertyNames
        {
            public const string WeakFingerprint = FingerprintStoreConstants.WeakFingerprint;

            public const string StrongFingerprint = FingerprintStoreConstants.StrongFingerprint;

            public const string PathSet = ObservedPathEntryConstants.PathSet;

            public const string DirectoryEnumeration = ObservedInputConstants.DirectoryEnumeration;
        }

        /// <summary>
        /// The values for keys needed to lookup most of the sub-components of a fingerprint for a pip.
        /// Directory membership fingerprint keys are excluded here because they are serialized and stored
        /// separately from the rest of the fingerprint.
        /// </summary>
        public struct PipFingerprintKeys
        {
            /// <summary>
            /// String used to represent strong fingerprint.
            /// </summary>
            public string WeakFingerprint;

            /// <summary>
            /// String used to represent strong fingerprint.
            /// </summary>
            public string StrongFingerprint;

            /// <summary>
            /// String used to represent path set hash used in strong fingerprint.
            /// It is up to the caller to decide the string representation for this field.
            /// </summary>
            public string FormattedPathSetHash;

            /// <summary>
            /// Constructor for convenience.
            /// </summary>
            public PipFingerprintKeys(
                WeakContentFingerprint weakFingerprint,
                StrongContentFingerprint strongFingerprint,
                string pathSetHash)
            {
                WeakFingerprint = weakFingerprint.ToString();
                StrongFingerprint = strongFingerprint.ToString();
                FormattedPathSetHash = pathSetHash;
            }

            /// <inheritdoc />
            public override string ToString()
            {
                var keys = this;
                return JsonFingerprinter.CreateJsonString(writer =>
                {
                    writer.Add(PropertyNames.WeakFingerprint, keys.WeakFingerprint);
                    writer.Add(PropertyNames.StrongFingerprint, keys.StrongFingerprint);
                    writer.Add(PropertyNames.PathSet, keys.FormattedPathSetHash);
                },
                formatting: Newtonsoft.Json.Formatting.Indented);
            }
        }

        /// <summary>
        /// Names of column families.
        /// </summary>
        private readonly struct ColumnNames
        {
            /// <summary>
            /// Default column for <see cref="PipFingerprintKeys"/>, keyed by <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
            /// </summary>
            /// <remarks>
            /// Note that this does NOT need to be kept in-sync with the underlying key value stores name for its default column. 
            /// This is just for the <see cref="FingerprintStore"/> to represent the default column to when managing some state.
            /// All <see cref="KeyValueStoreAccessor"/> operations can access the default column by passing null.
            /// </remarks>
            public static readonly string Default = KeyValueStoreAccessor.DefaultColumnName;

            /// <summary>
            /// Column for weak fingerprints, keyed by <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
            /// </summary>
            public const string WeakFingerprints = "WeakFingerprints";

            /// <summary>
            /// Column for strong fingerprints, keyed by <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
            /// </summary>
            public const string StrongFingerprints = "StrongFingerprints";

            /// <summary>
            /// Column for directory membership fingerprint and path set hash inputs, keyed on the respective content hashes.
            /// </summary>
            public const string ContentHashes = "ContentHashes";

            /// <summary>
            /// Column for <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>es, keyed on the pip unique output hash computed by
            /// <see cref="BuildXL.Pips.Operations.Process.TryComputePipUniqueOutputHash(PathTable, out long, PathExpander)"/>.
            /// </summary>
            public const string PipUniqueOutputHashes = "PipUniqueOutputHashes";

            /// <summary>
            /// Convenience array for iterating through all the columns except the default,
            /// which requires special handling.
            /// </summary>
            public static readonly string[] ListAll = new string[]
            {
                Default,
                WeakFingerprints,
                StrongFingerprints,
                ContentHashes,
                PipUniqueOutputHashes,
            };
        }

        /// <summary>
        /// Convenience array of additional key-tracked column families.
        /// 
        /// Any column that is iterated through for garbage collection should be included here.
        /// Calls to <see cref="IKeyValueStore{TKey, TValue}.Contains(TKey, string)"/> are more efficient
        /// for columns that are key-tracked.
        /// </summary>
        private static readonly string[] s_additionalKeyTrackedColumns = new string[]
        {
            ColumnNames.ContentHashes,
            ColumnNames.PipUniqueOutputHashes,
        };

        /// <summary>
        /// Convenience array of additional column families.
        /// </summary>
        private static readonly string[] s_additionalColumns = new string[]
        {
            ColumnNames.WeakFingerprints,
            ColumnNames.StrongFingerprints,
        };

        /// <summary>
        /// Date time format when serializing or parsing date times for the fingerprint store.
        /// </summary>
        private const string DateTimeFormat = "u";

        /// <summary>
        /// Time-to-live for an entry is 3 days unless otherwise specified (a get or a put renews the TTL).
        /// </summary>
        private readonly TimeSpan m_maxEntryAge = TimeSpan.FromDays(3);

        /// <summary>
        /// How long garbage collection can run before being cancelled.
        /// </summary>
        private readonly TimeSpan m_garbageCollectionTimeLimit = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Provides classes the opportunity to cancel garbage collect due to factors external to the <see cref="FingerprintStore"/>.
        /// </summary>
        public CancellationTokenSource GarbageCollectCancellationToken { get; } = new CancellationTokenSource();

        /// <summary>
        /// Extends a <see cref="List{T}"/> to represent the cache miss list.
        /// Internal for testing.
        /// </summary>
        internal class CacheMissList : List<PipCacheMissInfo>
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            public CacheMissList(int count = 0) : base(count) { }

            /// <summary>
            /// Serializes any <see cref="ICollection{PipCacheMissInfo}"/> to binary that can be deserialized as a <see cref="CacheMissList"/>.
            /// </summary>
            public static void Serialize(BinaryWriter writer, ICollection<PipCacheMissInfo> cacheMissList)
            {
                writer.Write(cacheMissList.Count);
                foreach (var miss in cacheMissList)
                {
                    miss.Serialize(writer);
                }
            }

            /// <summary>
            /// Deserializes from binary.
            /// </summary>
            public static CacheMissList Deserialize(BinaryReader reader)
            {
                var count = reader.ReadInt32();
                var cacheMissList = new CacheMissList(count);
                for (var i = 0; i < count; ++i)
                {
                    cacheMissList.Add(PipCacheMissInfo.Deserialize(reader));
                }

                return cacheMissList;
            }
        }

        /// <summary>
        /// Extends a <see cref="Dictionary{TKey, TValue}"/> to represent a map of { entry key : timestamp in ticks }.
        /// Internal for testing.
        /// </summary>
        internal class LruEntriesMap : Dictionary<string, long>
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="count"></param>
            public LruEntriesMap(int count = 0) : base(count) { }

            /// <summary>
            /// Serializes to binary.
            /// </summary>
            /// <param name="writer"></param>
            public void Serialize(BinaryWriter writer)
            {
                writer.Write(Count);
                foreach (var kvp in this)
                {
                    writer.Write(kvp.Key);
                    writer.Write(kvp.Value);
                }
            }

            /// <summary>
            /// Deserializes from binary.
            /// </summary>
            public static LruEntriesMap Deserialize(BinaryReader reader)
            {
                var count = reader.ReadInt32();
                var lruEntriesMap = new LruEntriesMap(count);
                for (var i = 0; i < count; ++i)
                {
                    var key = reader.ReadString();
                    var value = reader.ReadInt64();
                    lruEntriesMap.Add(key, value);
                }

                return lruEntriesMap;
            }
        }

        /// <summary>
        /// Helper class for tracking, by column, which entries were queried or put during a build for LRU garbage collection.
        /// This class is exclusively for the <see cref="FingerprintStore"/>.
        /// </summary>
        private class LruEntryTracker
        {
            /// <summary>
            /// Map from { column family name : set of entries tracked, identified by entry key }.
            /// <see cref="ConcurrentDictionary{TKey,TValue}"/> with true always as the value is used as
            /// the set of entries tracked since there is no concurrent HashSet. 
            /// </summary>
            /// <remarks>
            /// This is an <see cref="IReadOnlyDictionary{TKey, TValue}"/> for thread-safety.
            /// </remarks>
            private readonly IReadOnlyDictionary<string, ConcurrentDictionary<string, bool>> m_lruMaps;

            /// <summary>
            /// The key used for null column family names since dictionaries can't handle null keys.
            /// </summary>
            private const string NullKey = "null";

            /// <summary>
            /// Provides access to the sets of entry tracked for this build for a column.
            /// </summary>
            public ICollection<string> this[string columnName] => m_lruMaps[columnName ?? NullKey].Keys;

            /// <summary>
            /// Private access to the tracked entries for a column that can be mutated.
            /// </summary>
            private ConcurrentDictionary<string, bool> Get(string columnName)
            {
                return m_lruMaps[columnName ?? NullKey];
            }

            /// <summary>
            /// Constructor.
            /// </summary>
            public LruEntryTracker()
            {
                var lruMaps = new Dictionary<string, ConcurrentDictionary<string, bool>>();
                foreach (var column in ColumnNames.ListAll)
                {
                    lruMaps.Add(column ?? NullKey, new ConcurrentDictionary<string, bool>());
                }

                m_lruMaps = lruMaps;
            }

            /// <summary>
            /// Marks all the entries related to one specific pip as tracked.
            /// Tracked entries will have their entry age renewed at the end of the build.
            /// </summary>
            /// <remarks>
            /// All the entries tracked in this function are conceptually one fingerprint store entry, but are stored as separate
            /// entries in the fingerprint store (to de-dupe path set entries).
            /// 
            /// Because the entries together represent one complete fingerprint, the TTLs should be renewed together.
            /// </remarks>
            public void TrackFingerprintStoreEntry(string pipFormattedSemiStableHash, PipFingerprintKeys pfk)
            {
                Get(ColumnNames.Default).AddOrUpdate(pipFormattedSemiStableHash, true, (k, v) => true);
                Get(ColumnNames.WeakFingerprints).AddOrUpdate(pipFormattedSemiStableHash, true, (k, v) => true);
                Get(ColumnNames.StrongFingerprints).AddOrUpdate(pipFormattedSemiStableHash, true, (k, v) => true);
                Get(ColumnNames.ContentHashes).AddOrUpdate(pfk.FormattedPathSetHash, true, (k, v) => true);
            }

            /// <summary>
            /// Marks a content hash entry as tracked.
            /// Tracked entries will have their entry age renewed at the end of the build.
            /// </summary>
            /// <param name="contentHash"></param>
            public void TrackContentHashEntry(string contentHash)
            {
                Get(ColumnNames.ContentHashes).AddOrUpdate(contentHash, true, (k, v) => true);
            }

            /// <summary>
            /// Marks a pip unique output hash entry as tracked.
            /// Tracked entries will have their entry age at the end of their build.
            /// </summary>
            public void TrackPipUniqueOutputHashEntry(string semiStableOutputHash)
            {
                Get(ColumnNames.PipUniqueOutputHashes).AddOrUpdate(semiStableOutputHash, true, (k, v) => true);
            }
        }

        private readonly LruEntryTracker m_lruEntryTracker;

        /// <summary>
        /// Counters, shared with <see cref="FingerprintStoreExecutionLogTarget"/>.
        /// </summary>
        public CounterCollection<FingerprintStoreCounters> Counters { get; }

        /// <summary>
        /// Test hooks.
        /// </summary>
        private readonly FingerprintStoreTestHooks m_testHooks;

        /// <summary>
        /// Logging context.
        /// </summary>
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Provides access to the fingerprint store.
        /// </summary>
        private KeyValueStoreAccessor Accessor { get; set; }

        /// <summary>
        /// Whether the fingerprint store can still be accessed.
        /// </summary>
        public bool Disabled => Accessor.Disabled;

        /// <summary>
        /// Store directory.
        /// </summary>
        public string StoreDirectory => Accessor.StoreDirectory;

        /// <summary>
        /// Version of the store opened.
        /// </summary>
        public int StoreVersion => Accessor.StoreVersion;

        /// <summary>
        /// <see cref="FingerprintStoreMode"/>.
        /// </summary>
        private readonly FingerprintStoreMode m_mode = FingerprintStoreMode.Default;

        /// <summary>
        /// Opens or creates a new fingerprint store.
        /// </summary>
        /// <param name="storeDirectory">
        /// The directory of the fingerprint store.
        /// </param>
        /// <param name="readOnly">
        /// Whether the store should be opened read-only.
        /// </param>
        /// <param name="maxEntryAge">
        /// Optional max entry age of entries. Any entries older than this age will be garbage collected.
        /// If unset, the default is the constructed value at <see cref="m_maxEntryAge"/>.
        /// </param>
        /// <param name="loggingContext">
        /// Optional logging context to log failures.
        /// </param>
        /// <param name="mode">
        /// Optional <see cref="FingerprintStoreMode"/>.
        /// </param>
        /// <param name="counters">
        /// Optional <see cref="CounterCollection"/> of <see cref="FingerprintStoreCounters"/> to use 
        /// if sharing counters across objects. If not provided, counters will be created local to just this store.
        /// </param>
        /// <param name="testHooks">
        /// Optional test hooks.
        /// </param>
        public static Possible<FingerprintStore> Open(
            string storeDirectory,
            bool readOnly = false,
            TimeSpan? maxEntryAge = null,
            LoggingContext loggingContext = null,
            FingerprintStoreMode mode = FingerprintStoreMode.Default,
            CounterCollection<FingerprintStoreCounters> counters = null,
            FingerprintStoreTestHooks testHooks = null)
        {
            Contract.Requires(mode != FingerprintStoreMode.Invalid);

            Action<Failure> failureHandler = null;
            if (loggingContext != null)
            {
                failureHandler = (f) =>
                {
                    KeyValueStoreUtilities.CheckAndLogRocksDbException(f, loggingContext);
                    Logger.Log.FingerprintStoreFailure(loggingContext, f.DescribeIncludingInnerFailures());
                };
            }

            if (FileUtilities.Exists(storeDirectory))
            {
                var existingColumns = new HashSet<string>(KeyValueStoreAccessor.ListColumnFamilies(storeDirectory));
                foreach (var column in ColumnNames.ListAll)
                {
                    if (!existingColumns.Contains(column))
                    {
                        // To enable backwards compatability with older stores when a new column family is added,
                        // override read only requests with read-write to allow the new column to be created.
                        // The new column will be empty, but reads to the column will not throw exceptions.
                        readOnly = false;
                        break;
                    }
                }
            }

            var possibleAccessor = KeyValueStoreAccessor.OpenWithVersioning(
                storeDirectory,
                readOnly ? KeyValueStoreAccessor.IgnoreStoreVersion : (int)FormatVersion.Version, /* In read-only, allow attempts to read outdated stores */
                defaultColumnKeyTracked: true,
                additionalColumns: s_additionalColumns,
                additionalKeyTrackedColumns: s_additionalKeyTrackedColumns,
                failureHandler: failureHandler,
                openReadOnly: readOnly,
                onFailureDeleteExistingStoreAndRetry: true);

            if (possibleAccessor.Succeeded)
            {
                return new FingerprintStore(possibleAccessor.Result, maxEntryAge, mode, loggingContext, counters, testHooks);
            }
            else
            {
                return possibleAccessor.Failure;
            }
        }

        /// <summary>
        /// Opens an existing fingerprint store by creating a snapshot.
        /// </summary>
        public static Possible<FingerprintStore> CreateSnapshot(FingerprintStore store, LoggingContext loggingContext)
        {
            try
            {
                var accessor = new KeyValueStoreAccessor(store.Accessor);
                return new FingerprintStore(accessor, maxEntryAge: null, mode: store.m_mode, loggingContext: loggingContext, counters: null, testHooks: null);
            }
            catch (Exception ex)
            {
                return new Failure<Exception>(ex);
            }
        }

        private FingerprintStore(KeyValueStoreAccessor accessor, TimeSpan? maxEntryAge, FingerprintStoreMode mode, LoggingContext loggingContext, CounterCollection<FingerprintStoreCounters> counters, FingerprintStoreTestHooks testHooks)
        {
            Accessor = accessor;
            m_mode = mode;
            Counters = counters ?? new CounterCollection<FingerprintStoreCounters>();
            m_testHooks = testHooks;

            // Don't track or modify TTL of entries during read only sessions
            m_lruEntryTracker = Accessor.ReadOnly ? null : new LruEntryTracker();
            m_loggingContext = loggingContext;

            if (testHooks?.MaxEntryAge != null)
            {
                m_maxEntryAge = testHooks.MaxEntryAge;
            }
            else if (maxEntryAge.HasValue)
            {
                m_maxEntryAge = maxEntryAge.Value;
            }
        }

        /// <summary>
        /// Retrieves cache miss list from the store.
        /// </summary>
        public bool TryGetCacheMissList(out IReadOnlyList<PipCacheMissInfo> cacheMissList)
        {
            if (TryGetValueInternal(MetadataNames.CacheMissList, out var serializedList))
            {
                cacheMissList = serializedList.Length == 0
                    ? new CacheMissList()
                    : BinaryDeserialize(serializedList, reader => CacheMissList.Deserialize(reader), FingerprintStoreCounters.DeserializeCacheMissListTime);
                return true;
            }

            cacheMissList = null;
            return false;
        }

        /// <summary>
        /// Puts cache miss list used for the fingerprint store.
        /// </summary>
        public void PutCacheMissList(ICollection<PipCacheMissInfo> cacheMissList)
        {
            var serialized = cacheMissList.Count == 0
                ? new byte[0] // Only called once per build
                : BinarySerialize(writer => CacheMissList.Serialize(writer, cacheMissList), FingerprintStoreCounters.SerializeCacheMissListTime);

            PutInternal(MetadataNames.CacheMissList, serialized);
        }

        /// <summary>
        /// Retrieves entry from a column for a map from { entry key : timestamp of build where entry was last touched }.
        /// Internal for testing.
        /// </summary>
        internal bool TryGetLruEntriesMap(out LruEntriesMap lruEntriesMap, string columnFamilyName = null)
        {
            if (TryGetValueInternal(MetadataNames.LruEntriesMap, out var serializedMap, columnFamilyName))
            {
                lruEntriesMap = BinaryDeserialize(serializedMap, reader => LruEntriesMap.Deserialize(reader), FingerprintStoreCounters.DeserializeLruEntriesMapTime);
                return true;
            }

            lruEntriesMap = null;
            return false;
        }

        /// <summary>
        /// Puts an entry for a map from { entry key : timestamp of build where entry was last touched }.
        /// This helps track least recently used entries for garbage collection.
        /// Internal for testing.
        /// </summary>
        internal void PutLruEntriesMap(LruEntriesMap lruEntriesMap, string columnFamilyName = null)
        {
            if (lruEntriesMap.Count == 0)
            {
                return;
            }

            var serialized = BinarySerialize(writer => lruEntriesMap.Serialize(writer), FingerprintStoreCounters.SerializeLruEntriesMapsTime);
            PutInternal(MetadataNames.LruEntriesMap, serialized, columnFamilyName);
        }

        /// <summary>
        /// Helper function for serializing objects to binary.
        /// </summary>
        private byte[] BinarySerialize(Action<BinaryWriter> writeOps, FingerprintStoreCounters counter)
        {
            using (Counters.StartStopwatch(counter))
            {
                using (var pools = Pools.MemoryStreamPool.GetInstance())
                using (var writer = new BinaryWriter(pools.Instance, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writeOps(writer);
                    return pools.Instance.ToArray();
                }
            }
        }

        /// <summary>
        /// Helper function for deserializing objects from binary.
        /// </summary>
        private T BinaryDeserialize<T>(byte[] serialized, Func<BinaryReader, T> readOps, FingerprintStoreCounters counter)
        {
            using (Counters.StartStopwatch(counter))
            {
                using (var stream = new MemoryStream(serialized))
                using (var reader = new BinaryReader(stream))
                {
                    return readOps(reader);
                }
            }
        }

        /// <summary>
        /// Puts an entry for { pip unique output hash : pip formatted semi stable hash }.
        /// The pip unique output hash cannot be computed for pips with shared opaque directories.
        /// This entry enables primary lookup for a fingerprint store entry using the pip unique output hash, with a fallback to pip formatted semi stable hash.
        /// </summary>
        public void PutPipUniqueOutputHash(long pipUniqueOutputHash, string pipFormattedSemiStableHash)
        {
            Contract.Requires(!Accessor.ReadOnly);

            var hashString = pipUniqueOutputHash.ToString();
            PutInternal(hashString, pipFormattedSemiStableHash, ColumnNames.PipUniqueOutputHashes);

            m_lruEntryTracker?.TrackPipUniqueOutputHashEntry(hashString);
        }

        /// <summary>
        /// Puts an entry for a full fingerprint into the fingerprint store.
        /// </summary>
        /// <param name="entry">
        /// A <see cref="FingerprintStoreEntry"/> to place in the store.
        /// </param>
        /// <param name="storePathSet">
        /// Whether the path set component of the entry should be placed in the store.
        /// </param>
        /// <remarks>
        /// The path set component is a content-addressable key-value pair of { path set hash : path set hash inputs }.
        /// If there is known existing path set entry with the same key, unnecessary puts can be avoided by setting 
        /// storePathSet to false.
        /// </remarks>
        public void PutFingerprintStoreEntry(FingerprintStoreEntry entry, bool storePathSet = false)
        {
            Contract.Requires(!storePathSet || !string.IsNullOrEmpty(entry.StrongFingerprintEntry.PathSetHashToInputs.Key), "A non-empty path set hash must be provided to store a path set entry.");
            Contract.Requires(!Accessor.ReadOnly);

            Analysis.IgnoreResult(
                Accessor.Use(store =>
                {
                    store.Put(entry.PipToFingerprintKeys.Key, JsonSerialize(entry.PipToFingerprintKeys.Value));
                    store.Put(entry.PipToFingerprintKeys.Key, entry.WeakFingerprintToInputs.Value, columnFamilyName: ColumnNames.WeakFingerprints);

                    var sfEntry = entry.StrongFingerprintEntry;
                    store.Put(entry.PipToFingerprintKeys.Key, sfEntry.StrongFingerprintToInputs.Value, columnFamilyName: ColumnNames.StrongFingerprints);

                    if (storePathSet)
                    {
                        Counters.IncrementCounter(FingerprintStoreCounters.NumPathSetEntriesPut);
                        store.Put(sfEntry.PathSetHashToInputs.Key, sfEntry.PathSetHashToInputs.Value, columnFamilyName: ColumnNames.ContentHashes);
                    }
                })
            );

            // Renew TTL on entries
            m_lruEntryTracker?.TrackFingerprintStoreEntry(entry.PipToFingerprintKeys.Key, entry.PipToFingerprintKeys.Value);
        }

        /// <summary>
        /// Puts an entry for { content hash : inputs used for hash computation }.
        /// </summary>
        public void PutContentHash(string contentHash, string inputs)
        {
            PutInternal(contentHash, inputs, columnFamilyName: ColumnNames.ContentHashes);

            // Renew TTL on entry
            m_lruEntryTracker?.TrackContentHashEntry(contentHash);
        }

        private void PutInternal(string key, string value, string columnFamilyName = null)
        {
            Contract.Requires(!Accessor.ReadOnly);

            Analysis.IgnoreResult(
                Accessor.Use(store =>
                {
                    store.Put(key, value, columnFamilyName: columnFamilyName);
                })
            );
        }

        private void PutInternal(byte[] key, byte[] value, string columnFamilyName = null)
        {
            Analysis.IgnoreResult(
                Accessor.Use(store =>
                {
                    store.Put(key, value, columnFamilyName: columnFamilyName);
                })
            );
        }

        /// <summary>
        /// Retrieves a pip's <see cref="PipFingerprintKeys"/>.
        /// </summary>
        /// <param name="pipFormattedSemiStableHash">
        /// A <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>
        /// </param>
        /// <param name="pipFingerprintKeys">
        /// The corresponding <see cref="PipFingerprintKeys"/> which includes the pip's last used weak and strong fingerprint and path set hash.
        /// </param>
        public bool TryGetPipFingerprintKeys(string pipFormattedSemiStableHash, out PipFingerprintKeys pipFingerprintKeys)
        {
            pipFingerprintKeys = default;

            if (TryGetValueInternal(pipFormattedSemiStableHash, out var jsonValue)
                && TryParsePipToFingerprintKeysValue(jsonValue, out pipFingerprintKeys))
            {
                // Gets should renew TTL for entry
                m_lruEntryTracker?.TrackFingerprintStoreEntry(pipFormattedSemiStableHash, pipFingerprintKeys);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Retrieves a pip's <see cref="FingerprintStoreEntry"/>.
        /// The order of lookups is described below.
        /// 
        /// Legend:
        /// columnName
        /// { key : next lookup key }
        /// { key : [final value] }
        /// 
        /// Lookups:
        /// <see cref="ColumnNames.PipUniqueOutputHashes"/>
        /// { pipUniqueOutputHash : pipFormattedSemiStableHash }
        ///                       <see cref="ColumnNames.Default"/>
        ///                       { pipFormattedSemiStableHash : pipFingerprintKeys }
        ///                                                    <see cref="ColumnNames.ContentHashes"/>
        ///                                                    { pipFingerprintKeys.PathSetHash : [ path set hash inputs ] }
        ///                                                    [ pipFingerprintKeys.WeakFingerprint]
        ///                                                    [ pipFingerprintKeys.StrongFingerprint]
        ///                       <see cref="ColumnNames.WeakFingerprints"/>
        ///                       { pipFormattedSemiStableHash : [ weak fingerprint inputs ] }
        ///                       <see cref="ColumnNames.StrongFingerprints"/>
        ///                       { pipFormattedSemiStableHash : [ strong fingerprint inputs ] }
        /// </summary>
        /// <param name="pipUniqueOutputHash">
        /// The pip's unique output hash as computed by <see cref="BuildXL.Pips.Operations.Process.TryComputePipUniqueOutputHash(PathTable, out long, PathExpander)"/>.
        /// </param>
        /// <param name="formattedSemiStableHash">
        /// The pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
        /// </param>
        /// <param name="entry">
        /// If successful, the pip's <see cref="FingerprintStoreEntry"/> with fingerprint input information;
        /// Otherwise, an invalid <see cref="FingerprintStoreEntry"/>.
        /// </param>
        public bool TryGetFingerprintStoreEntry(string pipUniqueOutputHash, string formattedSemiStableHash, out FingerprintStoreEntry entry)
        {
            // Find initial entry for pip data
            // First attempt to use the pip output hash which is a more reliable way to identify pips across builds
            // than the pip semi stable hash

            return TryGetFingerprintStoreEntryByPipUniqueOutputHash(pipUniqueOutputHash, out entry)
                || TryGetFingerprintStoreEntryBySemiStableHash(formattedSemiStableHash, out entry);
        }

        /// <summary>
        /// Retrieves a pip's <see cref="FingerprintStoreEntry"/>.
        /// <see cref="TryGetFingerprintStoreEntry(string, string, out FingerprintStoreEntry)"/> for full order of lookups.
        /// </summary>
        /// <param name="pipUniqueOutputHash">
        /// The pip's unique output hash as computed by <see cref="BuildXL.Pips.Operations.Process.TryComputePipUniqueOutputHash(PathTable, out long, PathExpander)"/>.
        /// </param>
        /// <param name="entry">
        /// If successful, the pip's <see cref="FingerprintStoreEntry"/> with fingerprint input information;
        /// Otherwise, an invalid <see cref="FingerprintStoreEntry"/>.
        /// </param>
        public bool TryGetFingerprintStoreEntryByPipUniqueOutputHash(string pipUniqueOutputHash, out FingerprintStoreEntry entry)
        {
            entry = null;
            return TryGetPipUniqueOutputHashValue(pipUniqueOutputHash, out var pipFormattedSemiStableHash)
                && TryGetFingerprintStoreEntryBySemiStableHash(pipFormattedSemiStableHash, out entry);
        }

        /// <summary>
        /// Retrieves a pip's <see cref="FingerprintStoreEntry"/>.
        /// <see cref="TryGetFingerprintStoreEntry(string, string, out FingerprintStoreEntry)"/> for full order of lookups.
        /// </summary>
        /// <param name="pipFormattedSemiStableHash">
        /// The pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
        /// </param>
        /// <param name="entry">
        /// If successful, the pip's <see cref="FingerprintStoreEntry"/> with fingerprint input information;
        /// Otherwise, an invalid <see cref="FingerprintStoreEntry"/>.
        /// </param>
        public bool TryGetFingerprintStoreEntryBySemiStableHash(string pipFormattedSemiStableHash, out FingerprintStoreEntry entry)
        {
            entry = null;

            // Get the initial entry for the pip
            if (!TryGetPipFingerprintKeys(pipFormattedSemiStableHash, out var pipFingerprintKeys))
            {
                return false;
            }

            // Get the weak fingerprint entry
            if (!TryGetWeakFingerprintValue(pipFormattedSemiStableHash, out var weakFingerprintInputs))
            {
                return false;
            }

            // Get the strong fingerprint entry
            if (!TryGetStrongFingerprintEntry(
                pipFormattedSemiStableHash,
                pipFingerprintKeys,
                out var strongFingerprintEntry))
            {
                return false;
            }

            entry = new FingerprintStoreEntry
            {
                PipToFingerprintKeys = new KeyValuePair<string, PipFingerprintKeys>(pipFormattedSemiStableHash, pipFingerprintKeys),
                WeakFingerprintToInputs = new KVP(pipFingerprintKeys.WeakFingerprint, weakFingerprintInputs),
                StrongFingerprintEntry = strongFingerprintEntry
            };

            // Renew TTL for entries
            m_lruEntryTracker?.TrackFingerprintStoreEntry(entry.PipToFingerprintKeys.Key, entry.PipToFingerprintKeys.Value);
            return true;
        }

        /// <summary>
        /// Parses <see cref="FingerprintStoreEntry.PipToFingerprintKeys"/> entry (retrievable through <see cref="TryGetPipFingerprintKeys(string, out PipFingerprintKeys)"/>
        /// or <see cref="TryGetFingerprintStoreEntryBySemiStableHash(string, out FingerprintStoreEntry)"/>)
        /// into a <see cref="PipFingerprintKeys"/>.
        /// </summary>
        private bool TryParsePipToFingerprintKeysValue(string pipToFingerprintsEntryValue, out PipFingerprintKeys pipFingerprintKeys)
        {
            var reader = new JsonReader(pipToFingerprintsEntryValue);

            // Parse the fingerprints entry for weak and strong fingerprint
            pipFingerprintKeys = new PipFingerprintKeys();
            if (!reader.TryGetPropertyValue(PropertyNames.WeakFingerprint, out pipFingerprintKeys.WeakFingerprint)
                || !reader.TryGetPropertyValue(PropertyNames.StrongFingerprint, out pipFingerprintKeys.StrongFingerprint)
                || !reader.TryGetPropertyValue(PropertyNames.PathSet, out pipFingerprintKeys.FormattedPathSetHash))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Retrieves a pip's <see cref="StrongFingerprintEntry"/>.
        /// </summary>
        /// <param name="pipFormattedSemiStableHash">
        /// The pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
        /// </param>
        /// <param name="pipFingerprintKeys">
        /// The pip's <see cref="PipFingerprintKeys"/>, retrievable through <see cref="TryGetPipFingerprintKeys(string, out PipFingerprintKeys)"/>.
        /// </param>
        /// <param name="strongFingerprintEntry">
        /// If successful, the pip's <see cref="StrongFingerprintEntry"/> with fingerprint input information;
        /// Otherwise, an invalid <see cref="StrongFingerprintEntry"/>.
        /// </param>
        private bool TryGetStrongFingerprintEntry(string pipFormattedSemiStableHash, PipFingerprintKeys pipFingerprintKeys, out StrongFingerprintEntry strongFingerprintEntry)
        {
            strongFingerprintEntry = new StrongFingerprintEntry();

            if (!TryGetStrongFingerprintValue(pipFormattedSemiStableHash, out var strongFingerprintInputs))
            {
                return false;
            };
            strongFingerprintEntry.StrongFingerprintToInputs = new KVP(pipFingerprintKeys.StrongFingerprint, strongFingerprintInputs);

            var reader = new JsonReader(strongFingerprintInputs);

            // Path sets are stored separately, so extract path set inputs
            if (!TryGetContentHashValue(pipFingerprintKeys.FormattedPathSetHash, out var pathSetInputs))
            {
                return false;
            }

            strongFingerprintEntry.PathSetHashToInputs = new KVP(pipFingerprintKeys.FormattedPathSetHash, pathSetInputs);
            return true;
        }

        /// <summary>
        /// Retrieves a pip's weak fingerprint inputs.
        /// Internal for testing.
        /// </summary>
        /// <param name="pipFormattedSemiStableHash">
        /// The pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
        /// </param>
        /// <param name="value">
        /// If successful, the pip's weak fingerprint inputs as a JSON string;
        /// otherwise, null.
        /// </param>
        internal bool TryGetWeakFingerprintValue(string pipFormattedSemiStableHash, out string value)
        {
            return TryGetValueInternal(pipFormattedSemiStableHash, out value, ColumnNames.WeakFingerprints);
        }

        /// <summary>
        /// Retrieves a pip's strong fingerprint inputs.
        /// Internal for testing.
        /// </summary>
        /// <param name="pipFormattedSemiStableHash">
        /// The pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
        /// </param>
        /// <param name="value">
        /// If successful, the pip's strong fingerprint inputs as a JSON string;
        /// otherwise, null.
        /// </param>
        internal bool TryGetStrongFingerprintValue(string pipFormattedSemiStableHash, out string value)
        {
            return TryGetValueInternal(pipFormattedSemiStableHash, out value, ColumnNames.StrongFingerprints);
        }

        /// <summary>
        /// Retrieves the inputs used to compute a content hash's hash.
        /// </summary>
        /// <param name="contentHash">
        /// The content hash as a string. The string format should match the corresponding <see cref="PutContentHash(string, string)"/> call.
        /// </param>
        /// <param name="value">
        /// If successful, the content hash's inputs as a JSON string;
        /// otherwise, null.
        /// </param>
        public bool TryGetContentHashValue(string contentHash, out string value)
        {
            // Renew TTL for entry
            m_lruEntryTracker?.TrackContentHashEntry(contentHash);
            return TryGetValueInternal(contentHash, out value, ColumnNames.ContentHashes);
        }

        /// <summary>
        /// Retrieves a pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
        /// </summary>
        /// <param name="pipUniqueOutputHash">
        /// The pip's unique output hash as computed by <see cref="BuildXL.Pips.Operations.Process.TryComputePipUniqueOutputHash(PathTable, out long, PathExpander)"/>.
        /// </param>
        /// <param name="pipFormattedSemiStableHash">
        /// If successful, the pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>;
        /// otherwise, null.
        /// </param>
        public bool TryGetPipUniqueOutputHashValue(string pipUniqueOutputHash, out string pipFormattedSemiStableHash)
        {
            m_lruEntryTracker?.TrackPipUniqueOutputHashEntry(pipUniqueOutputHash);
            return TryGetValueInternal(pipUniqueOutputHash, out pipFormattedSemiStableHash, columnFamilyName: ColumnNames.PipUniqueOutputHashes);
        }

        private bool TryGetValueInternal(string key, out string value, string columnFamilyName = null)
        {
            if (m_mode == FingerprintStoreMode.IgnoreExistingEntries)
            {
                value = null;
                return false;
            }

            var keyFound = false;
            string innerValue = null;
            Analysis.IgnoreResult(
                Accessor.Use(store =>
                {
                    keyFound = store.TryGetValue(key, out innerValue, columnFamilyName: columnFamilyName);
                })
            );
            value = innerValue;
            return keyFound;
        }

        private bool TryGetValueInternal(byte[] key, out byte[] value, string columnFamilyName = null)
        {
            if (m_mode == FingerprintStoreMode.IgnoreExistingEntries)
            {
                value = null;
                return false;
            }

            var keyFound = false;
            byte[] innerValue = null;
            Analysis.IgnoreResult(
                Accessor.Use(store => { keyFound = store.TryGetValue(key, out innerValue, columnFamilyName: columnFamilyName); })
            );

            value = innerValue;
            return keyFound;
        }

        private bool ContainsInternal(string key, string columnFamilyName = null)
        {
            if (m_mode == FingerprintStoreMode.IgnoreExistingEntries)
            {
                return false;
            }

            var keyFound = false;
            Analysis.IgnoreResult(
                Accessor.Use(store =>
                {
                    keyFound = store.Contains(key, columnFamilyName);
                })
            );

            return keyFound;
        }

        /// <summary>
        /// Checks if a content hash already exists in the store.
        /// </summary>
        public bool ContainsContentHash(string contentHash)
        {
            return ContainsInternal(contentHash.ToString(), ColumnNames.ContentHashes);
        }

        /// <summary>
        /// Checks if a <see cref="FingerprintStoreEntry"/> exists in the store for the given pip.
        /// </summary>
        /// <param name="pipFormattedSemiStableHash">
        /// The pip's <see cref="BuildXL.Pips.Operations.Pip.FormattedSemiStableHash"/>.
        /// </param>
        /// <param name="pipUniqueOutputHash">
        /// The pip's unique output hash as computed by <see cref="BuildXL.Pips.Operations.Process.TryComputePipUniqueOutputHash(PathTable, out long, PathExpander)"/>.
        /// </param>
        public bool ContainsFingerprintStoreEntry(string pipFormattedSemiStableHash, string pipUniqueOutputHash = null)
        {
            if (pipUniqueOutputHash != null)
            {
                // Prioritize looking up the entry using pip unique output hash as the pip identifier first
                // The pip unique output hash is more stable across builds than the formatted semi stable hash
                if (TryGetPipUniqueOutputHashValue(pipUniqueOutputHash, out var semiStableHash)
                    && ContainsInternal(semiStableHash))
                {
                    return true;
                }
            }

            return ContainsInternal(pipFormattedSemiStableHash);
        }

        /// <summary>
        /// Serializes <see cref="PipFingerprintKeys"/> to JSON.
        /// </summary>
        private string JsonSerialize(PipFingerprintKeys pfk)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.JsonSerializationTime))
            {
                return JsonFingerprinter.CreateJsonString(writer =>
                    {
                        writer.Add(FingerprintStoreConstants.WeakFingerprint, pfk.WeakFingerprint);
                        writer.Add(FingerprintStoreConstants.StrongFingerprint, pfk.StrongFingerprint);
                        writer.Add(ObservedPathEntryConstants.PathSet, pfk.FormattedPathSetHash);
                    });
            }
        }

        /// <summary>
        /// Given a column family, returns other column families that share the same key space.
        /// Column families with the same key space can be garbage collected together.
        /// </summary>
        private IEnumerable<string> GetColumnFamiliesWithSameKeySpace(string columnFamilyName = null)
        {
            var otherColumns = new List<string>();
            switch (columnFamilyName)
            {
                // default column that contains the starting lookup for the fingerprint store
                case null:
                    otherColumns.Add(ColumnNames.WeakFingerprints);
                    otherColumns.Add(ColumnNames.StrongFingerprints);
                    break;
                // Columns that don't share key spaces with other columns
                case ColumnNames.ContentHashes:
                case ColumnNames.PipUniqueOutputHashes:
                default:
                    break;
            }

            return otherColumns;
        }

        /// <summary>
        /// Updates the LRU entry map kept in the FingerprintStore with entry tracking info from the current FingerprintStore session.
        /// </summary>
        private void UpdateLruEntriesMap(LruEntriesMap lruEntriesMap, DateTime currentTime, string columnFamilyName = null)
        {
            foreach (var trackedEntry in m_lruEntryTracker[columnFamilyName])
            {
                // Refresh TTL for entries tracked in current build
                lruEntriesMap[trackedEntry] = currentTime.Ticks;
            }
        }

        /// <summary>
        /// Garbage collects the fingerprint store and also handles managing LRU for entries.
        /// </summary>
        private void GarbageCollect()
        {
            Contract.Requires(!Accessor.ReadOnly, "Garbage collection should not be performed for read-only accesses.");

            using (Counters.StartStopwatch(FingerprintStoreCounters.TotalGarbageCollectionTime))
            {
                var garbageCollectionTimestamp = DateTime.UtcNow;

#if !DEBUG
                // Limit max amount of time for GC to 10 seconds
                GarbageCollectCancellationToken.CancelAfter(m_garbageCollectionTimeLimit);
#endif

                // Each column is independent of the others, so they can be garbage collected in parallel
                Parallel.ForEach(ColumnNames.ListAll, column =>
                {
                    var maxEntryCounterLock = new object();
                    var maxEntryCollectTime = TimeSpan.Zero;

                    // No-op case, garbage collect has been cancelled and there are no LRU entries to refresh
                    if (GarbageCollectCancellationToken.IsCancellationRequested && m_lruEntryTracker[column].Count == 0)
                    {
                        return;
                    }

                    var gcResult = GarbageCollectColumn(garbageCollectionTimestamp, column);

                    lock (maxEntryCounterLock)
                    {
                        if (gcResult.MaxBatchEvictionTime > maxEntryCollectTime)
                        {
                            maxEntryCollectTime = gcResult.MaxBatchEvictionTime;
                        }
                    }

                    if (gcResult.Canceled && m_loggingContext != null)
                    {
                        Logger.Log.FingerprintStoreGarbageCollectCanceled(m_loggingContext, column, m_garbageCollectionTimeLimit.ToString());
                    }

                    Counters.AddToCounter(FingerprintStoreCounters.GarbageCollectionMaxEntryTime, maxEntryCollectTime);
                });
            }
        }

        /// <summary>
        /// Garbage collects a column family of the fingerprint store and manages the LRU for entries.
        /// </summary>
        /// <param name="currentTime">
        /// A timestamp that represents the current build (or garbage collection session).
        /// Used for determining if entries have expired and renewing TTLs.
        /// </param>
        /// <param name="columnFamilyName">
        /// The column family to use. If nothing is passed in, the default column will be used.
        /// </param>
        private GarbageCollectResult GarbageCollectColumn(DateTime currentTime, string columnFamilyName = null)
        {
            var gcResult = default(GarbageCollectResult);

            // If there is no LruEntriesMap, assume this is a brand new FingerprintStore with no previous entries to garbage collect
            var previousEntriesExist = TryGetLruEntriesMap(out var mapFromStore, columnFamilyName);
            var lruEntriesMap = previousEntriesExist ? mapFromStore : new LruEntriesMap();

            UpdateLruEntriesMap(lruEntriesMap, currentTime, columnFamilyName);

            if (previousEntriesExist && !GarbageCollectCancellationToken.IsCancellationRequested)
            {
                using (Counters.StartStopwatch(FingerprintStoreCounters.GarbageCollectionTime))
                {
                    // Garbage collect the column based on updated LRU map
                    double avgEntryAgeMinutes = 0;
                    Analysis.IgnoreResult(
                        Accessor.Use(store =>
                        {
                            gcResult = store.GarbageCollect(key =>
                            {
                                if (lruEntriesMap.TryGetValue(key, out var entryAgeTicks))
                                {
                                    var entryAge = currentTime.Subtract(new DateTime(entryAgeTicks));

                                    // Calculate a running average one element at a time instead of using (sum / n) to avoid overflowing the sum
                                    avgEntryAgeMinutes = (gcResult.TotalCount / (gcResult.TotalCount + 1)) * avgEntryAgeMinutes + entryAge.TotalMinutes / (gcResult.TotalCount + 1);

                                    if (entryAge > m_maxEntryAge)
                                    {
                                        lruEntriesMap.Remove(key);
                                        return true;
                                    }
                                }

                                // Metadata entries will not have LRU entries, but should not be garbage collected
                                return false;
                            },
                            columnFamilyName: columnFamilyName,
                            additionalColumnFamilies: GetColumnFamiliesWithSameKeySpace(columnFamilyName),
                            cancellationToken: GarbageCollectCancellationToken.Token);
                        })
                    );
                    UpdateGarbageCollectCounters(gcResult, (long)avgEntryAgeMinutes, columnFamilyName);
                }
            }

            if (m_lruEntryTracker[columnFamilyName].Count + gcResult.RemovedCount > 0)
            {
                // Changes have been made to the LRU map that need to be persisted
                PutLruEntriesMap(lruEntriesMap, columnFamilyName);
            }
            return gcResult;
        }

        /// <summary>
        /// Updates counters for garbage collect stats for each column family.
        /// </summary>
        private void UpdateGarbageCollectCounters(GarbageCollectResult gcResult, long avgEntryAgeMin, string columnFamilyName)
        {
            // C# switch-cases don't allow static values but the default column name 
            // is declared static by the underlying key-value store
            if (columnFamilyName == ColumnNames.Default)
            {
                columnFamilyName = null;
            }

            // Log stats
            switch (columnFamilyName)
            {
                // default column where main pip entry is stored
                case null:
                    Counters.AddToCounter(FingerprintStoreCounters.NumPipFingerprintEntriesGarbageCollected, gcResult.RemovedCount);
                    Counters.AddToCounter(FingerprintStoreCounters.NumPipFingerprintEntriesRemaining, gcResult.TotalCount - gcResult.RemovedCount);
                    Counters.AddToCounter(FingerprintStoreCounters.PipFingerprintEntriesAverageEntryAgeMinutes, avgEntryAgeMin);
                    break;
                case ColumnNames.ContentHashes:
                    Counters.AddToCounter(FingerprintStoreCounters.NumContentHashEntriesGarbageCollected, gcResult.RemovedCount);
                    Counters.AddToCounter(FingerprintStoreCounters.NumContentHashEntriesRemaining, gcResult.TotalCount - gcResult.RemovedCount);
                    Counters.AddToCounter(FingerprintStoreCounters.ContentHashEntriesAverageEntryAgeMinutes, avgEntryAgeMin);
                    break;
                case ColumnNames.PipUniqueOutputHashes:
                    Counters.AddToCounter(FingerprintStoreCounters.NumPipUniqueOutputHashEntriesGarbageCollected, gcResult.RemovedCount);
                    Counters.AddToCounter(FingerprintStoreCounters.NumPipUniqueOutputHashEntriesRemaining, gcResult.TotalCount - gcResult.RemovedCount);
                    Counters.AddToCounter(FingerprintStoreCounters.PipUniqueOutputHashEntriesAverageEntryAgeMinutes, avgEntryAgeMin);
                    break;
                default:
                    // Any other column should have a 1:n relationship between
                    // NumPipFingerprintEntries:NumOtherColumnEntries so multiplication
                    // of the PipFingerprintEntries counters is sufficient
                    break;
            }
        }

        /// <summary>
        /// TESTING ONLY. Removes a content hash entry from the store. Internal for testing.
        /// </summary>
        internal void RemoveContentHashForTesting(string contentHash)
        {
            Analysis.IgnoreResult(
                Accessor.Use(store =>
                {
                    store.Remove(contentHash, ColumnNames.ContentHashes);
                })
            );
        }

        /// <summary>
        /// TESTING ONLY. Removes a content hash entry from the store. Internal for testing.
        /// </summary>
        internal void RemoveFingerprintStoreEntryForTesting(FingerprintStoreEntry entry)
        {
            Analysis.IgnoreResult(
                Accessor.Use(store =>
                {
                    store.Remove(entry.PipToFingerprintKeys.Key);
                    store.Remove(entry.PipToFingerprintKeys.Value.WeakFingerprint, ColumnNames.WeakFingerprints);
                    store.Remove(entry.PipToFingerprintKeys.Value.StrongFingerprint, ColumnNames.StrongFingerprints);
                    store.Remove(entry.PipToFingerprintKeys.Value.FormattedPathSetHash, ColumnNames.ContentHashes);
                })
            );
        }

        /// <summary>
        /// Creates a snapshot of the fingerprint store in the log directory
        /// specified by the configuration.
        /// </summary>
        internal static async Task<Unit> CopyAsync(
            LoggingContext loggingContext,
            FingerprintStoreTestHooks testHooks,
            PathTable pathTable,
            IConfiguration configuration,
            CounterCollection<FingerprintStoreCounters> counters = null)
        {
            using (counters?.StartStopwatch(FingerprintStoreCounters.SnapshotTime))
            {
                var logDirectory = configuration.Logging.ExecutionFingerprintStoreLogDirectory.ToString(pathTable);
                var filesToCopy = new List<Task<bool>>();
                var hardLinkFailureSeen = false;

                try
                {
                    if (FileUtilities.Exists(logDirectory))
                    {
                        FileUtilities.DeleteDirectoryContents(logDirectory);
                    }
                    else
                    {
                        FileUtilities.CreateDirectory(logDirectory);
                    }

                    // Copy all files 
                    FileUtilities.EnumerateDirectoryEntries(
                        configuration.Layout.FingerprintStoreDirectory.ToString(pathTable),
                        recursive: false,
                        handleEntry: (directory, file, fileAttributes) =>
                        {
                            // Ignore archive directory
                            if ((fileAttributes & FileAttributes.Directory) == 0)
                            {
                                var storeFile = Path.Combine(directory, file);
                                var logFile = Path.Combine(logDirectory, file);

                                if (testHooks != null && testHooks.MinimalIO && 
                                    Path.GetFileName(file).ToUpperInvariant().Contains(KeyValueStoreAccessor.LogFileName))
                                {
                                    // Skip copying extra files
                                    return;
                                }

                                // Attempt to hard link immutable storage files; if this fails, make a copy
                                if (Path.GetExtension(file).Equals(KeyValueStoreAccessor.StorageFileTypeExtension, StringComparison.OrdinalIgnoreCase))
                                {
                                    counters?.AddToCounter(FingerprintStoreCounters.TotalStorageFilesSizeBytes, new FileInfo(storeFile).Length);

                                    // Assume if the first hard link fails, all the hard links will fail
                                    if (!hardLinkFailureSeen)
                                    {
                                        if (FileUtilities.IsCopyOnWriteSupportedByEnlistmentVolume)
                                        {
                                            var possiblyCreateCopyOnWrite = FileUtilities.TryCreateCopyOnWrite(storeFile, logFile, followSymlink: false);

                                            if (possiblyCreateCopyOnWrite.Succeeded)
                                            {
                                                counters?.IncrementCounter(FingerprintStoreCounters.SnapshotNumStorageFilesCopyOnWrite);
                                                return;
                                            }

                                            Logger.Log.FingerprintStoreUnableToCopyOnWriteLogFile(loggingContext, logFile, storeFile, possiblyCreateCopyOnWrite.Failure.Describe());
                                        }

                                        var hardlinkStatus = FileUtilities.TryCreateHardLink(logFile, storeFile);

                                        if (hardlinkStatus == CreateHardLinkStatus.Success)
                                        {
                                            counters?.IncrementCounter(FingerprintStoreCounters.SnapshotNumStorageFilesHardlinked);
                                            return;
                                        }

                                        Logger.Log.FingerprintStoreUnableToHardLinkLogFile(loggingContext, logFile, storeFile, hardlinkStatus.ToString());

                                        hardLinkFailureSeen = true;
                                    }
                                }
                                else
                                {
                                    counters?.AddToCounter(FingerprintStoreCounters.TotalOtherFilesSizeBytes, new FileInfo(storeFile).Length);
                                }

                                // Copy any files that are not storage or outdated
                                // Storage files fall through to here if hard linking fails
                                // IndexOf is a way to do case insensitive string.Contains
                                if (file.IndexOf(KeyValueStoreAccessor.OutdatedFileMarker, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    counters?.IncrementCounter(FingerprintStoreCounters.SnapshotNumOtherFilesCopied);
                                    filesToCopy.Add(FileUtilities.CopyFileAsync(storeFile, logFile));
                                }
                            }
                        });

                    await Task.WhenAll(filesToCopy);
                }
                catch (BuildXLException ex)
                {
                    Logger.Log.FingerprintStoreSnapshotException(loggingContext, ex.Message);
                }

                return Unit.Void;

            }
        }

        /// <summary>
        /// Takes a string key-value pair with a JSON value and pretty-formats the JSON value.
        /// </summary>
        public static KVP PrettyFormatJsonField(KVP jsonField)
        {
            return new KVP(jsonField.Key, JsonTree.PrettyPrintJson(jsonField.Value));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Garbage collect on write sessions
            if (!Accessor.ReadOnly)
            {
                GarbageCollect();
            }

            if (m_testHooks != null)
            {
                m_testHooks.Counters = Counters;
            }

            Accessor.Dispose();
        }
    }
}
