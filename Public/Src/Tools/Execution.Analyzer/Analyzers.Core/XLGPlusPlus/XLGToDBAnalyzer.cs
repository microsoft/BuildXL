// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Google.Protobuf;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Native.IO;
using System.Diagnostics;
using BuildXL.Execution.Analyzer.Model;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using System.Text;
using BuildXL.Pips;

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


    /// <summary>
    /// Analyzer to dump xlg events and other data into RocksDB
    /// </summary>
    internal sealed class XLGToDBAnalyzer : Analyzer
    {
        public string OutputDirPath;

        /// <summary>
        /// Whetherthe initializing the accessor succeeded or not
        /// </summary>
        private bool m_accessorSucceeded;
        private KeyValueStoreAccessor Accessor { get; set; }

        /// <summary>
        /// Store workerID to pass into protobuf object to identify this event
        /// </summary>
        private uint WorkerID { get; set; }

        /// <summary>
        /// Count thetotal number of events processed
        /// </summary>
        private uint EventCount { get; set; }


        public XLGToDBAnalyzer(AnalysisInput input) : base(input) { }

        /// <inheritdoc/>
        public override void Prepare()
        {
            try
            {
                Directory.Delete(path: OutputDirPath, recursive: true);
            }
            catch (Exception e)
            {
                Console.WriteLine("No such dir or could not delete dir with exception {0}.\nIf dir still exists, this analyzer will append data to existing DB.", e);
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
            return 0;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            Analysis.IgnoreResult(
                Accessor.Use(database =>
                {
                    var ec = new EventCount_XLGpp();
                    ec.Value = EventCount;
                    database.Put(Encoding.ASCII.GetBytes("EventCount"), ec.ToByteArray());
                })
            );
        }

        /// <inheritdoc/>
        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            if (m_accessorSucceeded && (eventId == ExecutionEventId.FileArtifactContentDecided))
            {
                EventCount++;
                WorkerID = workerId;
                return true;
            }
            return false;
        }


        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var fileArtifactContentDecidedEvent = data.ToFileArtifactContentDecidedEvent_XLGpp(PathTable);

            Analysis.IgnoreResult(
              Accessor.Use(database =>
              {
                  var eq = new EventTypeQuery_XLGpp
                  {
                      EventTypeID = ExecutionEventId_XLGpp.FileArtifactContentDecided,
                      UUID = fileArtifactContentDecidedEvent.UUID
                  };

                  database.Put(eq.ToByteArray(), fileArtifactContentDecidedEvent.ToByteArray());
              })
            );
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void WorkerList(WorkerListEventData data)
        {
            base.WorkerList(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = new PipExecutionPerformanceEvent_XLGpp();
            var uuid = Guid.NewGuid().ToString();

            // TODO: Work with timestamp and other random structs
            pipExecPerfEvent.UUID = uuid;
            pipExecPerfEvent.PipID = data.PipId.Value;
            pipExecPerfEvent.PipExecutionLevel = (int)data.ExecutionPerformance.ExecutionLevel;
            pipExecPerfEvent.WorkerID = data.ExecutionPerformance.WorkerId;

            var innerProcesPipPerf = new PipExecutionPerformanceEvent_XLGpp.Types.ProcessPipExecutionPerformance();
            var performance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
            if (performance != null)
            {
                innerProcesPipPerf.PeakMemoryUsage = performance.PeakMemoryUsage;
                innerProcesPipPerf.PeakMemoryUsageMb = performance.PeakMemoryUsageMb;
                innerProcesPipPerf.NumberOfProcesses = performance.NumberOfProcesses;
                //innerProcesPipPerf.CacheDescriptorId = performance.CacheDescriptorId.Value;
            }

            pipExecPerfEvent.ProcessPipExecutionPerformance = innerProcesPipPerf;
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            base.DirectoryMembershipHashed(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            base.ProcessExecutionMonitoringReported(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            base.ProcessFingerprintComputed(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void ExtraEventDataReported(ExtraEventData data)
        {
            base.ExtraEventDataReported(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            base.DependencyViolationReported(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            base.PipExecutionStepPerformanceReported(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipCacheMiss(PipCacheMissEventData data)
        {
            base.PipCacheMiss(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void StatusReported(StatusEventData data)
        {
            base.StatusReported(data);
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void DominoInvocation(DominoInvocationEventData data)
        {
            var bxlInvEvent = data.ToBXLInvocationEvent_XLGpp(WorkerID, PathTable);
            
            Analysis.IgnoreResult(
              Accessor.Use(database =>
              {
                  var eq = new EventTypeQuery_XLGpp
                  {
                      EventTypeID = ExecutionEventId_XLGpp.DominoInvocation,
                      UUID = bxlInvEvent.UUID
                  };

                  database.Put(eq.ToByteArray(), bxlInvEvent.ToByteArray());
              })
            );
        }

        /// <summary>
        /// Override event to capture its data and store it in the protobuf 
        /// </summary>
        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            base.PipExecutionDirectoryOutputs(data);
        }

    }
}
