// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Utilities.Qualifier
{
    /// <summary>
    /// Describes one entry in a qualifier type.
    /// </summary>
    public readonly struct QualifierSpaceEntry : IEquatable<QualifierSpaceEntry>
    {
        /// <summary>
        /// Key for an entry in a qualifier type.
        /// </summary>
        public StringId Key { get; }

        /// <summary>
        /// Possible values for an entry in a qualifier type.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public StringId[] Values { get; }

        /// <nodoc/>
        public QualifierSpaceEntry(StringId key, StringId[] values)
        {
            Contract.Requires(key.IsValid);
            Contract.Requires(values != null);
            Contract.Requires(values.Length > 0);
            Contract.RequiresForAll(values, v => v.IsValid);

            Key = key;
            Values = values;
        }

        /// <nodoc/>
        public static QualifierSpaceEntry Create(StringId key, StringId[] values)
        {
            return new QualifierSpaceEntry(key, values);
        }

        /// <summary>
        /// Indicates that the instance is valid and not created using <code>default(QualifierSpaceEntry)</code>.
        /// </summary>
        public bool IsValid => Key.IsValid;

        /// <inheritdoc/>
        public bool Equals(QualifierSpaceEntry other)
        {
            return Key.Equals(other.Key) && Equals(Values, other.Values);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is QualifierSpaceEntry && Equals((QualifierSpaceEntry)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                Key.GetHashCode(),
                HashCodeHelper.Combine(Values, id => id.Value));
        }

        /// <nodoc/>
        public static bool operator ==(QualifierSpaceEntry left, QualifierSpaceEntry right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(QualifierSpaceEntry left, QualifierSpaceEntry right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Qualifier space.
    /// </summary>
    /// <remarks>
    /// A qualifier space represents the allowed legal qualifier instances.
    /// It is conceptually a mapping from keys to lists of legal values, where the first element of
    /// each list is the default value for the corresponding key.
    /// </remarks>
    public sealed class QualifierSpace : IEquatable<QualifierSpace>
    {
        private readonly StringId[] m_keys;
        private readonly StringId[] m_defaults;
        private readonly StringId[][] m_valueValues;
        private readonly int m_hashKey;

        /// <summary>Qualifier keys as <see cref="StringId"/>s.</summary>
        public IReadOnlyList<StringId> Keys => m_keys;

        /// <summary>A ragged 2-D array of qualifier values (each qualifier key is associated with an array of values).</summary>
        public IReadOnlyList<StringId[]> Values => m_valueValues;

        /// <summary>
        /// Defaults, following the same order in <see cref="Keys"/>.
        /// </summary>
        public IReadOnlyList<StringId> Defaults => m_defaults;

        /// <summary>
        /// Qualifier space as a dictionary mapping keys to sets of values.
        /// </summary>
        public Dictionary<StringId, StringId[]> AsDictionary
        {
            get
            {
                var result = new Dictionary<StringId, StringId[]>(m_keys.Length);
                for (int i = 0; i < m_keys.Length; ++i)
                {
                    result[m_keys[i]] = m_valueValues[i];
                }

                return result;
            }
        }

        /// <summary>
        /// Empty qualifier space.
        /// </summary>
        public static readonly QualifierSpace Empty = new QualifierSpace(CollectionUtilities.EmptyArray<StringId>(), CollectionUtilities.EmptyArray<StringId>(), CollectionUtilities.EmptyArray<StringId[]>());

        /// <summary>
        /// Internal constructor.
        /// </summary>
        private QualifierSpace(StringId[] keys, StringId[] defaults, StringId[][] valueValues)
        {
            Contract.Requires(keys != null);
            Contract.Requires(defaults != null);
            Contract.Requires(valueValues != null);
            Contract.Requires(keys.Length == valueValues.Length);
            Contract.Requires(keys.Length == defaults.Length);
            Contract.RequiresForAll(keys, key => key.IsValid);
            Contract.RequiresForAll(valueValues, valueValue => valueValue != null && valueValue.Length > 0);

            m_keys = keys;
            m_defaults = defaults;
            m_valueValues = valueValues;

            m_hashKey = ComputeHashCode();
        }

        /// <summary>
        /// Creates a qualifier space given mappings from keys to list of values.
        /// </summary>
        /// <remarks>
        /// This is internal so that creation has to happen via qualifier table which allows us to cache the instances and share memory.
        /// The underlying array is mutated and can't be relied upon afterwards.
        /// </remarks>
        internal static QualifierSpace CreateQualifierSpace(StringTable stringTable, QualifierSpaceEntry[] entries)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(entries != null);

            Array.Sort(entries, new QualifierSpaceEntryComparerByKey(stringTable));

            var keys = new List<StringId>(entries.Length);
            var defaults = new List<StringId>(entries.Length);
            var values = new List<StringId[]>(entries.Length);

            for (int i = 0; i < entries.Length; i++)
            {
                var key = entries[i].Key;
                var defaultValue = entries[i].Values[0];
                var sortedValues = entries[i].Values;
                Array.Sort(sortedValues, stringTable.OrdinalComparer);

                if (i > 0 && key == keys[keys.Count - 1])
                {
                    defaults[defaults.Count - 1] = defaultValue;
                    values[values.Count - 1] = sortedValues;
                }
                else
                {
                    keys.Add(key);
                    defaults.Add(defaultValue);
                    values.Add(sortedValues);
                }
            }

            return new QualifierSpace(keys.ToArray(), defaults.ToArray(), values.ToArray());
        }

        /// <inheritdoc />
        public bool Equals(QualifierSpace other)
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

                var values = m_valueValues[i];
                var otherValues = other.m_valueValues[i];
                if (values.Length != otherValues.Length)
                {
                    return false;
                }

                for (int j = 0; j < values.Length; j++)
                {
                    if (!values[j].Equals(otherValues[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as QualifierSpace);
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
                builder.Append(":[");
                var values = m_valueValues[i];
                for (int j = 0; j < values.Length; j++)
                {
                    if (j > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append('\"');
                    builder.Append(stringTable.GetString(values[j]));
                    builder.Append('\"');
                }

                builder.Append("]");
            }

            builder.Append("}");
            return builder.ToString();
        }

        /// <summary>
        /// Creates a qualifier from a given input qualifier such that the resulting qualifier respects this qualifier space.
        /// </summary>
        internal bool TryCreateQualifierForQualifierSpace(
            StringTable stringTable,
            PathTable pathTable,
            LoggingContext loggingContext,
            Qualifier currentQualifier,
            out Qualifier qualifier,
            out UnsupportedQualifierValue error,
            bool useDefaults)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(loggingContext != null);

            StringId[] keys;
            StringId[] values;
            currentQualifier.GetInternalKeyValueArrays(out keys, out values);

            var targetKeys = new StringId[m_keys.Length];
            var targetValues = new StringId[m_keys.Length];

            int qIndex = 0;
            int sIndex = 0;
            while (sIndex < m_keys.Length)
            {
                var sKey = m_keys[sIndex];

                if (qIndex < keys.Length && keys[qIndex] == sKey)
                {
                    var qValue = values[qIndex];
                    if (m_valueValues[sIndex].Contains(qValue))
                    {
                        targetKeys[sIndex] = keys[qIndex];
                        targetValues[sIndex] = qValue;

                        qIndex++;
                        sIndex++;
                    }
                    else
                    {
                        error = new UnsupportedQualifierValue
                        {
                            QualifierKey = sKey.ToString(stringTable),
                            InvalidValue = qValue.ToString(stringTable),
                            LegalValues = string.Join(", ", m_valueValues[sIndex].Select(id => id.ToString(stringTable))),
                        };

                        qualifier = Qualifier.Empty;
                        return false;
                    }
                }
                else
                {
                    // Check if we have any key from the currentQualifier left. If not, we'll 'trick' the compare below be a 'missing'
                    // value we can treat it to insert the default value for the remaining space keys
                    var compare = qIndex < keys.Length ? stringTable.OrdinalComparer.Compare(keys[qIndex], sKey) : 1;
                    Contract.Assume(compare != 0, "expected above equals to handle that case");

                    if (compare < 0)
                    {
                        // Given that the lists are sorted and the qualifier key is less than the space key, it means that key is not in the target space.
                        // so we can skip it.
                        qIndex++;
                    }
                    else
                    {
                        if (useDefaults == false)
                        {
                            qualifier = Qualifier.Empty;

                            // var lineInfo = location.Value.ToLogLocation(pathTable);
                            // TODO: Consider adding a more specific exception for the no defaults case
                            error = new UnsupportedQualifierValue
                                    {
                                        // Location = lineInfo,
                                        QualifierKey = sKey.ToString(stringTable),
                                        InvalidValue = string.Empty,
                                        LegalValues = string.Join(", ", m_valueValues[sIndex].Select(id => id.ToString(stringTable))),
                                    };

                            return false;
                        }

                        // It is larger, so we need to add the default value of the space to the target if enabled.
                        targetKeys[sIndex] = sKey;
                        targetValues[sIndex] = m_defaults[sIndex];

                        sIndex++;
                    }
                }
            }

            qualifier = new Qualifier(targetKeys, targetValues);
            error = default(UnsupportedQualifierValue);

            return true;
        }

        private int ComputeHashCode()
        {
            var result = HashCodeHelper.Combine(0, m_keys.Length);

            for (int i = 0; i < m_keys.Length; i++)
            {
                result = HashCodeHelper.Combine(result, m_keys[i].Value);
                result = HashCodeHelper.Combine(result, m_defaults[i].Value);

                var values = m_valueValues[i];
                result = HashCodeHelper.Combine(result, values.Length);

                for (int j = 0; j < values.Length; j++)
                {
                    result = HashCodeHelper.Combine(result, values[j].Value);
                }
            }

            return result;
        }

        /// <summary>
        /// Helper to compare the qualifier space key in the key-value map.
        /// </summary>
        private readonly struct QualifierSpaceEntryComparerByKey : IComparer<QualifierSpaceEntry>
        {
            private readonly StringTable m_stringTable;

            public QualifierSpaceEntryComparerByKey(StringTable stringTable)
            {
                Contract.Requires(stringTable != null);
                m_stringTable = stringTable;
            }

            /// <inheritdoc />
            public int Compare(QualifierSpaceEntry x, QualifierSpaceEntry y)
            {
                return m_stringTable.OrdinalComparer.Compare(x.Key, y.Key);
            }
        }


        /// <nodoc />
        public static QualifierSpace Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var keys = reader.ReadArray(r => r.ReadStringId());
            var defaults = reader.ReadArray(r => r.ReadStringId());
            var valueValues = reader.ReadArray(r => r.ReadArray(r2 => r2.ReadStringId()));

            return new QualifierSpace(keys, defaults, valueValues);
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(m_keys, (w, item) => w.Write(item));
            writer.Write(m_defaults, (w, item) => w.Write(item));
            writer.Write(m_valueValues, (w, itemList) => w.Write(itemList, (w2, item) => w2.Write(item)));
        }
    }
}
