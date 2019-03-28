// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Scheduler.IncrementalScheduling.IncrementalSchedulingStateWriteTextHelpers;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Class tracking clean producers.
    /// </summary>
    internal class PipProducers
    {
        private readonly ConcurrentBigMap<AbsolutePath, PipStableId> m_pipProducers;

        private readonly ConcurrentBigMap<PipStableId, HashSet<AbsolutePath>> m_producedPaths;

        /// <summary>
        /// Creates a new instance of <see cref="PipProducers"/>.
        /// </summary>
        public static PipProducers CreateNew() => new PipProducers(new ConcurrentBigMap<AbsolutePath, PipStableId>(), new ConcurrentBigMap<PipStableId, HashSet<AbsolutePath>>());

        private PipProducers(ConcurrentBigMap<AbsolutePath, PipStableId> pipProducers, ConcurrentBigMap<PipStableId, HashSet<AbsolutePath>> producedPaths)
        {
            Contract.Requires(pipProducers != null);
            Contract.Requires(producedPaths != null);

            m_pipProducers = pipProducers;
            m_producedPaths = producedPaths;
        }

        /// <summary>
        /// Adds the fact that a path is produced by a given producer.
        /// </summary>
        public void Add(AbsolutePath path, PipStableId producer)
        {
            m_producedPaths.AddOrUpdate(
                producer,
                path,
                (pip, pathToAdd) =>
                {
                    var set = new HashSet<AbsolutePath> { pathToAdd };
                    m_pipProducers.TryAdd(pathToAdd, pip);
                    return set;
                },
                (pip, pathToAdd, existingSet) =>
                {
                    if (existingSet.Add(pathToAdd))
                    {
                        m_pipProducers.TryAdd(pathToAdd, pip);
                    }

                    return existingSet;
                });
        }

        /// <summary>
        /// Tries to get producer.
        /// </summary>
        public bool TryGetProducer(AbsolutePath path, out PipStableId producer)
        {
            return m_pipProducers.TryGetValue(path, out producer);
        }

        /// <summary>
        /// Tries to remove producer by path.
        /// </summary>
        public bool TryRemoveProducer(AbsolutePath path, out PipStableId producer) => m_pipProducers.TryGetValue(path, out producer) && TryRemoveProducer(producer);

        /// <summary>
        /// Tries to remove producer.
        /// </summary>
        public bool TryRemoveProducer(PipStableId producer)
        {
            if (m_producedPaths.ContainsKey(producer))
            {
                m_producedPaths.AddOrUpdate(
                    producer,
                    (HashSet<AbsolutePath>)null,
                    (p, nullValue) => new HashSet<AbsolutePath>(),
                    (p, nullValue, existingSet) =>
                    {
                        foreach (var path in existingSet)
                        {
                            m_pipProducers.TryRemove(path, out var dummyId);
                        }

                        return new HashSet<AbsolutePath>();
                    });

                return true;
            }

            return false;
        }

        /// <summary>
        /// Serializes this instance of <see cref="PipProducers"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            m_pipProducers.Serialize(
                writer,
                kv =>
                {
                    writer.Write(kv.Key);
                    writer.Write(kv.Value);
                });
        }

        /// <summary>
        /// Deserializes an instance of <see cref="PipProducers"/>.
        /// </summary>
        public static PipProducers Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var producedPaths = new ConcurrentBigMap<PipStableId, HashSet<AbsolutePath>>();
            var pipProducers = ConcurrentBigMap<AbsolutePath, PipStableId>.Deserialize(
                reader,
                () =>
                {
                    var path = reader.ReadAbsolutePath();
                    var producer = reader.ReadPipStableId();

                    producedPaths.AddOrUpdate(
                        producer,
                        path,
                        (pip, pathToAdd) => new HashSet<AbsolutePath> { pathToAdd },
                        (pip, pathToAdd, existingSet) =>
                        {
                            existingSet.Add(pathToAdd);
                            return existingSet;
                        });

                    return new KeyValuePair<AbsolutePath, PipStableId>(path, producer);
                });

            return new PipProducers(pipProducers, producedPaths);
        }

        public void WriteText(TextWriter writer, PipOrigins pipOrigins, PathTable pathTable)
        {
            Contract.Requires(writer != null);
            Contract.Requires(pipOrigins != null);
            Contract.Requires(pathTable != null);

            WriteTextMap(writer, m_pipProducers, p => p.ToString(pathTable), i => GetPipIdText(pipOrigins, i));
        }
    }
}
