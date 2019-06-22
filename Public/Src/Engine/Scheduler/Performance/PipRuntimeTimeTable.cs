// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// A table mapping semi stable hashes to unsigned integers.
    /// </summary>
    /// <remarks>
    /// The mapping is not precise --- multiple semi stable hashes may be mapped to the same value.
    /// </remarks>
    // TODO: Loading and Saving involves a bunch of unnecessary copies.
    // Consider using memory-mapped files instead, or a full-blown database without loosing any further information.
    public sealed class PipRuntimeTimeTable
    {
        /// <summary>
        /// The version for format of <see cref="PipRuntimeTimeTable"/>
        /// </summary>
        public const int FormatVersion = 0;

        private static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "Runtime", version: FormatVersion);

        /// <summary>
        /// The data holder
        /// </summary>
        private readonly ConcurrentBigMap<long, PipHistoricPerfData> m_runtimeData;

        /// <summary>
        /// Number of Process pip nodes for which critical path duration suggestions were added
        /// </summary>
        private long m_numRunningTimeAdded;

        /// <summary>
        /// Number of Process pip nodes for which critical path duration suggestions were updated
        /// </summary>
        private long m_numRunningTimeUpdated;

        /// <summary>
        /// Sum of percentage points of deviation between actually observed and estimated process running times
        /// </summary>
        private long m_sumRelativeRunningTimeDeviation;

        /// <summary>
        /// Number of prior-runtime query hits.
        /// </summary>
        private long m_numHits;

        /// <summary>
        /// Number of prior-runtime query misses (no data).
        /// </summary>
        private long m_numMisses;

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public PipRuntimeTimeTable(int initialCapacity = 0)
        {
            m_runtimeData = new ConcurrentBigMap<long, PipHistoricPerfData>(capacity: initialCapacity);
        }

        /// <summary>
        /// Opens a pip cost hash table from a file.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while operating on the file.
        /// </exception>
        public static PipRuntimeTimeTable Load(string fileName)
        {
            Contract.Requires(fileName != null);
            using (
                FileStream fileStream = FileUtilities.CreateFileStream(
                    fileName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete))
            {
                return Load(fileStream);
            }
        }

        internal static PipRuntimeTimeTable Load(Stream stream)
        {
            return ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    Analysis.IgnoreResult(FileEnvelope.ReadHeader(stream));
                    using (BuildXLReader reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: true))
                    {
                        int size = reader.ReadInt32();
                        var table = new PipRuntimeTimeTable(initialCapacity: size);

                        for (int i = 0; i < size; ++i)
                        {
                            long semiStableHash = reader.ReadInt64();
                            PipHistoricPerfData historicData;
                            if (PipHistoricPerfData.Deserialize(reader, out historicData))
                            {
                                if (!table.m_runtimeData.TryAdd(semiStableHash, historicData))
                                {
                                    throw new BuildXLException("Corrupted file has duplicate records");
                                }
                            }
                        }

                        return table;
                    }
                },
                ex => { throw new BuildXLException("Reading of file failed", ex); });
        }

        /// <summary>
        /// Number of elements distinguished by this table.
        /// </summary>
        public int Count => m_runtimeData.Count;

        /// <summary>
        /// Saves a pip cost hash table to a file.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while operating on the file.
        /// </exception>
        public void Save(string fileName)
        {
            Contract.Requires(fileName != null);
            using (FileStream fileStream = FileUtilities.CreateFileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Delete))
            {
                Save(fileStream);
            }
        }

        internal void Save(Stream stream)
        {
            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    // We don't have anything in particular to correlate this file to,
                    // so we are simply creating a unique correlation id that is used as part
                    // of the header consistency check.
                    FileEnvelopeId correlationId = FileEnvelopeId.Create();
                    FileEnvelope.WriteHeader(stream, correlationId);

                    using (BuildXLWriter writer = new BuildXLWriter(debug: false, stream: stream, leaveOpen: true, logStats: false))
                    {
                        writer.Write(m_runtimeData.Count);
                        foreach (var kvp in m_runtimeData)
                        {
                            writer.Write(kvp.Key);
                            kvp.Value.Serialize(writer);
                        }
                    }

                    FileEnvelope.FixUpHeader(stream, correlationId);
                    return (object)null;
                },
                ex => { throw new BuildXLException("Writing of file failed", ex); });
        }

        /// <summary>
        /// Access the runtime data
        /// </summary>
        public PipHistoricPerfData this[long semiStableHash]
        {
            get
            {
                PipHistoricPerfData ret;
                if (m_runtimeData.TryGetValue(semiStableHash, out ret))
                {
                    Interlocked.Increment(ref m_numHits);

                    if (ret.IsFresh)
                    {
                        return ret;
                    }

                    PipHistoricPerfData freshRet = ret.MakeFresh();
                    m_runtimeData[semiStableHash] = freshRet;

                    return freshRet;
                }
                else
                {
                    Interlocked.Increment(ref m_numMisses);
                    return default(PipHistoricPerfData);
                }
            }

            set
            {
                var result = m_runtimeData.AddOrUpdate(
                    semiStableHash,
                    value,
                    (key, val) => val,
                    (key, val, oldValue) => val.Merge(oldValue));

                if (result.IsFound)
                {
                    uint oldMilliseconds = result.OldItem.Value.DurationInMs;
                    uint milliseconds = value.DurationInMs;
                    var difference = milliseconds > oldMilliseconds ? milliseconds - oldMilliseconds : oldMilliseconds - milliseconds;
                    var relativeDeviation = (int)(difference * 100 / Math.Max(milliseconds, oldMilliseconds));
                    Interlocked.Add(ref m_sumRelativeRunningTimeDeviation, relativeDeviation);
                    Interlocked.Increment(ref m_numRunningTimeUpdated);
                    Tracing.Logger.Log.RunningTimeUpdated(Events.StaticContext, semiStableHash, milliseconds, oldMilliseconds, relativeDeviation);
                }
                else
                {
                    Interlocked.Increment(ref m_numRunningTimeAdded);
                    Tracing.Logger.Log.RunningTimeAdded(Events.StaticContext, semiStableHash, value.DurationInMs);
                }
            }
        }

        /// <summary>
        /// Log the gathered statistics
        /// </summary>
        public void LogStats(LoggingContext loggingContext)
        {
            var sumRelativeRuntimeDeviation = Volatile.Read(ref m_sumRelativeRunningTimeDeviation);
            var numRunningTimeUpdated = Volatile.Read(ref m_numRunningTimeUpdated);

            Tracing.Logger.Log.RunningTimeStats(
                loggingContext,
                Volatile.Read(ref m_numHits),
                Volatile.Read(ref m_numMisses),
                Volatile.Read(ref m_numRunningTimeAdded),
                numRunningTimeUpdated,
                numRunningTimeUpdated == 0 ? 0 : (int)(sumRelativeRuntimeDeviation / numRunningTimeUpdated));
        }
    }
}
