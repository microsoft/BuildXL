// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Xldb;
using BuildXL.Xldb.Proto;
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

    /// <summary>
    /// Analyzer that dumps xlg and graph data into RocksDB
    /// </summary>
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
            m_actionBlock = new ActionBlockSlim<Action>(Environment.ProcessorCount, action => action());
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
    /// Inner wrapper analyzer that works with the action blocks spawned from the outer main analyzer
    /// </summary>
    internal sealed class XLGToDBAnalyzerInner : Analyzer
    {
        private const int s_eventOutputBatchLogSize = 100000;
        private const int s_pipOutputBatchLogSize = 10000;

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
        private int m_eventSequenceNumber;
        private Dictionary<Scheduler.Tracing.ExecutionEventId, EventCountByTypeValue> m_eventCountByType = new Dictionary<Scheduler.Tracing.ExecutionEventId, EventCountByTypeValue>();
        private string[] m_additionalColumns = { XldbDataStore.EventColumnFamilyName, XldbDataStore.PipColumnFamilyName, XldbDataStore.StaticGraphColumnFamilyName };

        private ConcurrentBigMap<Utilities.FileArtifact, HashSet<uint>> m_fileConsumerMap = new ConcurrentBigMap<Utilities.FileArtifact, HashSet<uint>>();
        private ConcurrentBigMap<Utilities.FileArtifact, uint> m_dynamicFileProducerMap = new ConcurrentBigMap<Utilities.FileArtifact, uint>();
        private ConcurrentBigMap<Utilities.DirectoryArtifact, HashSet<uint>> m_directoryConsumerMap = new ConcurrentBigMap<Utilities.DirectoryArtifact, HashSet<uint>>();

        public XLGToDBAnalyzerInner(AnalysisInput input) : base(input)
        {
            m_stopWatch = new Stopwatch();
            m_stopWatch.Start();
        }

        internal void ProcessEvent<TEventData>(TEventData data, uint workerId) where TEventData : struct, IExecutionLogEventData<TEventData>
        {
            var eventCount = Interlocked.Increment(ref m_eventCount);

            if (eventCount % s_eventOutputBatchLogSize == 0)
            {
                Console.WriteLine($"Processed {eventCount} events so far. {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds have elapsed.");
            }

            WorkerID.Value = workerId;
            data.Metadata.LogToTarget(data, this);
        }

        /// <inheritdoc/>
        public override void Prepare()
        {
            var accessor = KeyValueStoreAccessor.Open(storeDirectory: OutputDirPath, additionalColumns: m_additionalColumns, openBulkLoad: false);

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
            m_eventSequenceNumber = 1;
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
            var maybeInserted = m_accessor.Use(database =>
                {
                    foreach (var kvp in m_eventCountByType)
                    {
                        var eventCountByTypeQuery = new EventCountByTypeKey
                        {
                            EventTypeID = (Xldb.Proto.ExecutionEventId)(kvp.Key + 1)
                        };

                        database.Put(eventCountByTypeQuery.ToByteArray(), kvp.Value.ToByteArray());
                    }
                });

            if (!maybeInserted.Succeeded)
            {
                Console.WriteLine("Failed to insert event metadata into RocksDb. Exiting analyzer now ...");
                maybeInserted.Failure.Throw();
            }

            WriteToDb(Encoding.ASCII.GetBytes(XldbDataStore.EventCountKey), ec.ToByteArray());

            Console.WriteLine("\nEvent data ingested into RocksDB. Starting to ingest static graph data ...\n");

            IngestAllPips();
            Console.WriteLine($"\nAll pips ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            Console.WriteLine("\nStarting to ingest PipGraph metadata");
            var xldbPipGraph = CachedGraph.PipGraph.ToPipGraph(PathTable, CachedGraph.PipTable);

            var cachedGraphKey = new CachedGraphKey
            {
                PipGraph = true
            };

            WriteToDb(cachedGraphKey.ToByteArray(), xldbPipGraph.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            Console.WriteLine($"\nPipGraph metadata ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            Console.WriteLine("\nStarting to ingest file and directory consumer/producer information.");

            IngestProducerConsumerInformation();
            Console.WriteLine($"\nConsumer/producer information ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            return 0;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            Console.Write("Done writing all data ... compacting DB now.");
            m_accessor.Dispose();
            Console.WriteLine($"\nDone compacting and exiting analyzer ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");
        }

        /// <inheritdoc/>
        public override bool CanHandleEvent(Scheduler.Tracing.ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            if (m_eventCountByType.TryGetValue(eventId, out var eventCountVal))
            {
                eventCountVal.WorkerToCountMap.TryGetValue(workerId, out var count);
                count++;

                eventCountVal.WorkerToCountMap[workerId] = count;
            }
            else
            {
                eventCountVal = new EventCountByTypeValue();
                eventCountVal.WorkerToCountMap.Add(workerId, 1);
                m_eventCountByType.Add(eventId, eventCountVal);
            }

            // Excluding Observed Inputs because we capture the same information instead in ProcessFingerprintComputation Event
            return (m_accessorSucceeded && eventId != Scheduler.Tracing.ExecutionEventId.ObservedInputs);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var value = data.ToFileArtifactContentDecidedEvent(WorkerID.Value, PathTable);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.FileArtifactContentDecided,
                FileArtifactContentDecidedKey = AbsolutePathToXldbString(data.FileArtifact.Path),
                FileRewriteCount = data.FileArtifact.RewriteCount
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void WorkerList(WorkerListEventData data)
        {
            var value = data.ToWorkerListEvent(WorkerID.Value);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.WorkerList,
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            var value = data.ToPipExecutionPerformanceEvent();
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.PipExecutionPerformance,
                PipId = data.PipId.Value
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            var value = data.ToDirectoryMembershipHashedEvent(WorkerID.Value, PathTable);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.DirectoryMembershipHashed,
                PipId = data.PipId.Value,
                DirectoryMembershipHashedKey = AbsolutePathToXldbString(data.Directory),
                EventSequenceNumber = Interlocked.Increment(ref m_eventSequenceNumber)
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            var value = data.ToProcessExecutionMonitoringReportedEvent(WorkerID.Value, PathTable);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.ProcessExecutionMonitoringReported,
                PipId = data.PipId.Value
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var value = data.ToProcessFingerprintComputationEvent(WorkerID.Value, PathTable);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.ProcessFingerprintComputation,
                PipId = data.PipId.Value,
                ProcessFingerprintComputationKey = value.Kind,
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ExtraEventDataReported(ExtraEventData data)
        {
            var value = data.ToExtraEventDataReported(WorkerID.Value);
            // There will be exactly one event of this type that is reported, so nothing special needs to be added to the key
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.ExtraEventDataReported,
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            var value = data.ToDependencyViolationReportedEvent(WorkerID.Value, PathTable);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.DependencyViolationReported,
                ViolatorPipID = data.ViolatorPipId.Value
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            var value = data.ToPipExecutionStepPerformanceReportedEvent(WorkerID.Value);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.PipExecutionStepPerformanceReported,
                PipId = data.PipId.Value,
                PipExecutionStepPerformanceKey = value.Step,
                EventSequenceNumber = Interlocked.Increment(ref m_eventSequenceNumber)
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipCacheMiss(PipCacheMissEventData data)
        {
            var value = data.ToPipCacheMissEvent(WorkerID.Value);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.PipCacheMiss,
                PipId = data.PipId.Value
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void StatusReported(StatusEventData data)
        {
            var value = data.ToResourceUsageReportedEvent(WorkerID.Value);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.ResourceUsageReported,
                EventSequenceNumber = Interlocked.Increment(ref m_eventSequenceNumber)
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DominoInvocation(DominoInvocationEventData data)
        {
            var value = data.ToBXLInvocationEvent(WorkerID.Value, PathTable);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.BxlInvocation,
            };

            WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var (directoryArtifact, fileArtifactArray) in data.DirectoryOutputs)
            {
                var value = new PipExecutionDirectoryOutputsEvent
                {
                    WorkerID = WorkerID.Value,
                    PipID = data.PipId.Value,
                    DirectoryArtifact = directoryArtifact.ToDirectoryArtifact(PathTable),
                };

                value.FileArtifactArray.AddRange(fileArtifactArray.Select(
                        file => file.ToFileArtifact(PathTable)));

                var key = new EventKey
                {
                    EventTypeID = Xldb.Proto.ExecutionEventId.PipExecutionDirectoryOutputs,
                    PipId = data.PipId.Value,
                    PipExecutionDirectoryOutputKey = AbsolutePathToXldbString(directoryArtifact.Path)
                };

                foreach (var file in fileArtifactArray)
                {
                    m_dynamicFileProducerMap.Add(file, data.PipId.Value);
                }

                WriteToDb(key.ToByteArray(), value.ToByteArray(), XldbDataStore.EventColumnFamilyName);
            }
        }

        /// <summary>
        /// Ingest all of the pips to RocksDB
        /// </summary>
        private void IngestAllPips()
        {
            var totalNumberOfPips = CachedGraph.PipTable.StableKeys.Count;
            var pipIds = CachedGraph.PipTable.StableKeys;
            var concurrency = Environment.ProcessorCount / 2;
            var partitionSize = totalNumberOfPips / concurrency;
            Console.WriteLine("Ingesting pips now ...");

            Parallel.For(0, concurrency, i =>
            {
                // Hold only one lock while inserting all of these keys into the DB
                var maybeInserted = m_accessor.Use(database =>
                {
                    var start = i * partitionSize;
                    var end = (i + 1) == concurrency ? totalNumberOfPips : (i + 1) * partitionSize;
                    var pipsIngested = 0;
                    var pipSemistableMap = new Dictionary<byte[], byte[]>();
                    var pipIdMap = new Dictionary<byte[], byte[]>();

                    for (int j = start; j < end; j++)
                    {
                        var pipId = pipIds[j];
                        pipsIngested++;

                        if (pipsIngested % s_pipOutputBatchLogSize == 0)
                        {
                            Console.Write(".");
                            database.ApplyBatch(pipSemistableMap, XldbDataStore.PipColumnFamilyName);
                            database.ApplyBatch(pipIdMap, XldbDataStore.PipColumnFamilyName);

                            pipSemistableMap.Clear();
                            pipIdMap.Clear();
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

                            foreach (var fileArtifact in ipcPip.FileDependencies)
                            {
                                AddToFileConsumerMap(fileArtifact, pipId);
                            }

                            foreach (var directoryArtifact in ipcPip.DirectoryDependencies)
                            {
                                AddToDirectoryConsumerMap(directoryArtifact, pipId);
                            }
                        }
                        else if (pipType == PipType.SealDirectory)
                        {
                            var sealDirectoryPip = (Pips.Operations.SealDirectory)hydratedPip;
                            xldbSpecificPip = sealDirectoryPip.ToSealDirectory(PathTable, xldbPip);

                            // If it is a shared opaque, then flatten the list of composted directories
                            if (sealDirectoryPip.Kind == Pips.Operations.SealDirectoryKind.SharedOpaque)
                            {
                                var directoryQueue = new Queue<Utilities.DirectoryArtifact>();

                                foreach (var initialDirectory in sealDirectoryPip.ComposedDirectories)
                                {
                                    directoryQueue.Enqueue(initialDirectory);
                                }

                                while (directoryQueue.Count > 0)
                                {
                                    var nestedDirectory = directoryQueue.Dequeue();
                                    var nestedPipId = CachedGraph.PipGraph.GetSealedDirectoryNode(nestedDirectory).ToPipId();

                                    if (CachedGraph.PipTable.IsSealDirectoryComposite(nestedPipId))
                                    {
                                        var nestedPip = (Pips.Operations.SealDirectory)CachedGraph.PipGraph.GetSealedDirectoryPip(nestedDirectory, Pips.PipQueryContext.SchedulerExecuteSealDirectoryPip);
                                        foreach (var pendingDirectory in nestedPip.ComposedDirectories)
                                        {
                                            directoryQueue.Enqueue(pendingDirectory);
                                        }
                                    }
                                    else
                                    {
                                        Contract.Assert(nestedDirectory.IsOutputDirectory());
                                        AddToDirectoryConsumerMap(nestedDirectory, pipId);
                                    }
                                }
                            }

                            foreach (var fileArtifact in sealDirectoryPip.Contents)
                            {
                                AddToFileConsumerMap(fileArtifact, pipId);
                            }
                        }
                        else if (pipType == PipType.CopyFile)
                        {
                            var copyFilePip = (Pips.Operations.CopyFile)hydratedPip;
                            xldbSpecificPip = copyFilePip.ToCopyFile(PathTable, xldbPip);
                            AddToFileConsumerMap(copyFilePip.Source, pipId);
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

                            AddToFileConsumerMap(processPip.StandardInputFile, pipId);
                            AddToFileConsumerMap(processPip.Executable, pipId);

                            foreach (var fileArtifact in processPip.Dependencies)
                            {
                                AddToFileConsumerMap(fileArtifact, pipId);
                            }

                            foreach (var directoryArtifact in processPip.DirectoryDependencies)
                            {
                                AddToDirectoryConsumerMap(directoryArtifact, pipId);
                            }
                        }
                        else
                        {
                            Contract.Assert(false, "Unknown pip type parsed");
                        }

                        // If the SemiStableHash != 0, then we want to create the SemistableHash -> PipId indirection.
                        // Else we do not want that since the key would no longer be unique
                        if (hydratedPip.SemiStableHash != 0)
                        {
                            var pipSemistableHashKey = new PipSemistableHashKey()
                            {
                                SemiStableHash = hydratedPip.SemiStableHash,
                            };

                            var pipIdValue = new PipIdValue()
                            {
                                PipId = hydratedPip.PipId.Value,
                            };

                            pipSemistableMap.Add(pipSemistableHashKey.ToByteArray(), pipIdValue.ToByteArray());
                        }

                        var pipIdKey = new PipIdKey()
                        {
                            PipId = hydratedPip.PipId.Value,
                            PipType = (Xldb.Proto.PipType)(pipType + 1)
                        };

                        pipIdMap.Add(pipIdKey.ToByteArray(), xldbSpecificPip.ToByteArray());
                    }

                    database.ApplyBatch(pipSemistableMap, XldbDataStore.PipColumnFamilyName);
                    database.ApplyBatch(pipIdMap, XldbDataStore.PipColumnFamilyName);
                });

                if (!maybeInserted.Succeeded)
                {
                    Console.WriteLine("Failed to insert pip data into RocksDb. Exiting analyzer now ...");
                    maybeInserted.Failure.Throw();
                }
            });
        }

        /// <summary>
        /// Ingest file and directory producer and consumer information to RocksDB
        /// </summary>
        private void IngestProducerConsumerInformation()
        {
            var parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount;

            Parallel.ForEach(m_fileConsumerMap, parallelOptions, kvp =>
            {
                var fileConsumerKey = new FileProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Consumer,
                    FileArtifact = AbsolutePathToXldbString(kvp.Key.Path),
                    RewriteCount = kvp.Key.RewriteCount
                };

                var fileConsumerValue = new FileConsumerValue();
                fileConsumerValue.PipIds.AddRange(kvp.Value);

                WriteToDb(fileConsumerKey.ToByteArray(), fileConsumerValue.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            });

            Parallel.ForEach(m_directoryConsumerMap, parallelOptions, kvp =>
            {
                var directoryConsumerKey = new DirectoryProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Consumer,
                    DirectoryArtifact = AbsolutePathToXldbString(kvp.Key.Path)
                };

                var directoryConsumerValue = new DirectoryConsumerValue();
                directoryConsumerValue.PipIds.AddRange(kvp.Value);

                WriteToDb(directoryConsumerKey.ToByteArray(), directoryConsumerValue.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            });

            Parallel.ForEach(CachedGraph.PipGraph.AllFilesAndProducers, parallelOptions, kvp =>
            {
                var fileProducerKey = new FileProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Producer,
                    FileArtifact = AbsolutePathToXldbString(kvp.Key.Path),
                    RewriteCount = kvp.Key.RewriteCount
                };

                var fileProducerValue = new FileProducerValue()
                {
                    PipId = kvp.Value.Value
                };

                WriteToDb(fileProducerKey.ToByteArray(), fileProducerValue.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            });

            Parallel.ForEach(CachedGraph.PipGraph.AllOutputDirectoriesAndProducers, parallelOptions, kvp =>
            {
                var directoryProducerKey = new DirectoryProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Producer,
                    DirectoryArtifact = AbsolutePathToXldbString(kvp.Key.Path)
                };

                var directoryProducerValue = new DirectoryProducerValue()
                {
                    PipId = kvp.Value.Value
                };

                WriteToDb(directoryProducerKey.ToByteArray(), directoryProducerValue.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            });

            Parallel.ForEach(m_dynamicFileProducerMap, parallelOptions, kvp =>
            {
                var fileProducerKey = new FileProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Producer,
                    FileArtifact = AbsolutePathToXldbString(kvp.Key.Path),
                    RewriteCount = kvp.Key.RewriteCount
                };

                var fileProducerValue = new FileProducerValue()
                {
                    PipId = kvp.Value
                };

                WriteToDb(fileProducerKey.ToByteArray(), fileProducerValue.ToByteArray(), XldbDataStore.StaticGraphColumnFamilyName);
            });
        }

        /// <summary>
        /// Add file artifacts to the file consumer map
        /// </summary>
        private void AddToFileConsumerMap(Utilities.FileArtifact fileArtifact, PipId pipId)
        {
            var consumers = m_fileConsumerMap.GetOrAdd(fileArtifact, new HashSet<uint>()).Item.Value;
            lock (consumers)
            {
                consumers.Add(pipId.Value);
            }
        }

        /// <summary>
        /// Add directory artifacts to the directory consumer map 
        /// </summary>
        private void AddToDirectoryConsumerMap(Utilities.DirectoryArtifact directoryArtifact, PipId pipId)
        {
            var consumers = m_directoryConsumerMap.GetOrAdd(directoryArtifact, new HashSet<uint>()).Item.Value;
            lock (consumers)
            {
                consumers.Add(pipId.Value);
            }
        }

        /// <summary>
        /// Write a key/value pair to the db
        /// </summary>
        public void WriteToDb(byte[] key, byte[] value, string columnFamilyName = null)
        {
            var maybeInserted = m_accessor.Use(database =>
            {
                database.Put(key, value, columnFamilyName);
            });

            if (!maybeInserted.Succeeded)
            {
                Console.WriteLine("Failed to insert event data into RocksDb. Exiting analyzer now ...");
                maybeInserted.Failure.Throw();
            }
        }

        /// <summary>
        /// Convert an absolute path to a string specifically and only with windows path format (to keep it consistent amongst all databases)
        /// </summary>
        private string AbsolutePathToXldbString(Utilities.AbsolutePath path)
        {
            return path.ToString(PathTable, PathFormat.Windows);
        }
    }
}
