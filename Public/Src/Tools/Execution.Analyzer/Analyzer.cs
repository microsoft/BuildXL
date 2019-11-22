// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Execution.Analyzer.Model;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Analyzer used to analyze cached graph and execution log
    /// </summary>
    public abstract class Analyzer : ExecutionAnalyzerBase
    {
        /// <summary>
        /// The loaded cached graph for the analyzer
        /// </summary>
        public readonly CachedGraph CachedGraph;

        /// <summary>
        /// Analysis inputs (XLG file location etc.)
        /// </summary>
        public readonly AnalysisInput Input;

        public LoggingContext LoggingContext { get; set; }

        protected Analyzer(AnalysisInput input)
            : base(input.CachedGraph.PipGraph)
        {
            Contract.Requires(input.CachedGraph != null);

            Input = input;
            CachedGraph = input.CachedGraph;
        }

        /// <summary>
        /// Prepares the analyzer and reads the execution log events into the analyzer
        /// </summary>
        public bool ReadExecutionLog(bool prepare = true)
        {
            if (prepare)
            {
                Prepare();
            }

            return ReadEvents();
        }

        protected virtual bool ReadEvents()
        {
            try
            {
                return Input.ReadExecutionLog(this);
            }
            catch (Exception e)
            {
                throw new InvalidDataException("Cannot read execution log; the version of the log has possibly changed.", e);
            }
        }

        #region Utility Methods

        public delegate TimeSpan GetElapsedLookup(NodeId node);

        public delegate TimeSpan GetKernelTimeLookup(NodeId node);

        public delegate TimeSpan GetUserTimeLookup(NodeId node);

        public delegate NodeAndCriticalPath GetCriticalPathLookup(NodeId node);

        internal string PrintCriticalPath(
            NodeAndCriticalPath nodeAndCriticalPath,
            GetElapsedLookup getElapsed,
            GetKernelTimeLookup getKernelTime,
            GetUserTimeLookup getUserTime,
            GetCriticalPathLookup getCriticalPath,
            out Model.CriticalPath criticalPath)
        {
            Contract.Assert(getElapsed != null, "getElapsed must be set.");
            Contract.Assert(getCriticalPath != null, "getCriticalPath must be set.");
            Contract.Assert(!nodeAndCriticalPath.Equals(default(NodeAndCriticalPath)), "nodeAndCriticalPath must be set.");

            criticalPath = new Model.CriticalPath();

            ConcurrentDictionary<PathAtom, TimeSpan> toolStats = new ConcurrentDictionary<PathAtom, TimeSpan>();
            var totalTimeWidth = ToSeconds(nodeAndCriticalPath.Time).Length;
            var totalKernelTimeWidth = ToSeconds(nodeAndCriticalPath.KernelTime).Length;
            var totalUserTimeWidth = ToSeconds(nodeAndCriticalPath.UserTime).Length;
            criticalPath.time = ToSeconds(nodeAndCriticalPath.Time);
            TimeSpan startTime = new TimeSpan(0);
            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var criticalPathWriter = new StringWriter(CultureInfo.InvariantCulture))
                {
                    while (true)
                    {
                        var pip = CachedGraph.PipGraph.GetPipFromUInt32(nodeAndCriticalPath.Node.Value);
                        var process = pip as Process;

                        var elapsed = getElapsed(nodeAndCriticalPath.Node);
                        var kernelTime = getKernelTime(nodeAndCriticalPath.Node);
                        var userTime = getUserTime(nodeAndCriticalPath.Node);

                        if (process != null)
                        {
                            toolStats.AddOrUpdate(process.GetToolName(CachedGraph.Context.PathTable), elapsed, (k, v) => v + elapsed);
                        }

                        var node = new Node();
                        node.pipId = pip.PipId.Value.ToString("X16", CultureInfo.InvariantCulture).TrimStart(new Char[] { '0' });
                        node.semiStableHash = pip.SemiStableHash.ToString("X");
                        node.pipType = pip.PipType.ToString();
                        node.duration = ToSeconds(elapsed);
                        node.kernelTime = ToSeconds(kernelTime);
                        node.userTime = ToSeconds(userTime);
                        node.shortDescription = pip.GetShortDescription(CachedGraph.Context);
                        node.startTime = ToSeconds(startTime);
                        startTime = startTime + elapsed;
                        criticalPath.nodes.Add(node);

                        criticalPathWriter.Write(
                            "({0}) ({2}) ({3}) [{1}]",
                            ToSeconds(elapsed).PadLeft(totalTimeWidth),
                            pip.GetDescription(CachedGraph.Context),
                            ToSeconds(kernelTime).PadLeft(totalKernelTimeWidth),
                            ToSeconds(userTime).PadLeft(totalUserTimeWidth));
                        if (nodeAndCriticalPath.Next.IsValid)
                        {
                            criticalPathWriter.WriteLine(" =>");
                        }
                        else
                        {
                            criticalPathWriter.WriteLine();
                            break;
                        }

                        nodeAndCriticalPath = getCriticalPath(nodeAndCriticalPath.Next);
                    }

                    output.WriteLine("TOOL STATS:");
                    foreach (var toolStatEntry in toolStats.OrderByDescending(kvp => kvp.Value))
                    {
                        output.WriteLine(
                            "{0}, {1}",
                            ToSeconds(toolStatEntry.Value).PadLeft(totalTimeWidth),
                            toolStatEntry.Key.ToString(CachedGraph.Context.StringTable));

                        var toolStat = new ToolStat();
                        toolStat.name = toolStatEntry.Key.ToString(CachedGraph.Context.StringTable);
                        toolStat.time = ToSeconds(toolStatEntry.Value);
                        criticalPath.toolStats.Add(toolStat);
                    }

                    output.WriteLine();
                    output.WriteLine("( Duration ) ( Kernel Time ) ( User Time ) Seconds ::  CRITICAL PATH DEPENDENCY LIST:");
                    output.Write(criticalPathWriter.ToString());
                }

                return output.ToString();
            }
        }

        protected static string ToSeconds(TimeSpan time)
        {
            return Math.Round(time.TotalSeconds, 3).ToString(CultureInfo.InvariantCulture);
        }

        public struct NodeAndCriticalPath
        {
            public NodeId Next;
            public NodeId Node;
            public TimeSpan Time;
            public TimeSpan KernelTime;
            public TimeSpan UserTime;
        }

        #endregion
    }
}
