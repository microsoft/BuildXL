// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Execution.Analyzer.Xldb;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;
using Google.Protobuf;
using PipType = BuildXL.Pips.Operations.PipType;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeXLGToDBAnalyzer()
        {
            string outputDirPath = null;
            bool removeDirPath = false;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDir", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirPath = ParseSingletonPathOption(opt, outputDirPath);
                }
                else if (opt.Name.Equals("removeDir", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("r", StringComparison.OrdinalIgnoreCase))
                {
                    removeDirPath = true;
                }
                else
                {
                    throw Error("Unknown option for event stats analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputDirPath))
            {
                throw Error("/outputDir parameter is required");
            }

            if (Directory.Exists(outputDirPath) && Directory.EnumerateFileSystemEntries(outputDirPath).Any())
            {
                if (removeDirPath)
                {
                    Console.WriteLine("Deleting directory since the 'removeDir' flag was set");
                    Directory.Delete(outputDirPath, true);
                }
                else
                {
                    throw Error("Directory provided exists, is non-empty, and removeDir flag was not passed in. Aborting analyzer.");
                }
            }

            return new XLGToDBAnalyzer(GetAnalysisInput())
            {
                OutputDirPath = outputDirPath,
            };
        }

        /// <summary>
        /// Write the help message when the analyzer is invoked with the /help flag
        /// </summary>
        private static void WriteXLGToDBHelp(HelpWriter writer)
        {
            writer.WriteBanner("XLG to DB \"Analyzer\"");
            writer.WriteModeOption(nameof(AnalysisMode.XlgToDb), "Dumps event data from the xlg into a database.");
            writer.WriteOption("outputDir", "Required. The new directory to write out the RocksDB database", shortName: "o");
            writer.WriteOption("removeDir", "Optional. Boolean if you wish to delete the 'output' directory if it already exists. Defaults to false if left unset", shortName: "r");
        }
    }

    internal sealed class XLGToDBAnalyzer : Analyzer
    {
        private XLGToDBAnalyzerInner m_inner;
        private ActionBlockSlim<Action> m_actionBlock;
        public string OutputDirPath
        {
            set => m_inner.OutputDirPath = value;
        }

        public XLGToDBAnalyzer(AnalysisInput input)
            : base(input)
        {
            m_inner = new XLGToDBAnalyzerInner(input);
            m_actionBlock = new ActionBlockSlim<Action>(12, action => action());
        }

        /// <inheritdoc/>
        public override bool CanHandleEvent(Scheduler.Tracing.ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            return m_inner.CanHandleEvent(eventId, workerId, timestamp, eventPayloadSize);
        }

        /// <inheritdoc/>
        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            var workerId = CurrentEventWorkerId;
            m_actionBlock.Post(() =>
            {
                m_inner.ProcessEvent(data, workerId);
            });
        }

        /// <inheritdoc/>
        public override void Prepare()
        {
            m_inner.Prepare();
        }

        /// <inheritdoc/>
        public override int Analyze()
        {
            m_actionBlock.Complete();
            m_actionBlock.CompletionAsync().GetAwaiter().GetResult();
            return m_inner.Analyze();
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            m_inner.Dispose();
        }
    }


    /// <summary>
    /// Analyzer to dump xlg events and other data into RocksDB
    /// </summary>
    internal sealed class XLGToDBAnalyzerInner : Analyzer
    {
        /// <summary>
        /// Output directory path
        /// </summary>
        public string OutputDirPath;

        /// <summary>
        /// Store WorkerID.Value to pass into protobuf object to identify this event
        /// </summary>
        public ThreadLocal<uint> WorkerID = new ThreadLocal<uint>();

        private bool m_accessorSucceeded;

        private KeyValueStoreAccessor m_accessor;

        private Stopwatch m_stopWatch;

        private int m_eventCount;

        private Dictionary<Scheduler.Tracing.ExecutionEventId, EventCountByTypeValue> m_eventCountByType = new Dictionary<Scheduler.Tracing.ExecutionEventId, EventCountByTypeValue>();

        public XLGToDBAnalyzerInner(AnalysisInput input) : base(input)
        {
            m_stopWatch = new Stopwatch();
            m_stopWatch.Start();
        }

        internal void ProcessEvent<TEventData>(TEventData data, uint workerId) where TEventData : struct, IExecutionLogEventData<TEventData>
        {
            var eventCount = Interlocked.Increment(ref m_eventCount);

            if (eventCount % 100000 == 0)
            {
                Console.WriteLine($"Processed {eventCount} events so far. {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds have elapsed.");
            }

            WorkerID.Value = workerId;
            data.Metadata.LogToTarget(data, this);
        }

        /// <inheritdoc/>
        public override void Prepare()
        {
            string[] additionalColumns = { XldbDataStore.EventColumnFamilyName, XldbDataStore.PipColumnFamilyName, XldbDataStore.StaticGraphColumnFamilyName };
            var accessor = KeyValueStoreAccessor.Open(storeDirectory: OutputDirPath, additionalColumns: additionalColumns);

            if (accessor.Succeeded)
            {
                m_accessor = accessor.Result;
                m_accessorSucceeded = true;
            }
            else
            {
                Console.Error.WriteLine("Could not access RocksDB datastore. Exiting analyzer.");
            }

            m_eventCount = 0;
        }

        /// <inheritdoc/>
        public override int Analyze()
        {
            Console.WriteLine($"Total number of events ingested = {m_eventCount}");
            Console.WriteLine($"Total time for event ingestion: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");
            var ec = new EventCount
            {
                Value = (uint)m_eventCount
            };

            // Hold only one lock while inserting all of these keys into the DB
            Analysis.IgnoreResult(
                m_accessor.Use(database =>
                {
                    foreach (var kvp in m_eventCountByType)
                    {
                        var eventCountByTypeQuery = new EventCountByTypeKey
                        {
                            EventTypeID = (Xldb.ExecutionEventId)(kvp.Key + 1)
                        };

                        database.Put(eventCountByTypeQuery.ToByteArray(), kvp.Value.ToByteArray());
                    }
                })
            );
            
            WriteToDb(Encoding.ASCII.GetBytes(XldbDataStore.EventCountKey), ec.ToByteArray());

            Console.WriteLine("\nEvent data ingested into RocksDB. Starting to ingest static graph data ...\n");

            var pipTable = CachedGraph.PipTable.ToPipTable();

            var graphMetadata = new CachedGraphQuery
            {
                PipTable = true
            };

            WriteToDb(graphMetadata.ToByteArray(), pipTable.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            IngestAllPips();
            Console.WriteLine($"\nAll pips ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            Console.WriteLine("\nStarting to ingest PipGraph.");
            var xldbPipGraph = CachedGraph.PipGraph.ToPipGraph(PathTable);

            graphMetadata = new CachedGraphQuery
            {
                PipGraph = true
            };

            WriteToDb(graphMetadata.ToByteArray(), xldbPipGraph.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            Console.WriteLine($"\nPip graph ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");
            return 0;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            m_accessor.Dispose();
        }

        /// <inheritdoc/>
        public override bool CanHandleEvent(Scheduler.Tracing.ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {

            if (m_eventCountByType.TryGetValue(eventId, out var eventCountVal))
            {
                eventCountVal.WorkerToCountMap.TryGetValue(workerId, out var count);
                count++;
                eventCountVal.WorkerToPayloadMap.TryGetValue(workerId, out var payload);
                payload += eventPayloadSize;

                eventCountVal.WorkerToCountMap[workerId] = count;
                eventCountVal.WorkerToPayloadMap[workerId] = payload;

            }
            else
            {
                eventCountVal = new EventCountByTypeValue();
                eventCountVal.WorkerToCountMap.Add(workerId, 1);
                eventCountVal.WorkerToPayloadMap.Add(workerId, eventPayloadSize);
                m_eventCountByType.Add(eventId, eventCountVal);
            }

            return (m_accessorSucceeded && eventId != Scheduler.Tracing.ExecutionEventId.ObservedInputs);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var fileArtifactContentDecidedEvent = data.ToFileArtifactContentDecidedEvent(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.FileArtifactContentDecided,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), fileArtifactContentDecidedEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void WorkerList(WorkerListEventData data)
        {
            var workerListEvent = data.ToWorkerListEvent(WorkerID.Value);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.WorkerList,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), workerListEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = data.ToPipExecutionPerformanceEvent();
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipExecutionPerformance,
                PipId = data.PipId.Value,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), pipExecPerfEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            var directoryMembershipEvent = data.ToDirectoryMembershipHashedEvent(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.DirectoryMembershipHashed,
                PipId = data.PipId.Value,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), directoryMembershipEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            var processExecMonitoringReportedEvent = data.ToProcessExecutionMonitoringReportedEvent(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ProcessExecutionMonitoringReported,
                PipId = data.PipId.Value,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), processExecMonitoringReportedEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var processFingerprintComputedEvent = data.ToProcessFingerprintComputationEvent(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ProcessFingerprintComputation,
                PipId = data.PipId.Value,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), processFingerprintComputedEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ExtraEventDataReported(ExtraEventData data)
        {
            var extraEvent = data.ToExtraEventDataReported(WorkerID.Value);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ExtraEventDataReported,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), extraEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            var dependencyViolationEvent = data.ToDependencyViolationReportedEvent(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.DependencyViolationReported,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), dependencyViolationEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            var pipExecStepPerformanceEvent = data.ToPipExecutionStepPerformanceReportedEvent(WorkerID.Value);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipExecutionStepPerformanceReported,
                PipId = data.PipId.Value,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), pipExecStepPerformanceEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipCacheMiss(PipCacheMissEventData data)
        {
            var pipCacheMissEvent = data.ToPipCacheMissEvent(WorkerID.Value);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipCacheMiss,
                PipId = data.PipId.Value,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), pipCacheMissEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void StatusReported(StatusEventData data)
        {
            var statusReportedEvent = data.ToResourceUsageReportedEvent(WorkerID.Value);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ResourceUsageReported,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), statusReportedEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DominoInvocation(DominoInvocationEventData data)
        {
            var bxlInvEvent = data.ToBXLInvocationEvent(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.BxlInvocation,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), bxlInvEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            var pipExecDirectoryOutputEvent = data.ToPipExecutionDirectoryOutputsEvent(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipExecutionDirectoryOutputs,
                UUID = Guid.NewGuid().ToString()
            };

            WriteToDb(eq.ToByteArray(), pipExecDirectoryOutputEvent.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Ingest all of the pips to RocksDB
        /// </summary>
        public void IngestAllPips()
        {
            var totalNumberOfPips = CachedGraph.PipTable.StableKeys.Count;
            var pipIds = CachedGraph.PipTable.StableKeys;
            var concurrency = Environment.ProcessorCount;
            var partitionSize = totalNumberOfPips / concurrency;
            Console.WriteLine("Ingesting pips now ...");

            Parallel.For(0, concurrency, i =>
            {
                var start = i * partitionSize;
                var end = (i + 1) == concurrency ? totalNumberOfPips : (i + 1) * partitionSize;
                var pipsIngested = 0;
                var pipSemistableKeys = new List<byte[]>();
                var pipSemistableValues = new List<byte[]>();
                var pipPipIdKeys = new List<byte[]>();
                var pipValues = new List<byte[]>();

                for (int j = start; j < end; j++)
                {
                    var pipId = pipIds[j];
                    pipsIngested++;

                    if (pipsIngested % 100000 == 0)
                    {
                        Console.Write(".");

                        Analysis.IgnoreResult(
                            m_accessor.Use(database =>
                            {
                                database.ApplyBatch(pipSemistableKeys, pipSemistableValues, XldbDataStore.PipColumnFamilyName);
                                database.ApplyBatch(pipPipIdKeys, pipValues, XldbDataStore.PipColumnFamilyName);
                            })
                        );

                        pipSemistableKeys.Clear();
                        pipSemistableValues.Clear();
                        pipPipIdKeys.Clear();
                        pipValues.Clear();
                    }

                    var hydratedPip = CachedGraph.PipTable.HydratePip(pipId, Pips.PipQueryContext.PipGraphRetrieveAllPips);
                    var pipType = hydratedPip.PipType;

                    if (pipType == PipType.Value || pipType == PipType.HashSourceFile || pipType == PipType.SpecFile || pipType == PipType.Module)
                    {
                        continue;
                    }

                    var xldbPip = hydratedPip.ToPip(CachedGraph);
                    IMessage xldbSpecificPip = xldbPip;

                    if (pipType == PipType.Ipc)
                    {
                        var ipcPip = (Pips.Operations.IpcPip)hydratedPip;
                        xldbSpecificPip = ipcPip.ToIpcPip(PathTable, xldbPip);
                    }
                    else if (pipType == PipType.SealDirectory)
                    {
                        var sealDirectoryPip = (Pips.Operations.SealDirectory)hydratedPip;
                        xldbSpecificPip = sealDirectoryPip.ToSealDirectory(PathTable, xldbPip);
                    }
                    else if (pipType == PipType.CopyFile)
                    {
                        var copyFilePip = (Pips.Operations.CopyFile)hydratedPip;
                        xldbSpecificPip = copyFilePip.ToCopyFile(PathTable, xldbPip);
                    }
                    else if (pipType == PipType.WriteFile)
                    {
                        var writeFilePip = (Pips.Operations.WriteFile)hydratedPip;
                        xldbSpecificPip = writeFilePip.ToWriteFile(PathTable, xldbPip);
                    }
                    else if (pipType == PipType.Process)
                    {
                        var processPip = (Pips.Operations.Process)hydratedPip;
                        xldbSpecificPip = processPip.ToProcessPip(PathTable, xldbPip);
                    }

                    var pipSemistableQuery = new PipQuerySemiStableHash()
                    {
                        SemiStableHash = hydratedPip.SemiStableHash,
                    };

                    var pipSemistableValue = new PipValueSemiStableHash()
                    {
                        PipId = hydratedPip.PipId.Value,
                    };

                    var pipIdQuery = new PipQueryPipId()
                    {
                        PipId = hydratedPip.PipId.Value,
                        PipType = (Xldb.PipType)pipType
                    };

                    // If the SemiStableHash != 0, then we want to create the SemistableHash -> PipId indirection.
                    // Else we do not want that since the key would no longer be unique
                    if (hydratedPip.SemiStableHash != 0)
                    {
                        pipSemistableKeys.Add(pipSemistableQuery.ToByteArray());
                        pipSemistableValues.Add(pipSemistableValue.ToByteArray());
                    }

                    pipPipIdKeys.Add(pipIdQuery.ToByteArray());
                    pipValues.Add(xldbSpecificPip.ToByteArray());
                }

                // Write the rest of the batched pips to the db
                Analysis.IgnoreResult(
                    m_accessor.Use(database =>
                    {
                        database.ApplyBatch(pipSemistableKeys, pipSemistableValues, XldbDataStore.PipColumnFamilyName);
                        database.ApplyBatch(pipPipIdKeys, pipValues, XldbDataStore.PipColumnFamilyName);
                    })
                );
            });
        }

        /// <summary>
        /// Write a key/value pair to the db
        /// </summary>
        public void WriteToDb(byte[] key, byte[] value, string columnFamilyName = null)
        {
            Analysis.IgnoreResult(
                m_accessor.Use(database =>
                {
                    database.Put(key, value, columnFamilyName);
                })
            );
        }
    }
}
