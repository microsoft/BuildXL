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

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeXLGToDBAnalyzer()
        {
            string outputFilePath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for event stats analysis: {0}", opt.Name);
                }
            }

            return new XLGToDBAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteXLGToDBHelp(HelpWriter writer)
        {
            writer.WriteBanner("XLG to DB \"Analyzer\"");
            writer.WriteModeOption(nameof(AnalysisMode.XlgToDb), "Dumps event data from the xlg into a database.");
            writer.WriteOption("outputFile", "Required. The file where to write the results", shortName: "o");
        }
    }


    /// <summary>
    /// Analyzer to dump xlg events and other data into RocksDB
    /// </summary>
    internal sealed class XLGToDBAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file
        /// </summary>
        public string OutputFilePath;

        private DominoInvocationEventList m_domInvEveList = new DominoInvocationEventList();
        private KeyValueStoreAccessor Accessor { get; set; }
        private readonly bool m_accessorSucceeded = true;

        public XLGToDBAnalyzer(AnalysisInput input) : base(input)
        {
            try
            {
                Directory.Delete(@".\testDir", true);
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not delete directory with exception {0}, continuing to make Accessor", e);
            }

            var dataStore = new XLGppDataStore();
            if (dataStore.OpenDatastore(storeDirectory: @".\testDir"))
            {
                Accessor = dataStore.Accessor;
            }
            else
            {
                Console.WriteLine("Could not access RocksDB datastore. Exiting analyzer.");
                m_accessorSucceeded = false;
            }
        }

        public override int Analyze()
        {
            if (!m_accessorSucceeded)
            {
                return 0;
            }

            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var writer = new StreamWriter(outputStream))
                {
                    m_domInvEveList.WriteTo(outputStream);
                    writer.Flush();
                }
            }

            Analysis.IgnoreResult(
              Accessor.Use(database =>
              {
                  database.Put("foo", "bar");
                  database.Put("baz", "bar");
                  database.Put("bazar", "bar");
                  database.Put("bazsdf", "bar");
                  database.Put("var", "bar");
                  database.Put("log", "bar");
              })
            );

            Accessor.Dispose();
            return 0;
        }

        public override bool CanHandleEvent(ExecutionEventId eventId, uint workerId, long timestamp, int eventPayloadSize)
        {
            if (eventId.Equals(ExecutionEventId.DominoInvocation))
            {
                Console.WriteLine("Found a valid domino invocation event. The worker id is {0}, the timestamp is {1}, and the payload size is {2}.", workerId, timestamp, eventPayloadSize);
                return true;
            }

            // return false to keep the event from being parsed
            return false;
        }

        /// <summary>
        /// Override the DominoInvocationEvent to capture its data and store it in the protobuf 
        /// </summary>
        /// <param name="data"></param>
        public override void DominoInvocation(DominoInvocationEventData data)
        {
            var domInvEvent = new DominoInvocationEvent();
            var loggingConfig = data.Configuration.Logging;

            domInvEvent.SubstSource = loggingConfig.SubstSource.ToString(PathTable, PathFormat.HostOs);
            domInvEvent.SubstTarget = loggingConfig.SubstTarget.ToString(PathTable, PathFormat.HostOs);
            domInvEvent.IsSubstSourceValid = loggingConfig.SubstSource.IsValid;
            domInvEvent.IsSubstTargetValid = loggingConfig.SubstTarget.IsValid;

            m_domInvEveList.DomInvEventList.Add(domInvEvent);

            Console.WriteLine(loggingConfig.SubstSource.ToString(PathTable, PathFormat.HostOs));
            Console.WriteLine(loggingConfig.SubstTarget.ToString(PathTable, PathFormat.HostOs));
        }
    }
}
