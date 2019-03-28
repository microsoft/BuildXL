// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
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
        /// The current file format is as follows;
        /// [0-3]               'CRTT'
        /// [4-7]               0 - little endian; anything else: big endian
        /// [8-11]              version number
        /// [12-15]             size (number of 4-byte entries following)
        /// [16-(16+size*4-1)]  entries
        /// </summary>
        private static readonly byte[] s_fileFormatMarker =
            new[]
            {
                // format   
                (byte) 'C', (byte) 'R', (byte) 'T', (byte) 'T',
                // version
                (byte) 1, (byte) 0, (byte) 0, (byte) 0,
            };

        /// <summary>
        /// The data holder
        /// </summary>
        private ConcurrentBigMap<long, PipHistoricPerfData> m_runtimeData = new ConcurrentBigMap<long, PipHistoricPerfData>();

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
        /// Opens a pip cost hash table from a file.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while operating on the file.
        /// </exception>
        public static async Task<PipRuntimeTimeTable> LoadAsync(string fileName)
        {
            Contract.Requires(fileName != null);
            using (
                FileStream fileStream = FileUtilities.CreateAsyncFileStream(
                    fileName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete))
            {
                return await LoadAsync(fileStream);
            }
        }

        internal static Task<PipRuntimeTimeTable> LoadAsync(Stream fileStream)
        {
            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                {
                    int size = await ReadFileFormatMarkerAsync(fileStream);
                    var table = new PipRuntimeTimeTable();

                    using (BuildXLReader reader = new BuildXLReader(false, fileStream, true))
                    {
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
                    }

                    if (fileStream.Position != fileStream.Length)
                    {
                        throw new BuildXLException("Corrupted file has excess bytes");
                    }

                    return table;
                },
                ex => { throw new BuildXLException("Reading of file failed", ex); });
        }

        private static async Task<int> ReadFileFormatMarkerAsync(Stream fileStream)
        {
            using (PooledObjectWrapper<byte[]> wrapper = Pools.GetByteArray(Math.Max(s_fileFormatMarker.Length, sizeof(int))))
            {
                byte[] buffer = wrapper.Instance;
                int bytesRead = await fileStream.ReadAsync(buffer, 0, s_fileFormatMarker.Length);
                if (bytesRead != s_fileFormatMarker.Length)
                {
                    goto badFileFormatMarker;
                }

                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] != s_fileFormatMarker[i])
                    {
                        goto badFileFormatMarker;
                    }
                }

                bytesRead = await fileStream.ReadAsync(buffer, 0, sizeof(int));
                if (bytesRead != sizeof(int))
                {
                    goto badFileFormatMarker;
                }

                bool isLittleEndian = (buffer[0] | buffer[1] | buffer[2] | buffer[3]) == 0;
                if (isLittleEndian != BitConverter.IsLittleEndian)
                {
                    // TODO: Once we have a platform where this actually happens, implement it!
                    throw new BuildXLException("Endian not supported");
                }

                bytesRead = await fileStream.ReadAsync(buffer, 0, sizeof(int));
                if (bytesRead != sizeof(int))
                {
                    goto badFileFormatMarker;
                }

                return BitConverter.ToInt32(buffer, 0);
            }

            badFileFormatMarker:
            throw new BuildXLException("Bad file format marker");
        }

        /// <summary>
        /// Number of elements distinguished by this table.
        /// </summary>
        public int Count
        {
            get { return m_runtimeData.Count; }
        }

        /// <summary>
        /// Saves a pip cost hash table to a file.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while operating on the file.
        /// </exception>
        public async Task SaveAsync(string fileName)
        {
            Contract.Requires(fileName != null);
            using (FileStream fileStream = FileUtilities.CreateAsyncFileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Delete))
            {
                await SaveAsync(fileStream);
            }
        }

        internal Task SaveAsync(Stream fileStream)
        {
            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                      {
                          await WriteFileFormatMarkerAsync(fileStream, m_runtimeData.Count);

                          using (BuildXLWriter writer = new BuildXLWriter(false, fileStream, true, false))
                          {
                              foreach (KeyValuePair<long, PipHistoricPerfData> kvp in m_runtimeData)
                              {
                                  writer.Write(kvp.Key);
                                  kvp.Value.Serialize(writer);
                              }
                          }

                          // Truncate, just in case file already existed but was bigger
                          fileStream.SetLength(fileStream.Position);
                          return (object) null;
                      },
                ex => { throw new BuildXLException("Writing of file failed", ex); });
        }

        private async Task WriteFileFormatMarkerAsync(Stream fileStream, int count)
        {
            await fileStream.WriteAsync(s_fileFormatMarker, 0, s_fileFormatMarker.Length);
            using (PooledObjectWrapper<byte[]> wrapper = Pools.GetByteArray(sizeof(int)))
            {
                byte[] buffer = wrapper.Instance;
                buffer[0] = (byte) (BitConverter.IsLittleEndian ? 0 : 1);
                await fileStream.WriteAsync(buffer, 0, sizeof(int));
            }

            byte[] sizeBytes = BitConverter.GetBytes(count);
            await fileStream.WriteAsync(sizeBytes, 0, sizeof(int));
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
                    if (ret.IsFresh)
                    {
                        return ret;
                    }

                    PipHistoricPerfData freshRet = ret.Merge(ret);
                    m_runtimeData.TryUpdate(semiStableHash, freshRet, ret);
                    return freshRet;
                }

                return default(PipHistoricPerfData);
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
                }
                else
                {
                    Interlocked.Increment(ref m_numRunningTimeAdded);
                }
            }
        }
    }
}
