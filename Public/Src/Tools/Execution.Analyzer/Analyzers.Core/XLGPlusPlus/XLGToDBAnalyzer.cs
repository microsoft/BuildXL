// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;
using Google.Protobuf;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeXLGToDBAnalyzer()
        {
            string outputDirPath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDir", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirPath = ParseSingletonPathOption(opt, outputDirPath);
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
                throw Error("Directory provided exists and is non-empty. Aborting analyzer.");
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
            writer.WriteOption("outputDir", "Required. The new (or existing, but empty) directory to write out the RocksDB database", shortName: "o");
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
        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
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

        public XLGToDBAnalyzerInner(AnalysisInput input) : base(input)
        {
            m_stopWatch = new Stopwatch();
            m_stopWatch.Start();
        }

        internal void ProcessEvent<TEventData>(TEventData data, uint workerId) where TEventData : struct, IExecutionLogEventData<TEventData>
        {
            var eventCount = Interlocked.Increment(ref m_eventCount);

            if (eventCount % 10000 == 0)
            {
                Console.WriteLine("Processed {0} events so far.", eventCount);
                Console.WriteLine("Total time elapsed: {0} seconds", m_stopWatch.ElapsedMilliseconds / 1000.0);
            }

            WorkerID.Value = workerId;
            data.Metadata.LogToTarget(data, this);
        }

        /// <inheritdoc/>
        public override void Prepare()
        {
            var accessor = KeyValueStoreAccessor.Open(storeDirectory: OutputDirPath);

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
            Console.WriteLine("Num events ingested = {0}", m_eventCount);
            Console.WriteLine("Total time elapsed: {0} seconds", m_stopWatch.ElapsedMilliseconds / 1000.0);
            var ec = new Xldb.EventCount
            {
                Value = (uint)m_eventCount
            };

            WriteToDb(Encoding.ASCII.GetBytes(XldbDataStore.EventCountKey), ec.ToByteArray());
            return 0;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            m_accessor.Dispose();
        }

        /// <inheritdoc/>
        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            return (m_accessorSucceeded && eventId != ExecutionEventId.ObservedInputs);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var fileArtifactContentDecidedEvent = data.ToFileArtifactContentDecidedEvent(WorkerID.Value, PathTable);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.FileArtifactContentDecided,
                UUID = fileArtifactContentDecidedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), fileArtifactContentDecidedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void WorkerList(WorkerListEventData data)
        {
            var workerListEvent = data.ToWorkerListEvent(WorkerID.Value);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.WorkerList,
                UUID = workerListEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), workerListEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = data.ToPipExecutionPerformanceEvent();
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipExecutionPerformance,
                UUID = pipExecPerfEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipExecPerfEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            var directoryMembershipEvent = data.ToDirectoryMembershipHashedEvent(WorkerID.Value, PathTable);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.DirectoryMembershipHashed,
                UUID = directoryMembershipEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), directoryMembershipEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            var processExecMonitoringReportedEvent = data.ToProcessExecutionMonitoringReportedEvent(WorkerID.Value, PathTable);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ProcessExecutionMonitoringReported,
                UUID = processExecMonitoringReportedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), processExecMonitoringReportedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var processFingerprintComputedEvent = data.ToProcessFingerprintComputationEvent(WorkerID.Value, PathTable);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ProcessFingerprintComputation,
                UUID = processFingerprintComputedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), processFingerprintComputedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ExtraEventDataReported(ExtraEventData data)
        {
            var extraEvent = data.ToExtraEventDataReported(WorkerID.Value);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ExtraEventDataReported,
                UUID = extraEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), extraEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            var dependencyViolationEvent = data.ToDependencyViolationReportedEvent(WorkerID.Value, PathTable);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.DependencyViolationReported,
                UUID = dependencyViolationEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), dependencyViolationEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            var pipExecStepPerformanceEvent = data.ToPipExecutionStepPerformanceReportedEvent(WorkerID.Value);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipExecutionStepPerformanceReported,
                UUID = pipExecStepPerformanceEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipExecStepPerformanceEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipCacheMiss(PipCacheMissEventData data)
        {
            var pipCacheMissEvent = data.ToPipCacheMissEvent(WorkerID.Value);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipCacheMiss,
                UUID = pipCacheMissEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipCacheMissEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void StatusReported(StatusEventData data)
        {
            var statusReportedEvent = data.ToResourceUsageReportedEvent(WorkerID.Value);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.ResourceUsageReported,
                UUID = statusReportedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), statusReportedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DominoInvocation(DominoInvocationEventData data)
        {
            var bxlInvEvent = data.ToBXLInvocationEvent(WorkerID.Value, PathTable);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.BxlInvocation,
                UUID = bxlInvEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), bxlInvEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            var pipExecDirectoryOutputEvent = data.ToPipExecutionDirectoryOutputsEvent(WorkerID.Value, PathTable);
            var eq = new Xldb.EventTypeQuery
            {
                EventTypeID = Xldb.ExecutionEventId.PipExecutionDirectoryOutputs,
                UUID = pipExecDirectoryOutputEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipExecDirectoryOutputEvent.ToByteArray());
        }

        /// <summary>
        /// Write a key/value pair to the db
        /// </summary>
        public void WriteToDb(byte[] key, byte[] value)
        {
            Analysis.IgnoreResult(
                m_accessor.Use(database =>
                {
                    database.Put(key, value);
                })
            );
        }
    }
}
