// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Utilities.Qualifier
{
    /// <summary>
    /// A helper class to manage qualifier instances and reduce duplication
    /// </summary>
    public sealed class QualifierTable
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "QualifierTable", version: 0);

        /// <nodoc/>
        public StringTable StringTable { get; }

        private readonly BidirectionalIndexBasedMap<Qualifier, QualifierId> m_qualifiers = new BidirectionalIndexBasedMap<Qualifier, QualifierId>(
            id => id.Id, 
            id => new QualifierId(id),
            (w, q) => q.Serialize(w),
            (r) => Qualifier.Deserialize(r)
        );
        private readonly BidirectionalIndexBasedMap<QualifierSpace, QualifierSpaceId> m_qualifierSpaces = new BidirectionalIndexBasedMap<QualifierSpace, QualifierSpaceId>(
            id => id.Id, 
            id => new QualifierSpaceId(id),
            (w, q) => q.Serialize(w),
            (r) => QualifierSpace.Deserialize(r)
        );

        private readonly ConcurrentDictionary<QualifierId, string> m_qualifierToCanonicalDisplayString = new ConcurrentDictionary<QualifierId, string>();

        private readonly ConcurrentDictionary<QualifierId, string> m_qualifierToFriendlyQualifierName = new ConcurrentDictionary<QualifierId, string>();

        /// <summary>
        /// The empty qualifier id.
        /// </summary>
        public QualifierId EmptyQualifierId { get; }

        /// <summary>
        /// The empty qualifier space id.
        /// </summary>
        public QualifierSpaceId EmptyQualifierSpaceId { get; }

        private QualifierTable(StringTable stringTable, QualifierId emptyQualifierId, QualifierSpaceId emptyQualifierSpaceId)
        {
            Contract.Requires(stringTable != null);

            StringTable = stringTable;
            EmptyQualifierId = emptyQualifierId;
            EmptyQualifierSpaceId = emptyQualifierSpaceId;
        }

        /// <summary>
        /// Constructs a new qualifier table
        /// </summary>
        public QualifierTable(StringTable stringTable)
        {
            Contract.Requires(stringTable != null);

            StringTable = stringTable;
            EmptyQualifierId = m_qualifiers.GetOrAdd(Qualifier.Empty);
            EmptyQualifierSpaceId = m_qualifierSpaces.GetOrAdd(QualifierSpace.Empty);
        }

        #region Qualifiers
        /// <summary>
        /// Number of qualifiers in the table.
        /// </summary>
        public int QualifiersCount => m_qualifiers.Count;

        /// <summary>
        /// Gets or adds a qualifier.
        /// </summary>
        private QualifierId GetOrAddQualifier(Qualifier qualifier) => m_qualifiers.GetOrAdd(qualifier);

        /// <summary>
        /// Gets the qualifier associated with a given qualifier id.
        /// </summary>
        public Qualifier GetQualifier(QualifierId qualifierId) => m_qualifiers.Get(qualifierId);

        /// <summary>
        /// Creates a qualifier by appending or updating the given qualifier (by id) with a key-value pair.
        /// </summary>
        public QualifierId CreateQualifierWithValue(QualifierId qualifierId, string key, string value)
        {
            Contract.Requires(IsValidQualifierId(qualifierId));
            Contract.Requires(key != null);
            Contract.Requires(value != null);
            Contract.Ensures(IsValidQualifierId(Contract.Result<QualifierId>()));

            return CreateQualifierWithValue(qualifierId, StringId.Create(StringTable, key), StringId.Create(StringTable, value));
        }

        /// <summary>
        /// Creates a qualifier by appending or updating the given qualifier (by id) with a key-value pair.
        /// </summary>
        public QualifierId CreateQualifierWithValue(QualifierId qualifierId, StringId key, StringId value)
        {
            Contract.Requires(IsValidQualifierId(qualifierId));
            Contract.Requires(key.IsValid);
            Contract.Requires(value.IsValid);
            Contract.Ensures(IsValidQualifierId(Contract.Result<QualifierId>()));

            Qualifier qualifier = GetQualifier(qualifierId);
            Qualifier newQualifier = qualifier.CreateQualifierWithValue(StringTable, key, value);
            return GetOrAddQualifier(newQualifier);
        }

        /// <summary>
        /// Creates a qualifier given a key-value map.
        /// </summary>
        public QualifierId CreateQualifier(params Tuple<string, string>[] keyValuePairs)
        {
            Contract.Requires(keyValuePairs != null);
            Contract.RequiresForAll(keyValuePairs, pair => pair.Item1 != null && pair.Item2 != null);
            Contract.Ensures(IsValidQualifierId(Contract.Result<QualifierId>()));

            var keyValuePairIds =
                keyValuePairs.Select(
                    kvp => new Tuple<StringId, StringId>(StringId.Create(StringTable, kvp.Item1), StringId.Create(StringTable, kvp.Item2)));
            return CreateQualifier(keyValuePairIds.ToArray());
        }

        /// <summary>
        /// Creates a qualifier given a key-value map.
        /// </summary>
        public QualifierId CreateQualifier(params Tuple<StringId, StringId>[] keyValuePairs)
        {
            Contract.Requires(keyValuePairs != null);
            Contract.RequiresForAll(keyValuePairs, pair => pair.Item1.IsValid && pair.Item2.IsValid);
            Contract.Ensures(IsValidQualifierId(Contract.Result<QualifierId>()));

            Qualifier qualifier = Qualifier.CreateQualifier(StringTable, keyValuePairs);
            return GetOrAddQualifier(qualifier);
        }

        /// <summary>
        /// Creates a qualifier given a key-value map and stored it as a 'named' entry.
        /// </summary>
        public QualifierId CreateNamedQualifier(string name, IReadOnlyDictionary<string, string> keyValueMap)
        {
            var qualifierId = CreateQualifier(keyValueMap);
            m_qualifierToFriendlyQualifierName.TryAdd(qualifierId, name);
            return qualifierId;
        }

        /// <summary>
        /// Creates a qualifier given a key-value map.
        /// </summary>
        public QualifierId CreateQualifier(IReadOnlyDictionary<string, string> keyValueMap)
        {
            Contract.Requires(keyValueMap != null);
            Contract.RequiresForAll(keyValueMap, kvp => kvp.Key != null && kvp.Value != null);
            Contract.Ensures(IsValidQualifierId(Contract.Result<QualifierId>()));

            var keyValuePairIds =
                keyValueMap.Select(
                    kvp => new Tuple<StringId, StringId>(StringId.Create(StringTable, kvp.Key), StringId.Create(StringTable, kvp.Value)));
            return CreateQualifier(keyValuePairIds.ToArray());
        }

        #endregion

        #region QualifierSpaces

        /// <summary>
        /// Number of qualifier spaces in the table.
        /// </summary>
        public int QualifierSpacesCount => m_qualifierSpaces.Count;

        /// <summary>
        /// Gets or adds a qualifier space.
        /// </summary>
        private QualifierSpaceId GetOrAddQualifierSpace(QualifierSpace qualifierSpace) => m_qualifierSpaces.GetOrAdd(qualifierSpace);

        /// <summary>
        /// Gets the qualifier space associated with a given qualifier space id.
        /// </summary>
        public QualifierSpace GetQualifierSpace(QualifierSpaceId qualifierSpaceId) => m_qualifierSpaces.Get(qualifierSpaceId);

        /// <summary>
        /// Creates a qualifier space given mappings from keys to list of eligible values.
        /// </summary>
        public QualifierSpaceId CreateQualifierSpace(params Tuple<string, string[]>[] keyValuesPairs)
        {
            Contract.Requires(keyValuesPairs != null);
            Contract.Requires(
                Contract.ForAll(
                    keyValuesPairs,
                    pair => pair.Item1 != null && pair.Item2 != null && pair.Item2.Length > 0 && Contract.ForAll(pair.Item2, value => value != null)));
            Contract.Ensures(IsValidQualifierSpaceId(Contract.Result<QualifierSpaceId>()));

            var keyValuesPairIds = new QualifierSpaceEntry[keyValuesPairs.Length];

            for (int i = 0; i < keyValuesPairs.Length; ++i)
            {
                var kvp = keyValuesPairs[i];
                var valuesIds = new StringId[kvp.Item2.Length];

                for (int j = 0; j < kvp.Item2.Length; ++j)
                {
                    valuesIds[j] = StringId.Create(StringTable, kvp.Item2[j]);
                }

                keyValuesPairIds[i] = QualifierSpaceEntry.Create(StringId.Create(StringTable, kvp.Item1), valuesIds);
            }

            return CreateQualifierSpace(keyValuesPairIds);
        }

        /// <summary>
        /// Creates a qualifier space given mappings from keys to list of eligible values.
        /// </summary>
        public QualifierSpaceId CreateQualifierSpace(IReadOnlyDictionary<string, IReadOnlyList<string>> keyValuesMap)
        {
            Contract.Requires(keyValuesMap != null);
            Contract.Requires(
                Contract.ForAll(
                    keyValuesMap,
                    kvp => kvp.Key != null && kvp.Value != null && kvp.Value.Count > 0 && Contract.ForAll(kvp.Value, value => value != null)));
            Contract.Ensures(IsValidQualifierSpaceId(Contract.Result<QualifierSpaceId>()));

            var keyValuesPairIds = new QualifierSpaceEntry[keyValuesMap.Count];

            int i = 0;

            foreach (var kvp in keyValuesMap)
            {
                var valuesIds = new StringId[kvp.Value.Count];

                for (int j = 0; j < kvp.Value.Count; ++j)
                {
                    valuesIds[j] = StringId.Create(StringTable, kvp.Value[j]);
                }

                keyValuesPairIds[i] = QualifierSpaceEntry.Create(StringId.Create(StringTable, kvp.Key), valuesIds);
                ++i;
            }

            return CreateQualifierSpace(keyValuesPairIds);
        }

        /// <summary>
        /// Creates a qualifier space given mappings from keys to list of eligible values.
        /// </summary>
        public QualifierSpaceId CreateQualifierSpace(params QualifierSpaceEntry[] keyValuesPairs)
        {
            Contract.Requires(keyValuesPairs != null);
#if DEBUG
            Contract.RequiresForAll(keyValuesPairs, e => e.IsValid);
#endif

            Contract.Ensures(IsValidQualifierSpaceId(Contract.Result<QualifierSpaceId>()));

            QualifierSpace qualifierSpace = QualifierSpace.CreateQualifierSpace(StringTable, keyValuesPairs);
            return GetOrAddQualifierSpace(qualifierSpace);
        }

        #endregion

        /// <summary>
        /// Creates a qualifier from a given input qualifier such that the resulting qualifier respects target qualifier space.
        /// </summary>
        public bool TryCreateQualifierForQualifierSpace(
            PathTable pathTable,
            LoggingContext loggingContext,
            QualifierId qualifierId,
            QualifierSpaceId qualifierSpaceId,
            bool useDefaultsForCoercion,
            out QualifierId resultingQualifierId,
            out UnsupportedQualifierValue error)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(loggingContext != null);
