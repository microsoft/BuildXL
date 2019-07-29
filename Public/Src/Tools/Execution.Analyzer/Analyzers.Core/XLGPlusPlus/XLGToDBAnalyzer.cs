// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
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
            writer.WriteOption("outputDir", "Required. The directory to write out the RocksDB database", shortName: "o");
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
            m_actionBlock = new ActionBlockSlim<Action>(1, action => action());
        }

        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            return m_inner.CanHandleEvent(eventId, workerId, timestamp, eventPayloadSize);
        }

        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            m_actionBlock.Post(() =>
            {
                m_inner.WorkerID.Value = CurrentEventWorkerId;
                data.Metadata.LogToTarget(data, m_inner);
            });
        }

        public override void Prepare()
        {
            m_inner.Prepare();
        }

        public override int Analyze()
        {
            m_actionBlock.Complete();
            return m_inner.Analyze();
        }

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
        public string OutputDirPath;

        /// <summary>
        /// Whether initializing the accessor succeeded or not
        /// </summary>
        private bool m_accessorSucceeded;
        private KeyValueStoreAccessor Accessor { get; set; }

        private Stopwatch m_stopWatch;

        /// <summary>
        /// Store WorkerID.Value to pass into protobuf object to identify this event
        /// </summary>
        public ThreadLocal<uint> WorkerID = new ThreadLocal<uint>();

        /// <summary>
        /// Count the total number of events processed
        /// </summary>
        private uint EventCount { get; set; }


        public XLGToDBAnalyzerInner(AnalysisInput input) : base(input)
        {
            m_stopWatch = new Stopwatch();
            m_stopWatch.Start();
        }

        /// <inheritdoc/>
        public override void Prepare()
        {
            try
            {
                Directory.Delete(path: OutputDirPath, recursive: true);
            }
            catch (Exception)
            {
                Console.WriteLine("Directory entered does not exist, creating directory for DB.");
            }

            var accessor = KeyValueStoreAccessor.Open(storeDirectory: OutputDirPath);

            if (accessor.Succeeded)
            {
                Accessor = accessor.Result;
                m_accessorSucceeded = true;
            }
            else
            {
                Console.Error.WriteLine("Could not access RocksDB datastore. Exiting analyzer.");
            }
            EventCount = 0;
        }

        /// <inheritdoc/>
        public override int Analyze()
        {
            Accessor.Dispose();
            Console.WriteLine("Num events ingested = {0}", EventCount);
            Console.WriteLine("Total time elapsed: {0} seconds", m_stopWatch.ElapsedMilliseconds / 1000.0);
            return 0;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            var ec = new EventCount_XLGpp
            {
                Value = EventCount
            };
            WriteToDb(Encoding.ASCII.GetBytes("EventCount"), ec.ToByteArray());
        }

        /// <inheritdoc/>
        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            if (m_accessorSucceeded && eventId != ExecutionEventId.ObservedInputs)
            {
                EventCount++;
                WorkerID.Value = workerId;

                if (EventCount % 10000 == 0)
                {
                    Console.WriteLine("Processed {0} events so far.", EventCount);
                    Console.WriteLine("Total time elapsed: {0} seconds", m_stopWatch.ElapsedMilliseconds / 1000.0);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var fileArtifactContentDecidedEvent = data.ToFileArtifactContentDecidedEvent_XLGpp(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.FileArtifactContentDecided,
                UUID = fileArtifactContentDecidedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), fileArtifactContentDecidedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void WorkerList(WorkerListEventData data)
        {
            var workerListEvent = data.ToWorkerListEvent_XLGpp(WorkerID.Value);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.WorkerList,
                UUID = workerListEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), workerListEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = data.ToPipExecutionPerformanceEvent_XLGpp();
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.PipExecutionPerformance,
                UUID = pipExecPerfEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipExecPerfEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            var directoryMembershipEvent = data.ToDirectoryMembershipHashedEvent_XLGpp(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.DirectoryMembershipHashed,
                UUID = directoryMembershipEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), directoryMembershipEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            var processExecMonitoringReportedEvent = data.ToProcessExecutionMonitoringReportedEvent_XLGpp(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.ProcessExecutionMonitoringReported,
                UUID = processExecMonitoringReportedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), processExecMonitoringReportedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            var processFingerprintComputedEvent = data.ToProcessFingerprintComputationEvent_XLGpp(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.ProcessFingerprintComputation,
                UUID = processFingerprintComputedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), processFingerprintComputedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ExtraEventDataReported(ExtraEventData data)
        {
            var extraEvent = data.ToExtraEventDataReported_XLGpp(WorkerID.Value);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.ExtraEventDataReported,
                UUID = extraEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), extraEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            var dependencyViolationEvent = data.ToDependencyViolationReportedEvent_XLGpp(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.DependencyViolationReported,
                UUID = dependencyViolationEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), dependencyViolationEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            var pipExecStepPerformanceEvent = data.ToPipExecutionStepPerformanceReportedEvent_XLGpp(WorkerID.Value);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.PipExecutionStepPerformanceReported,
                UUID = pipExecStepPerformanceEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipExecStepPerformanceEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipCacheMiss(PipCacheMissEventData data)
        {
            var pipCacheMissEvent = data.ToPipCacheMissEvent_XLGpp(WorkerID.Value);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.PipCacheMiss,
                UUID = pipCacheMissEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipCacheMissEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void StatusReported(StatusEventData data)
        {
            var statusReportedEvent = data.ToResourceUsageReportedEvent_XLGpp(WorkerID.Value);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.ResourceUsageReported,
                UUID = statusReportedEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), statusReportedEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DominoInvocation(DominoInvocationEventData data)
        {
            var bxlInvEvent = data.ToBXLInvocationEvent_XLGpp(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.DominoInvocation,
                UUID = bxlInvEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), bxlInvEvent.ToByteArray());
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            var pipExecDirectoryOutputEvent = data.ToPipExecutionDirectoryOutputsEvent_XLGpp(WorkerID.Value, PathTable);
            var eq = new EventTypeQuery_XLGpp
            {
                EventTypeID = ExecutionEventId_XLGpp.PipExecutionDirectoryOutputs,
                UUID = pipExecDirectoryOutputEvent.UUID
            };

            WriteToDb(eq.ToByteArray(), pipExecDirectoryOutputEvent.ToByteArray());
        }

        public void WriteToDb(byte[] key, byte[] value)
        {
            Console.WriteLine("Processing Event {0}", EventCount);
            Analysis.IgnoreResult(
              Accessor.Use(database =>
              {
                  database.Put(key, value);
              })
            );
        }
    }
}
