// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Qualifier
{
    /// <summary>
    /// Qualifier.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    public sealed class Qualifier : IEquatable<Qualifier>
    {
        private readonly StringId[] m_keys;
        private readonly StringId[] m_values;
        private readonly int m_hashKey;

        /// <summary>
        /// Keys.
        /// </summary>
        public IReadOnlyList<StringId> Keys => m_keys;

        /// <summary>
        /// Values.
        /// </summary>
        public IReadOnlyList<StringId> Values => m_values;

        /// <summary>
        /// Empty qualifier instance.
        /// </summary>
        public static Qualifier Empty { get; } = new Qualifier(CollectionUtilities.EmptyArray<StringId>(), CollectionUtilities.EmptyArray<StringId>());

        /// <summary>
        /// Internal constructor.
        /// </summary>
        /// <remarks>
        /// It might seem very restrictive to allow only a private constructor with keys here.
        /// The reasoning is that we are going to get a lot of instances of the qualifiers used in builds with very few variation
        /// but many many lookups. Therefore we want to keep internal details so we allow ourselves to optimize the lookups
        /// as fast as possible.
        /// </remarks>
        internal Qualifier(StringId[] keys, StringId[] values)
        {
            Contract.Requires(keys != null);
            Contract.Requires(values != null);
            Contract.Requires(keys.Length == values.Length);
            Contract.RequiresForAll(keys, key => key.IsValid);
            Contract.RequiresForAll(values, value => value.IsValid);

            // Contract: Keys must be a sorted list.
            // One can create a function that checks for sortedness to validate the contract.
            m_keys = keys;
            m_values = values;

            m_hashKey = 0;
            for (int i = 0; i < m_keys.Length; i++)
            {
                m_hashKey = HashCodeHelper.Combine(m_hashKey, m_keys[i].Value, m_values[i].Value);
            }
        }

        /// <summary>
        /// Creates a qualifier by appending or updating this qualifier with a new pair of key and value.
        /// </summary>
        /// <remarks>
        /// Should only be called by qualifier table so it can be cached. This qualifier will be returned
        /// if the key-value pair already exists.
        /// </remarks>
        internal Qualifier CreateQualifierWithValue(StringTable stringTable, StringId key, StringId value)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(key.IsValid);
            Contract.Requires(value.IsValid);

            StringId[] newValues;

            int insertIndex;
            for (insertIndex = 0; insertIndex < m_keys.Length; insertIndex++)
            {
                var currentKey = m_keys[insertIndex];
                var compare = stringTable.OrdinalComparer.Compare(currentKey, key);

                // We already have the key in the list.
                if (compare == 0)
                {
                    // Check if the value is the same.
                    if (value == m_values[insertIndex])
                    {
                        return this;
                    }

                    // If not, update the values.
                    newValues = new StringId[m_values.Length];
                    Array.Copy(m_values, newValues, m_values.Length);
                    newValues[insertIndex] = value;

                    return new Qualifier(m_keys, newValues);
                }

                if (compare > 0)
                {
                    break;
                }
            }

            // Insert into the array
            var newKeys = InsertIntoArray(m_keys, insertIndex, key);
            newValues = InsertIntoArray(m_values, insertIndex, value);

            return new Qualifier(newKeys, newValues);
        }

        /// <summary>
        /// Creates a qualifier with keys and values defined in a given key-value pairs.
        /// </summary>
        /// <remarks>
        /// This is internal so that creation has to happen via qualifier table which allows us to cache the instances and share memory.
        /// The underlying array is mutated and can't be relied upon afterwards.
        /// </remarks>
        internal static Qualifier CreateQualifier(StringTable stringTable, Tuple<StringId, StringId>[] keyValuePairs)
        {
            Contract.Requires(stringTable != null);
            Contract.RequiresForAll(keyValuePairs, pair => pair.Item1.IsValid && pair.Item2.IsValid);

            Array.Sort(keyValuePairs, new QualifierKeyComparer(stringTable));

            var keys = new List<StringId>(keyValuePairs.Length);
            var values = new List<StringId>(keyValuePairs.Length);

            for (int i = 0; i < keyValuePairs.Length; ++i)
            {
                if (i > 0 && keyValuePairs[i].Item1 == keys[keys.Count - 1])
                {
                    values[values.Count - 1] = keyValuePairs[i].Item2;
                }
                else
                {
                    keys.Add(keyValuePairs[i].Item1);
                    values.Add(keyValuePairs[i].Item2);
                }
            }

            return new Qualifier(keys.ToArray(), values.ToArray());
        }

        /// <summary>
        /// Internal helper to get to the keys and values.
        /// </summary>
        internal void GetInternalKeyValueArrays(out StringId[] keys, out StringId[] values)
        {
            keys = m_keys;
            values = m_values;
        }

        private static TItem[] InsertIntoArray<TItem>(TItem[] list, int insertIndex, TItem value)
        {
            Contract.Requires(list != null);

            var newList = new TItem[list.Length + 1];

            if (insertIndex > 0)
            {
                Array.Copy(list, 0, newList, 0, insertIndex);
            }

            newList[insertIndex] = value;

            if (list.Length > insertIndex)
            {
                Array.Copy(list, insertIndex, newList, insertIndex + 1, list.Length - insertIndex);
            }

            return newList;
        }

        /// <inheritdoc />
        public bool Equals(Qualifier other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (m_hashKey != other.m_hashKey)
            {
                return false;
            }

            if (m_keys.Length != other.m_keys.Length)
            {
                return false;
            }

            for (int i = 0; i < m_keys.Length; i++)
            {
                if (!m_keys[i].Equals(other.m_keys[i]))
                {
                    return false;
                }

                if (!m_values[i].Equals(other.m_values[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as Qualifier);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_hashKey;
        }

        /// <summary>
        /// Helper to print the value.
        /// </summary>
        public string ToDisplayString(StringTable stringTable)
        {
            Contract.Requires(stringTable != null);

            var builder = new StringBuilder();
            builder.Append("{");
            for (int i = 0; i < m_keys.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(stringTable.GetString(m_keys[i]));
                builder.Append(":\"");
                builder.Append(stringTable.GetString(m_values[i]));
                builder.Append("\"");
            }

            builder.Append("}");
            return builder.ToString();
        }

        /// <summary>
        /// Tries to retreive a key from the qualifier
        /// </summary>
        public bool TryGetValue(StringTable stringTable, StringId key, out StringId value)
        {
            var comparer = stringTable.OrdinalComparer;
            for (int i = 0; i < m_keys.Length; i++)
            {
                if (comparer.Compare(m_keys[i], key) == 0)
                {
                    value = m_values[i];
                    return true;
                }
            }

            value = StringId.Invalid;
            return false;
        }

        /// <summary>
        /// Tries to retreive a key from the qualifier
        /// </summary>
        public bool TryGetValue(StringTable stringTable, string key, out string value)
        {
            if (TryGetValue(stringTable, StringId.Create(stringTable, key), out var valueId))
            {
                value = valueId.ToString(stringTable);
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Helper to compare the qualifier key in the key-value map.
        /// </summary>
        private readonly struct QualifierKeyComparer : IComparer<Tuple<StringId, StringId>>
        {
            private readonly StringTable m_stringTable;

            /// <summary>
            /// Constructor
            /// </summary>
            public QualifierKeyComparer(StringTable stringTable)
            {
                Contract.Requires(stringTable != null);
                m_stringTable = stringTable;
            }

            /// <inheritdoc />
            public int Compare(Tuple<StringId, StringId> x, Tuple<StringId, StringId> y)
            {
                return m_stringTable.OrdinalComparer.Compare(x.Item1, y.Item1);
            }
        }

        /// <nodoc />
        public static Qualifier Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var keys = reader.ReadArray(r => r.ReadStringId());
            var values = reader.ReadArray(r => r.ReadStringId());

            return new Qualifier(keys, values);
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteReadOnlyList(Keys, (w, key) => w.Write(key));
            writer.WriteReadOnlyList(Values, (w, value) => w.Write(value));
        }
    }
}
