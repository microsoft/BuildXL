// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Path mappings used by incremental scheduling.
    /// </summary>
    public sealed class IncrementalSchedulingPathMapping<T>
    {
        /// <summary>
        /// Mappings from paths to a set of values.
        /// </summary>
        /// <remarks>
        /// These mappings must be kept up-to-date.
        /// </remarks>
        private readonly ConcurrentBigMap<AbsolutePath, HashSet<T>> m_pathToValue;

        /// <summary>
        /// This map is an over approximation of value to path, so that we can efficiently remove all entries for a given value from <see cref="m_pathToValue"/>.
        /// It is not the primary source of truth. In other words if there is a path->value entry in <see cref="m_pathToValue"/>, there is a value->path entry in this map,
        /// but not vice versa.
        /// </summary>
        private readonly ConcurrentBigMap<T, List<AbsolutePath>> m_valueToPathApproximation;

        /// <summary>
        /// Creates an instance of <see cref="IncrementalSchedulingPathMapping{T}"/>
        /// </summary>
        public IncrementalSchedulingPathMapping()
            : this(new ConcurrentBigMap<AbsolutePath, HashSet<T>>(), new ConcurrentBigMap<T, List<AbsolutePath>>())
        {
        }

        private IncrementalSchedulingPathMapping(
            ConcurrentBigMap<AbsolutePath, HashSet<T>> pathToValue,
            ConcurrentBigMap<T, List<AbsolutePath>> valueToPathApproximation)
        {
            Contract.Requires(pathToValue != null);
            Contract.Requires(valueToPathApproximation != null);

            m_pathToValue = pathToValue;
            m_valueToPathApproximation = valueToPathApproximation;
        }

        /// <summary>
        /// The number of paths in the map.
        /// </summary>
        public int PathCount => m_pathToValue.Count;

        /// <summary>
        /// Tries to get values given a path.
        /// </summary>
        public bool TryGetValues(AbsolutePath path, out IEnumerable<T> values)
        {
            if (m_pathToValue.TryGetValue(path, out var valueSet))
            {
                values = valueSet;
                return true;
            }

            values = null;
            return false;
        }

        /// <summary>
        /// Removes all bindings to a given value.
        /// </summary>
        public void ClearValue(T value)
        {
            // Run the clear logic in an AddOrUpdate because the HashSet in the value is not thread-safe and this
            // will let the ConcurrentBigMap deal with guarding concurrent access to the HashSet.

            if (m_valueToPathApproximation.ContainsKey(value))
            {
                m_valueToPathApproximation.AddOrUpdate(
                    value,
                    (HashSet<AbsolutePath>)null,
                    (n, nullValue) => new List<AbsolutePath>(),
                    (v, nullValue, paths) =>
                    {
                        foreach (var path in paths)
                        {
                            m_pathToValue.AddOrUpdate(
                                path,
                                v,
                                (_, p) => new HashSet<T>(),
                                (p, _, s) =>
                                {
                                    s.Remove(v);
                                    return s;
                                });
                        }

                        // For the given node we have cleared all paths so we can simply return an empty list here.
                        return new List<AbsolutePath>();
                    });
            }
        }

        /// <summary>
        /// Adds a binding for given value and path.
        /// </summary>
        public void AddEntry(T value, AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            AddHelper(m_pathToValue, path, value);
            AddHelper(m_valueToPathApproximation, value, path);
        }

        /// <summary>
        /// Helper to add a key-value to the set of map
        /// </summary>
        private static void AddHelper<TKey, TValue, TCollection>(
            ConcurrentBigMap<TKey, TCollection> map,
            TKey key,
            TValue value)
            where TCollection : ICollection<TValue>, new()
        {
            map.AddOrUpdate(
                key,
                value,
                (k, v) =>
                {
                    var s = new TCollection { v };
                    return s;
                },
                (k, v, s) =>
                {
                    s.Add(v);
                    return s;
                });
        }

        /// <summary>
        /// Serializes this instance to a writer.
        /// </summary>
        public void Serialize(BuildXLWriter writer, Action<BinaryWriter, T> writeValue)
        {
            Contract.Requires(writer != null);
            Contract.Requires(writeValue != null);

            m_pathToValue.Serialize(
                writer,
                kv =>
                {
                    writer.Write(kv.Key); // path

                    writer.WriteCompact(kv.Value.Count); // nodes
                    foreach (var value in kv.Value)
                    {
                        writeValue(writer, value);
                    }
                });
        }

        /// <summary>
        /// Deserializes an instance of <see cref="IncrementalSchedulingPathMapping{T}"/> from a reader.
        /// </summary>
        public static IncrementalSchedulingPathMapping<T> Deserialize(BuildXLReader reader, Func<BinaryReader, T> readValue)
        {
            Contract.Requires(reader != null);

            var valueToPathApproximation = new ConcurrentBigMap<T, List<AbsolutePath>>();
            var pathToValue = ConcurrentBigMap<AbsolutePath, HashSet<T>>.Deserialize(
                reader,
                () =>
                {
                    var path = reader.ReadAbsolutePath();
                    var valueCount = reader.ReadInt32Compact();
                    var values = new HashSet<T>();

                    for (int i = 0; i < valueCount; i++)
                    {
                        T value = readValue(reader);

                        // Reconstruct the approximation map.
                        AddHelper(valueToPathApproximation, value, path);

                        values.Add(value);
                    }

                    return new ConcurrentBigMapEntry<AbsolutePath, HashSet<T>>(path, values);
                });

            return new IncrementalSchedulingPathMapping<T>(pathToValue, valueToPathApproximation);
        }

        /// <summary>
        /// Writes a textual format of <see cref="IncrementalSchedulingPathMapping{T}"/>.
        /// </summary>
        public void WriteText(
            TextWriter writer, 
            PathTable pathTable, 
            Func<T, string> valueToText, 
            string pathToValuesBanner = null, 
            string valueToPathsBanner = null)
        {
            Contract.Requires(writer != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(valueToText != null);

            var valueToPaths = new Dictionary<T, List<AbsolutePath>>();

            writer.WriteLine(pathToValuesBanner ?? "Path to value mappings");

            foreach (var path in m_pathToValue.Keys.OrderBy(k => k, pathTable.ExpandedPathComparer))
            {
                writer.WriteLine(I($"\t- {path.ToString(pathTable)}"));

                foreach (var value in m_pathToValue[path])
                {
                    writer.WriteLine(
                        I($"\t\t* {valueToText(value)}"));

                    List<AbsolutePath> paths;
                    if (!valueToPaths.TryGetValue(value, out paths))
                    {
                        paths = new List<AbsolutePath>();
                        valueToPaths.Add(value, paths);
                    }

                    paths.Add(path);
                }
            }

            writer.WriteLine(valueToPathsBanner ?? "Value to list of paths mappings");

            foreach (var valueToPath in valueToPaths)
            {
                writer.WriteLine(
                    I($"\t- {valueToText(valueToPath.Key)}"));

                foreach (var path in valueToPath.Value.OrderBy(p => p, pathTable.ExpandedPathComparer))
                {
                    writer.WriteLine(I($"\t\t* {path.ToString(pathTable)}"));
                }
            }
        }
    }
}