#if DEBUG
            Contract.Requires(IsValidQualifierId(qualifierId));
            Contract.Requires(qualifierSpaceId.IsValid);
            Contract.Requires(IsValidQualifierSpaceId(qualifierSpaceId), "Id " + qualifierSpaceId.Id + " is not valid.");
            Contract.Ensures(!Contract.Result<bool>() || IsValidQualifierId(Contract.ValueAtReturn(out resultingQualifierId)));
#endif

            Qualifier qualifier = GetQualifier(qualifierId);
            QualifierSpace qualifierSpace = GetQualifierSpace(qualifierSpaceId);
            Qualifier resultingQualifier;

            bool success = qualifierSpace.TryCreateQualifierForQualifierSpace(
                StringTable,
                pathTable,
                loggingContext,
                qualifier,
                out resultingQualifier,
                out error,
                useDefaultsForCoercion);
            resultingQualifierId = success ? GetOrAddQualifier(resultingQualifier) : EmptyQualifierId;

            return success;
        }

        /// <summary>
        /// Gets a canonical string representation of the qualifier.
        /// </summary>
        public string GetCanonicalDisplayString(QualifierId qualifierId)
        {
#if DEBUG
            Contract.Requires(IsValidQualifierId(qualifierId));
#endif

            return m_qualifierToCanonicalDisplayString.GetOrAdd(
                qualifierId,
                id =>
                {
                    var qualifier = GetQualifier(id);
                    return qualifier.ToDisplayString(StringTable);
                });
        }

        /// <summary>
        /// Returns a friendly string for this qualifier. 
        /// </summary>
        /// <remarks>
        /// If the configruation file has a name for this qualifier, that name will be used, else the canonical name will be used.
        /// </remarks>
        public string GetFriendlyUserString(QualifierId qualifierId)
        {
#if DEBUG
            Contract.Requires(IsValidQualifierId(qualifierId));
#endif

            if (m_qualifierToFriendlyQualifierName.TryGetValue(qualifierId, out var friendlyString))
            {
                return friendlyString;
            }

            return GetCanonicalDisplayString(qualifierId);
        }

        /// <summary>
        /// Checks if a qualifier id is valid with respect to this qualifier table.
        /// </summary>
        [Pure]
        public bool IsValidQualifierId(QualifierId qualifierId)
        {
            return qualifierId.IsValid && qualifierId.Id < m_qualifiers.Count;
        }

        /// <summary>
        /// Checks if a qualifier space id is valid with respect to this qualifier table.
        /// </summary>
        [Pure]
        public bool IsValidQualifierSpaceId(QualifierSpaceId qualifierSpaceId)
        {
            return qualifierSpaceId.IsValid && qualifierSpaceId.Id < m_qualifierSpaces.Count;
        }

        /// <nodoc />
        public static bool IsValidQualifierKey(string key)
        {
            return -1 == key.IndexOfAny(new[] { ';', '=' });
        }

        /// <nodoc />
        public static bool IsValidQualifierValue(string key)
        {
            return -1 == key.IndexOfAny(new[] { ';', '=' });
        }

        /// <nodoc />
        public static async Task<QualifierTable> DeserializeAsync(BuildXLReader reader, Task<StringTable> stringTableTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(stringTableTask != null);

            var stringTable = await stringTableTask;

            var emptyQualifierId = reader.ReadQualifierId();
            var emptyQualifierSpaceId = reader.ReadQualifierSpaceId();

            var qualifierTable = new QualifierTable(stringTable, emptyQualifierId, emptyQualifierSpaceId);

            qualifierTable.m_qualifiers.Deserialize(reader);
            qualifierTable.m_qualifierSpaces.Deserialize(reader);

            var count = reader.ReadInt32Compact();
            for (int i = 0; i < count; i++)
            {
                var qualifierId = reader.ReadQualifierId();
                var friendlyName = reader.ReadString();
                qualifierTable.m_qualifierToFriendlyQualifierName.TryAdd(qualifierId, friendlyName);
            }

            return qualifierTable;
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(EmptyQualifierId);
            writer.Write(EmptyQualifierSpaceId);

            m_qualifiers.Serialize(writer);
            m_qualifierSpaces.Serialize(writer);

            writer.WriteCompact(m_qualifierToFriendlyQualifierName.Count);
            foreach (var kv in m_qualifierToFriendlyQualifierName)
            {
                writer.Write(kv.Key);
                writer.Write(kv.Value);
            }
        }

        /// <summary>
        /// Maintains a dictionary and integer index-based bi directional map betwen the item and the id.
        /// </summary>
        private class BidirectionalIndexBasedMap<TItem, TId>
        {
            private readonly object m_lock = new object();
            private readonly List<TItem> m_items = new List<TItem>();
            private readonly Dictionary<TItem, TId> m_itemToKeyMapping = new Dictionary<TItem, TId>();

            private readonly Func<TId, int> m_idToInt;
            private readonly Func<int, TId> m_intToId;
            private readonly Action<BuildXLWriter, TItem> m_serializeItem;
            private readonly Func<BuildXLReader, TItem> m_deserializeItem;

            /// <nodoc />
            public BidirectionalIndexBasedMap(Func<TId, int> idToInt, Func<int, TId> intToId, Action<BuildXLWriter, TItem> serializeItem, Func<BuildXLReader, TItem> deserializeItem)
            {
                m_idToInt = idToInt;
                m_intToId = intToId;
                m_serializeItem = serializeItem;
                m_deserializeItem = deserializeItem;
            }

            /// <summary>
            /// Returns the size of the map
            /// </summary>
            public int Count
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_items.Count;
                    }
                }
            }

            /// <summary>
            /// Gets or adds an item.
            /// </summary>
            public TId GetOrAdd(TItem item)
            {
                Contract.Requires(item != null);

                lock (m_lock)
                {
                    TId id;
                    if (!m_itemToKeyMapping.TryGetValue(item, out id))
                    {
                        var index = m_items.Count;
                        id = m_intToId(index);

                        m_items.Add(item);
                        m_itemToKeyMapping.Add(item, id);
                    }

                    return id;
                }
            }

            /// <summary>
            /// Gets an item by the index id.
            /// </summary>
            public TItem Get(TId id)
            {
                Contract.Requires(IsValidId(id));

                lock (m_lock)
                {
                    return m_items[m_idToInt(id)];
                }
            }

            /// <summary>
            /// Checks if a qualifier id is valid with respect to this qualifier table.
            /// </summary>
            [Pure]
            public bool IsValidId(TId id)
            {
                lock (m_lock)
                {
                    var index = m_idToInt(id);
                    return index >= 0 && index< m_items.Count;
                }
            }

            /// <nodoc />
            public void Serialize(BuildXLWriter writer)
            {
                lock (m_lock)
                {
                    var count = m_items.Count;
                    writer.WriteCompact(count);
                    for (var i = 0; i < count; i++)
                    {
                        m_serializeItem(writer, m_items[i]);
                    }
                }
            }

            /// <nodoc />
            public void Deserialize(BuildXLReader reader)
            {
                lock (m_lock)
                {
                    var count = reader.ReadInt32Compact();
                    for (int i = 0; i < count; i++)
                    {
                        var item = m_deserializeItem(reader);
                        m_items.Add(item);
                        m_itemToKeyMapping.Add(item, m_intToId(i));
                    }
                }
            }
        }
    }
}
