// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
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
using static BuildXL.Utilities.HierarchicalNameTable;
using PipType = BuildXL.Pips.Operations.PipType;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeXLGToDBAnalyzer()
        {
            string outputDirPath = null;
            bool removeDirPath = false;
            bool includeProcessFingerprintComputationEvent = false;

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
                else if (opt.Name.Equals("includeProcessFingerprintComputationEvent", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("ipfce", StringComparison.OrdinalIgnoreCase))
                {
                    includeProcessFingerprintComputationEvent = true;
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

            if (includeProcessFingerprintComputationEvent)
            {
                Console.WriteLine("Including ProcessFingerprintComputatingEvent data in the DB");
            }

            return new XLGToDBAnalyzer(GetAnalysisInput())
            {
                OutputDirPath = outputDirPath,
                IncludeProcessFingerprintComputationEvent = includeProcessFingerprintComputationEvent
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
            writer.WriteOption("removeDir", "Optional. Include flag if you wish to delete the 'output' directory if it already exists. If unset, directory will not be deleted", shortName: "r");
            writer.WriteOption("includeProcessFingerprintComputationEvent", "Optional. Include flag if you wish to include ProcessFingerprintComputationEvent data in the DB. Defaults to false if left unset", shortName: "ipfce");
        }
    }

    /// <summary>
    /// Analyzer that dumps xlg and graph data into RocksDB
    /// </summary>
    internal sealed class XLGToDBAnalyzer : Analyzer
    {
        private const double s_concurrencyMultiplier = 0.75;

        private XLGToDBAnalyzerInner m_inner;
        private ActionBlockSlim<Action> m_actionBlock;

        public string OutputDirPath
        {
            set => m_inner.OutputDirPath = value;
        }

        public bool IncludeProcessFingerprintComputationEvent
        {
            set => m_inner.IncludeProcessFingerprintComputationEvent = value;
        }

        public XLGToDBAnalyzer(AnalysisInput input)
            : base(input)
        {
            m_inner = new XLGToDBAnalyzerInner(input);
            m_actionBlock = new ActionBlockSlim<Action>((int)(Environment.ProcessorCount * s_concurrencyMultiplier), action => action());
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
        private const double s_innerConcurrencyMultiplier = 1.25;

        /// <summary>
        /// A larger size for the name expander for paths than the defauly size of 7013. This value was selected by 
        /// testing various larger prime numbers from HashCodeHelper.cs until one was found that resulted in good speedup without
        /// using an excessive amount of RAM.
        /// </summary>
        private const int s_nameExpanderSize = 108361;

        /// <summary>
        /// Output directory path
        /// </summary>
        public string OutputDirPath;

        /// <summary>
        /// Include the ProcessFingerprintComputationEvent data in the DB
        /// </summary>
        public bool IncludeProcessFingerprintComputationEvent;

        /// <summary>
        /// Store WorkerID.Value to pass into protobuf object to identify this event
        /// </summary>
        public ThreadLocal<uint> WorkerID = new ThreadLocal<uint>();

        private bool m_accessorSucceeded;
        private KeyValueStoreAccessor m_accessor;
        private Stopwatch m_stopWatch;
        private int m_eventSequenceNumber;
        private int m_eventCount;
        private ConcurrentDictionary<DBStoredTypes, DBStorageStatsValue> m_dBStorageStats = new ConcurrentDictionary<DBStoredTypes, DBStorageStatsValue>();
        private string[] m_additionalColumns = { XldbDataStore.EventColumnFamilyName, XldbDataStore.PipColumnFamilyName, XldbDataStore.StaticGraphColumnFamilyName };

        private ConcurrentBigMap<Utilities.FileArtifact, HashSet<uint>> m_fileConsumerMap = new ConcurrentBigMap<Utilities.FileArtifact, HashSet<uint>>();
        private ConcurrentBigMap<Utilities.FileArtifact, uint> m_dynamicFileProducerMap = new ConcurrentBigMap<Utilities.FileArtifact, uint>();
        private ConcurrentBigMap<Utilities.DirectoryArtifact, HashSet<uint>> m_directoryConsumerMap = new ConcurrentBigMap<Utilities.DirectoryArtifact, HashSet<uint>>();

        private readonly NameExpander m_nameExpander = new NameExpander(s_nameExpanderSize);

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

            m_eventSequenceNumber = 1;
            m_eventCount = 0;
        }

        /// <inheritdoc/>
        public override int Analyze()
        {
            Console.WriteLine($"Total number of events ingested = {m_eventCount}");
            Console.WriteLine($"Total time for event ingestion: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            Console.WriteLine("\nEvent data ingested into RocksDB. Starting to ingest static graph data ...\n");

            IngestAllPips();
            Console.WriteLine($"\nAll pips ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            Console.WriteLine("\nStarting to ingest PipGraph metadata");
            var xldbPipGraph = CachedGraph.PipGraph.ToPipGraph(PathTable, CachedGraph.PipTable, m_nameExpander);

            var cachedGraphKey = new GraphMetadataKey
            {
                Type = GraphMetaData.PipGraph
            };
            var keyArr = cachedGraphKey.ToByteArray();
            var valueArr = xldbPipGraph.ToByteArray();

            WriteToDb(keyArr, valueArr, XldbDataStore.StaticGraphColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.GraphMetaData, keyArr.Length + valueArr.Length);

            var xldbMounts = CachedGraph.MountPathExpander.ToMountPathExpander(PathTable, m_nameExpander);
            cachedGraphKey.Type = GraphMetaData.MountPathExpander;
            keyArr = cachedGraphKey.ToByteArray();
            valueArr = xldbMounts.ToByteArray();

            WriteToDb(keyArr, valueArr, XldbDataStore.StaticGraphColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.MountPathExpander, keyArr.Length + valueArr.Length);

            Console.WriteLine($"\nPipGraph metadata ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            Console.WriteLine("\nStarting to ingest file and directory consumer/producer information.");

            IngestProducerConsumerInformation();
            Console.WriteLine($"\nConsumer/producer information ingested ... total time is: {m_stopWatch.ElapsedMilliseconds / 1000.0} seconds");

            foreach (var kvp in m_dBStorageStats)
            {
                var dBStorageStatsKey = new DBStorageStatsKey
                {
                    StorageType = kvp.Key
                };

                WriteToDb(dBStorageStatsKey.ToByteArray(), kvp.Value.ToByteArray());
            }

            // Write the Xldb version file to the Xldb directory
            File.WriteAllText(Path.Combine(OutputDirPath, XldbDataStore.XldbVersionFileName), XldbDataStore.XldbVersion.ToString());
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
            // Excluding Observed Inputs Event because we capture the same information instead in ProcessFingerprintComputation Event  
            // Only include ProcessFingerprintComputation if the IncludeProcessFingerprintComputationEvent flag is passed in, but let all non ProcessFingerprintComputation events get handled
            return (m_accessorSucceeded && 
                eventId != Scheduler.Tracing.ExecutionEventId.ObservedInputs && 
                (eventId != Scheduler.Tracing.ExecutionEventId.ProcessFingerprintComputation || IncludeProcessFingerprintComputationEvent));
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var value = data.ToFileArtifactContentDecidedEvent(WorkerID.Value, PathTable, m_nameExpander);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.FileArtifactContentDecided,
                FileArtifactContentDecidedKey = AbsolutePathToXldbString(data.FileArtifact.Path),
                FileRewriteCount = data.FileArtifact.RewriteCount
            };

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.FileArtifactContentDecided, keyArr.Length + valueArr.Length);
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

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.WorkerList, keyArr.Length + valueArr.Length);
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

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.PipExecutionPerformance, keyArr.Length + valueArr.Length);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            var value = data.ToDirectoryMembershipHashedEvent(WorkerID.Value, PathTable, m_nameExpander);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.DirectoryMembershipHashed,
                PipId = data.PipId.Value,
                DirectoryMembershipHashedKey = AbsolutePathToXldbString(data.Directory),
                EventSequenceNumber = Interlocked.Increment(ref m_eventSequenceNumber)
            };

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.DirectoryMembershipHashed, keyArr.Length + valueArr.Length);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            var value = data.ToProcessExecutionMonitoringReportedEvent(WorkerID.Value, PathTable, m_nameExpander);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.ProcessExecutionMonitoringReported,
                PipId = data.PipId.Value
            };

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.ProcessExecutionMonitoringReported, keyArr.Length + valueArr.Length);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var value = data.ToProcessFingerprintComputationEvent(WorkerID.Value, PathTable, m_nameExpander);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.ProcessFingerprintComputation,
                PipId = data.PipId.Value,
                ProcessFingerprintComputationKey = value.Kind,
            };

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.ProcessFingerprintComputation, keyArr.Length + valueArr.Length);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void BuildSessionConfiguration(BuildSessionConfigurationEventData data)
        {
            var value = data.ToExecutionLogSaltsData(WorkerID.Value);
            // There will be exactly one event of this type that is reported, so nothing special needs to be added to the key
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.BuildSessionConfiguration,
            };

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.ExtraEventDataReported, keyArr.Length + valueArr.Length);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            var value = data.ToDependencyViolationReportedEvent(WorkerID.Value, PathTable, m_nameExpander);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.DependencyViolationReported,
                ViolatorPipID = data.ViolatorPipId.Value
            };

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.DependencyViolationReported, keyArr.Length + valueArr.Length);
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

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.PipExecutionStepPerformanceReported, keyArr.Length + valueArr.Length);
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

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.PipCacheMiss, keyArr.Length + valueArr.Length);
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

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.ResourceUsageReported, keyArr.Length + valueArr.Length);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void BxlInvocation(BxlInvocationEventData data)
        {
            var value = data.ToBxlInvocationEvent(WorkerID.Value, PathTable, m_nameExpander);
            var key = new EventKey
            {
                EventTypeID = Xldb.Proto.ExecutionEventId.BxlInvocation,
            };

            var keyArr = key.ToByteArray();
            var valueArr = value.ToByteArray();
            WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
            AddToDbStorageDictionary(DBStoredTypes.BxlInvocation, keyArr.Length + valueArr.Length);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var (directoryArtifact, fileArtifactArray) in data.DirectoryOutputs)
            {
                foreach (var file in fileArtifactArray)
                {
                    m_dynamicFileProducerMap.Add(file, data.PipId.Value);
                }

                var value = new PipExecutionDirectoryOutputsEvent
                {
                    WorkerID = WorkerID.Value,
                    PipID = data.PipId.Value,
                    DirectoryArtifact = directoryArtifact.ToDirectoryArtifact(PathTable, m_nameExpander),
                };

                value.FileArtifactArray.AddRange(fileArtifactArray.Select(
                        file => file.ToFileArtifact(PathTable, m_nameExpander)));

                var key = new EventKey
                {
                    EventTypeID = Xldb.Proto.ExecutionEventId.PipExecutionDirectoryOutputs,
                    PipId = data.PipId.Value,
                    PipExecutionDirectoryOutputKey = AbsolutePathToXldbString(directoryArtifact.Path)
                };

                var keyArr = key.ToByteArray();
                var valueArr = value.ToByteArray();
                WriteToDb(keyArr, valueArr, XldbDataStore.EventColumnFamilyName);
                AddToDbStorageDictionary(DBStoredTypes.PipExecutionDirectoryOutputs, keyArr.Length + valueArr.Length);
            }
        }

        /// <summary>
        /// Ingest all of the pips to RocksDB
        /// </summary>
        private void IngestAllPips()
        {
            var totalNumberOfPips = CachedGraph.PipTable.StableKeys.Count;
            var pipIds = CachedGraph.PipTable.StableKeys;
            var concurrency = (int)(Environment.ProcessorCount * s_innerConcurrencyMultiplier);
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
                            database.ApplyBatch(pipSemistableMap);
                            database.ApplyBatch(pipIdMap, XldbDataStore.PipColumnFamilyName);

                            pipSemistableMap.Clear();
                            pipIdMap.Clear();
                        }

                        var hydratedPip = CachedGraph.PipTable.HydratePip(pipId, BuildXL.Pips.PipQueryContext.PipGraphRetrieveAllPips);
                        var pipType = hydratedPip.PipType;

                        if (pipType == PipType.Value || pipType == PipType.HashSourceFile || pipType == PipType.SpecFile || pipType == PipType.Module)
                        {
                            continue;
                        }

                        var xldbPip = hydratedPip.ToPip(CachedGraph);
                        var pipIdKey = new PipIdKey()
                        {
                            PipId = hydratedPip.PipId.Value,
                            PipType = (Xldb.Proto.PipType)(pipType + 1)
                        };
                        var pipIdKeyArr = pipIdKey.ToByteArray();
                        IMessage xldbSpecificPip = xldbPip;

                        if (pipType == PipType.Ipc)
                        {
                            var ipcPip = (Pips.Operations.IpcPip)hydratedPip;
                            xldbSpecificPip = ipcPip.ToIpcPip(PathTable, xldbPip, m_nameExpander);

                            foreach (var fileArtifact in ipcPip.FileDependencies)
                            {
                                AddToFileConsumerMap(fileArtifact, pipId);
                            }

                            foreach (var directoryArtifact in ipcPip.DirectoryDependencies)
                            {
                                AddToDirectoryConsumerMap(directoryArtifact, pipId);
                            }

                            AddToDbStorageDictionary(DBStoredTypes.IpcPip, pipIdKeyArr.Length + xldbSpecificPip.ToByteArray().Length);
                        }
                        else if (pipType == PipType.SealDirectory)
                        {
                            var sealDirectoryPip = (Pips.Operations.SealDirectory)hydratedPip;
                            xldbSpecificPip = sealDirectoryPip.ToSealDirectory(PathTable, xldbPip, m_nameExpander);

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
                                        var nestedPip = (Pips.Operations.SealDirectory)CachedGraph.PipGraph.GetSealedDirectoryPip(nestedDirectory, BuildXL.Pips.PipQueryContext.SchedulerExecuteSealDirectoryPip);
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

                            AddToDbStorageDictionary(DBStoredTypes.SealDirectoryPip, pipIdKeyArr.Length + xldbSpecificPip.ToByteArray().Length);
                        }
                        else if (pipType == PipType.CopyFile)
                        {
                            var copyFilePip = (Pips.Operations.CopyFile)hydratedPip;
                            xldbSpecificPip = copyFilePip.ToCopyFile(PathTable, xldbPip, m_nameExpander);
                            AddToFileConsumerMap(copyFilePip.Source, pipId);
                            AddToDbStorageDictionary(DBStoredTypes.CopyFilePip, pipIdKeyArr.Length + xldbSpecificPip.ToByteArray().Length);
                        }
                        else if (pipType == PipType.WriteFile)
                        {
                            var writeFilePip = (Pips.Operations.WriteFile)hydratedPip;
                            xldbSpecificPip = writeFilePip.ToWriteFile(PathTable, xldbPip, m_nameExpander);
                            AddToDbStorageDictionary(DBStoredTypes.WriteFilePip, pipIdKeyArr.Length + xldbSpecificPip.ToByteArray().Length);
                        }
                        else if (pipType == PipType.Process)
                        {
                            var processPip = (Pips.Operations.Process)hydratedPip;
                            xldbSpecificPip = processPip.ToProcessPip(PathTable, xldbPip, m_nameExpander);

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

                            AddToDbStorageDictionary(DBStoredTypes.ProcessPip, pipIdKeyArr.Length + xldbSpecificPip.ToByteArray().Length);
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

                            pipSemistableMap.Add(pipSemistableHashKey.ToByteArray(), pipIdKeyArr);
                        }

                        pipIdMap.Add(pipIdKeyArr, xldbSpecificPip.ToByteArray());
                    }

                    database.ApplyBatch(pipSemistableMap);
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
            parallelOptions.MaxDegreeOfParallelism = (int)(Environment.ProcessorCount * s_innerConcurrencyMultiplier);

            Parallel.ForEach(m_fileConsumerMap, parallelOptions, kvp =>
            {
                var fileConsumerKey = new FileProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Consumer,
                    FilePath = AbsolutePathToXldbString(kvp.Key.Path),
                    RewriteCount = kvp.Key.RewriteCount
                };

                var fileConsumerValue = new FileConsumerValue();
                fileConsumerValue.PipIds.AddRange(kvp.Value);

                var key = fileConsumerKey.ToByteArray();
                var value = fileConsumerValue.ToByteArray();
                WriteToDb(key, value, XldbDataStore.StaticGraphColumnFamilyName);

                AddToDbStorageDictionary(DBStoredTypes.FileConsumer, key.Length + value.Length);
            });

            Parallel.ForEach(m_directoryConsumerMap, parallelOptions, kvp =>
            {
                var directoryConsumerKey = new DirectoryProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Consumer,
                    DirectoryPath = AbsolutePathToXldbString(kvp.Key.Path)
                };

                var directoryConsumerValue = new DirectoryConsumerValue();
                directoryConsumerValue.PipIds.AddRange(kvp.Value);

                var key = directoryConsumerKey.ToByteArray();
                var value = directoryConsumerValue.ToByteArray();
                WriteToDb(key, value, XldbDataStore.StaticGraphColumnFamilyName);

                AddToDbStorageDictionary(DBStoredTypes.DirectoryConsumer, key.Length + value.Length);
            });

            Parallel.ForEach(CachedGraph.PipGraph.AllFilesAndProducers, parallelOptions, kvp =>
            {
                var fileProducerKey = new FileProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Producer,
                    FilePath = AbsolutePathToXldbString(kvp.Key.Path),
                    RewriteCount = kvp.Key.RewriteCount
                };

                var fileProducerValue = new FileProducerValue()
                {
                    PipId = kvp.Value.Value
                };

                var key = fileProducerKey.ToByteArray();
                var value = fileProducerValue.ToByteArray();
                WriteToDb(key, value, XldbDataStore.StaticGraphColumnFamilyName);

                AddToDbStorageDictionary(DBStoredTypes.FileProducer, key.Length + value.Length);
            });

            Parallel.ForEach(CachedGraph.PipGraph.AllOutputDirectoriesAndProducers, parallelOptions, kvp =>
            {
                var directoryProducerKey = new DirectoryProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Producer,
                    DirectoryPath = AbsolutePathToXldbString(kvp.Key.Path)
                };

                var directoryProducerValue = new DirectoryProducerValue()
                {
                    PipId = kvp.Value.Value
                };

                var key = directoryProducerKey.ToByteArray();
                var value = directoryProducerValue.ToByteArray();
                WriteToDb(key, value, XldbDataStore.StaticGraphColumnFamilyName);

                AddToDbStorageDictionary(DBStoredTypes.DirectoryProducer, key.Length + value.Length);
            });

            Parallel.ForEach(m_dynamicFileProducerMap, parallelOptions, kvp =>
            {
                var fileProducerKey = new FileProducerConsumerKey()
                {
                    Type = ProducerConsumerType.Producer,
                    FilePath = AbsolutePathToXldbString(kvp.Key.Path),
                    RewriteCount = kvp.Key.RewriteCount
                };

                var fileProducerValue = new FileProducerValue()
                {
                    PipId = kvp.Value
                };

                var key = fileProducerKey.ToByteArray();
                var value = fileProducerValue.ToByteArray();
                WriteToDb(key, value, XldbDataStore.StaticGraphColumnFamilyName);

                AddToDbStorageDictionary(DBStoredTypes.FileProducer, key.Length + value.Length);
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
            return path.ToString(PathTable, PathFormat.Windows, m_nameExpander);
        }

        /// <summary>
        /// Adds storage event information to db storage dictionary
        /// </summary>
        private void AddToDbStorageDictionary(DBStoredTypes type, int payloadSize)
        {
            m_dBStorageStats.AddOrUpdate(type, new DBStorageStatsValue()
            {
                Count = 1,
                Size = (ulong)payloadSize,
            }, (key, dBStorageStatsValue) =>
            {
                dBStorageStatsValue.Count++;
                dBStorageStatsValue.Size += (ulong)payloadSize;

                return dBStorageStatsValue;
            });
        }
    }
}
