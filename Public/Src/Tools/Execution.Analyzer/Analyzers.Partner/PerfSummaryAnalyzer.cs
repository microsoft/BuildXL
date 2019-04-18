// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using BuildXL.Execution.Analyzer;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializePerfSummaryAnalyzer()
        {
            string outputFilePath = null;
            string statsFile = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                if (opt.Name.Equals("statsFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("s", StringComparison.OrdinalIgnoreCase))
                {
                    statsFile = ParseSingletonPathOption(opt, statsFile);
                }
                else
                {
                    throw Error("Unknown option for perf summary analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            if (string.IsNullOrEmpty(statsFile))
            {
                throw Error("/statsFile parameter is required");
            }

            return new PerfSummary(GetAnalysisInput(), outputFilePath, statsFile);
        }

        private static void WritePerfSummaryAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Performance Summary Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.PerfSummary), "Dumps an html file that contains a performance summary");
            writer.WriteOption("outputFile", "Required. The location of the output file for critical path analysis.", shortName: "o");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    internal sealed class PerfSummary : Analyzer
    {
        private readonly string m_outputFilePath;
        private readonly string m_statsFile;

        private readonly HtmlHelper m_html;

        private readonly Dictionary<PipId, PipExecutionPerformance> m_pipExecutionPerf = new Dictionary<PipId, PipExecutionPerformance>();
        private DateTime m_firstPip = DateTime.MaxValue;

        internal PerfSummary(AnalysisInput input, string outputFilePath, string statsFile)
            : base(input)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outputFilePath));

            m_outputFilePath = outputFilePath;
            m_statsFile = statsFile;

            m_html = new HtmlHelper(PathTable, StringTable, SymbolTable, CachedGraph.PipTable);
        }

        public override int Analyze()
        {
            BuildXLStats stats;
            if (!BuildXLStats.TryLoadStatsFile(m_statsFile, out stats))
            {
                return 1;
            }

            var main = new XElement(
                "div",
                m_html.CreateBlock(
                    "Number fo Pips",
                    m_html.CreateRow("Total", CachedGraph.PipTable.Count),
                    GetEnumValues<PipType>().Select(pipType => m_html.CreateRow(pipType.ToString(), CachedGraph.PipGraph.RetrievePipReferencesOfType(pipType).Count()))),
                m_html.CreateBlock(
                    "Overall execution times",
                    m_html.CreateRow("TimeToFirstPip", stats.GetMiliSeconds("TimeToFirstPipSyntheticMs")),
                    m_html.CreateRow(" - Cache init", stats.GetMiliSeconds("CacheInitialization.DurationMs")),
                    m_html.CreateRow(" - Parse config", stats.GetMiliSeconds("ParseConfigFiles.DurationMs")),
                    m_html.CreateRow(" - GraphCache check", stats.GetMiliSeconds("GraphCacheReuseCheck.DurationMs")),
                    m_html.CreateRow(" - Graph construction", "..."),
                    m_html.CreateRow(" - Filter & Schedule", stats.GetMiliSeconds("ApplyFilterAndScheduleReadyNodes.DurationMs")),
                    m_html.CreateRow("Execute", stats.GetMiliSeconds("TimeToEngineRunCompleteMs") - stats.GetMiliSeconds("TimeToFirstPipSyntheticMs")),
                    m_html.CreateRow(" - HashSourceFile", stats.GetMiliSeconds("PipExecution.HashSourceFileDurationMs")),
                    m_html.CreateRow(" - CopyFile", stats.GetMiliSeconds("PipExecution.CopyFileDurationMs")),
                    m_html.CreateRow(" - WriteFile", stats.GetMiliSeconds("PipExecution.WriteFileDurationMs")),
                    m_html.CreateRow(" - Process", stats.GetMiliSeconds("PipExecution.ProcessDurationMs")),
                    m_html.CreateRow(" - -- HashDeps", stats.GetMiliSeconds("PipExecution.HashProcessDependenciesDurationMs")),
                    m_html.CreateRow(" - -- CacheCheck", stats.GetMiliSeconds("PipExecution.CheckProcessRunnableFromCacheDurationMs")),
                    m_html.CreateRow(" - -- Execution", stats.GetMiliSeconds("PipExecution.ExecuteProcessDurationMs")),
                    m_html.CreateRow(" - -- ProcessOutputs", stats.GetMiliSeconds("PipExecution.ProcessOutputsDurationMs")),
                    m_html.CreateRow(" - -- Continue", stats.GetMiliSeconds("OnPipCompletedDurationMs")),
                    m_html.CreateRow(" - HashSourceFile", stats.GetMiliSeconds("PipExecution.HashSourceFileDurationMs")),
                    m_html.CreateRow(" - HashSourceFile", stats.GetMiliSeconds("PipExecution.HashSourceFileDurationMs")),
                    m_html.CreateRow("Red flags", "..."),
                    m_html.CreateRow(" - ProcessPipCacheHits", stats["ProcessPipCacheHits"]),
                    m_html.CreateRow(" - PipsFailed", stats["PipsFailed"])),
                m_html.CreateBlock("UsageStatistics", RenderUsageStatistics(stats)));

            var doc = m_html.CreatePage("Performance Summary ", main);
            Directory.CreateDirectory(Path.GetDirectoryName(m_outputFilePath));
            doc.Save(m_outputFilePath);

            return 0;
        }

        private IEnumerable<T> GetEnumValues<T>()
        {
            foreach (T value in Enum.GetValues(typeof(PipType)))
            {
                yield return value;
            }
        }

        private XElement RenderUsageStatistics(BuildXLStats stats)
        {
            var actualUsage = new ActiveWorkerTracker();
            foreach (var kv in m_pipExecutionPerf.Values)
            {
                actualUsage.AddEntry(kv.ExecutionStart - m_firstPip, kv.ExecutionStop - m_firstPip);
            }

            int numberOfPips = CachedGraph.PipTable.Count + 1; // pipIds start at 1;
            Console.WriteLine("Processing nr of pipzies: " + numberOfPips);
            var refCounts = new int[numberOfPips];
            var startTime = new TimeSpan[numberOfPips];
            var startTick = new int[numberOfPips];
            var duration = new TimeSpan[numberOfPips];

            var maxSimulationTick = 0;
            var maxSimulationTime = TimeSpan.Zero;
            var minActualStart = DateTime.MaxValue;
            var maxActualStop = DateTime.MinValue;

            var readyQueue = new Queue<PipId>();

            // build RunState
            foreach (var pipId in CachedGraph.PipTable.Keys)
            {
                var pipIdx = pipId.Value;

                int incommingCount = CachedGraph.DataflowGraph.GetIncomingEdgesCount(pipId.ToNodeId());
                refCounts[pipIdx] = incommingCount;

                if (incommingCount == 0)
                {
                    startTime[pipIdx] = TimeSpan.Zero;
                    startTick[pipIdx] = 0;

                    readyQueue.Enqueue(pipId);
                }

                if (m_pipExecutionPerf.ContainsKey(pipId))
                {
                    var executionStart = m_pipExecutionPerf[pipId].ExecutionStart;
                    var executionStop = m_pipExecutionPerf[pipId].ExecutionStop;

                    minActualStart = minActualStart < executionStart ? minActualStart : executionStart;
                    maxActualStop = maxActualStop > executionStop ? maxActualStop : executionStop;

                    duration[pipIdx] = executionStop - executionStart;
                }
                else
                {
                    duration[pipIdx] = TimeSpan.Zero;
                }
            }

            while (readyQueue.Count != 0)
            {
                var pipId = readyQueue.Dequeue();

                var pipEndTime = startTime[pipId.Value] + duration[pipId.Value];
                var pipEndTick = startTick[pipId.Value] + 1;

                maxSimulationTick = Math.Max(maxSimulationTick, pipEndTick);
                maxSimulationTime = maxSimulationTime > pipEndTime ? maxSimulationTime : pipEndTime;

                foreach (var dependent in CachedGraph.PipGraph.RetrievePipReferenceImmediateDependents(pipId))
                {
                    var depId = dependent.PipId;
                    var depIdx = depId.Value;
                    var curCount = refCounts[depIdx];
                    Contract.Assert(curCount > 0);
                    curCount--;
                    refCounts[depIdx] = curCount;

                    if (curCount == 0)
                    {
                        startTime[depIdx] = pipEndTime;
                        startTick[depIdx] = pipEndTick;

                        readyQueue.Enqueue(depId);
                    }
                }
            }

            var simulateWithActualTime = new ActiveWorkerTracker();
            var simulateWithEqualTime = new ActiveWorkerTracker();

            for (int i = 1; i < numberOfPips; i++)
            {
                if (CachedGraph.PipTable.GetPipType(new PipId((uint)i)) != PipType.Process)
                {
                    // Skip pips other than HashSourceFile pips
                    continue;
                }

                simulateWithActualTime.AddEntry(startTime[i], startTime[i] + duration[i]);
                simulateWithEqualTime.AddEntry(TimeSpan.FromSeconds(startTick[i]), TimeSpan.FromSeconds(startTick[i] + 1));
            }

            int graphSize = 1000;

            var warnings = new XElement("div");

            long? processPipCacheHits = stats.GetValue("ProcessPipCacheHits");
            if (processPipCacheHits > 0)
            {
                warnings.Add(new XElement("div", new XAttribute("class", "warning"), $"Warning: This build had '{stats["ProcessPipCacheHits"]}' cache hits. Therefore the following graphs are not reliable for full graph analysis as those pips have a much shorter execution time than normal if they were cache hits. You can run the build with '/incremental-' to force cache misses."));
            }

            long? pipsFailed = stats.GetValue("PipsFailed");
            if (pipsFailed > 0)
            {
                warnings.Add(new XElement("div", new XAttribute("class", "warning"), $"Warning: This build had '{stats["PipsFailed"]}' failed pips. Therefore the following graphs are not complete for analysis. The failed pips as well as the pips that transitively depend on them will not be visible nor measured in these graphs "));
            }

            return new XElement(
                "div",
                warnings,
                CreateGraph(
                    actualUsage,
                    durationTime: maxActualStop - minActualStart,
                    durationDepth: null,
                    graphSize: graphSize,
                    title: "Actual start times & durations"),
                CreateGraph(
                    simulateWithActualTime,
                    durationTime: maxSimulationTime,
                    durationDepth: maxSimulationTick,
                    graphSize: graphSize,
                    title: "Simulate with infinite resources and actual durations"),
                CreateGraph(
                    simulateWithEqualTime,
                    durationTime: null,
                    durationDepth: maxSimulationTick,
                    graphSize: graphSize,
                    title: "Simulate with infinite resources and equal cost per pip"));
        }

        private XElement CreateGraph(ActiveWorkerTracker workTracker, TimeSpan? durationTime, int? durationDepth, int graphSize, string title)
        {
            var entries = workTracker.Entries.ToArray();

            int maxCount = entries.Max(e => e.Count);
            long maxTime = entries.Last().Time.Ticks;

            long lastTime = 0;
            var polyLine1Builder = new StringBuilder();

            var count = entries.Length;
            var meanIndex = (count / 2) + (count % 2);
            if (meanIndex >= count)
            {
                meanIndex = 0;
            }

            var mean = entries[meanIndex].Count;

            var sum = 0L;
            var weightedSum = 0L;
            var stdDevSq = 0L;

            foreach (var entry in entries)
            {
                polyLine1Builder.AppendLine($"{GetPoint(graphSize, entry.Count, maxCount)},{GetPoint(graphSize, lastTime, maxTime)}");
                polyLine1Builder.AppendLine($"{GetPoint(graphSize, entry.Count, maxCount)},{GetPoint(graphSize, entry.Time.Ticks, maxTime)}");

                weightedSum += entry.Count * (entry.Time.Ticks - lastTime);
                sum += entry.Count;
                stdDevSq += (entry.Count - mean) * (entry.Count - mean);

                lastTime = entry.Time.Ticks;
            }

            return new XElement(
                "div",
                new XElement("h3", title),
                new XElement(
                    "div",
                    m_html.CreateRow("Parellalism (max)", maxCount),
                    m_html.CreateRow("Parellalism (avg)", Convert.ToInt64(sum / (double)count)),
                    m_html.CreateRow("Parellalism (weighted avg)", Convert.ToInt64(weightedSum / (double)maxTime)),
                    m_html.CreateRow("Parellalism (mean)", mean),
                    m_html.CreateRow("Parellalism (stdDev)", Convert.ToInt64(Math.Sqrt(stdDevSq))),
                    durationTime.HasValue ? m_html.CreateRow("Duration (time)", durationTime.Value) : null,
                    durationDepth.HasValue ? m_html.CreateRow("Duration (depth)", durationDepth.Value) : null),
                new XElement(
                    "svg",
                    new XAttribute("version", "1.1"),
                    new XAttribute("width", graphSize),
                    new XAttribute("height", graphSize),
                    new XAttribute("style", "boder: 1px solid black"),
                    new XElement(
                        "polyline",
                        new XAttribute("fill", "none"),
                        new XAttribute("stroke", "black"),
                        new XAttribute("stroke-widt", "2"),
                        new XAttribute("points", $"0,0 0,{graphSize}, {graphSize},{graphSize} {graphSize},0 0,0")),
                    new XElement(
                        "polyline",
                        new XAttribute("fill", "none"),
                        new XAttribute("stroke", "green"),
                        new XAttribute("stroke-widt", "3"),
                        new XAttribute("points", polyLine1Builder.ToString()))));
        }

        private string GetPoint(int maxSize, long current, long total)
        {
            return (2 + ((maxSize - 4) * current / total)).ToString(CultureInfo.InvariantCulture);
        }

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            if (CachedGraph.PipTable.GetPipType(data.PipId) != PipType.Process)
            {
                // Skip other than Process
                return;
            }

            var executionPerformance = data.ExecutionPerformance;
            if (executionPerformance != null)
            {
                m_pipExecutionPerf.Add(data.PipId, executionPerformance);

                if (executionPerformance.ExecutionStart < m_firstPip)
                {
                    m_firstPip = executionPerformance.ExecutionStart;
                }
            }
        }
    }

    internal class ActiveWorkerTracker
    {
        private readonly List<UsageCountEntry> m_usageCountEntries = new List<UsageCountEntry>();

        public IEnumerable<UsageCountEntry> Entries => m_usageCountEntries;

        public void AddEntry(TimeSpan startTime, TimeSpan endTime)
        {
            Contract.Requires(endTime >= startTime, "Finish time cannot be earlier than the start time.");

            int previousCount = 0;
            int currentCount = 0;
            int currentIndex = 0;

            bool requiresStartInsert = true;
            for (; currentIndex < m_usageCountEntries.Count; currentIndex++)
            {
                UsageCountEntry value = m_usageCountEntries[currentIndex];

                if (startTime <= value.Time)
                {
                    if (startTime == value.Time)
                    {
                        requiresStartInsert = false;
                    }

                    currentCount = previousCount;
                    break;
                }

                previousCount = value.Count;
            }

            currentCount++;
            previousCount = currentCount;
            if (requiresStartInsert)
            {
                m_usageCountEntries.Insert(currentIndex, new UsageCountEntry { Time = startTime, Count = currentCount });
                currentIndex++;
            }

            bool requiresFinishInsert = true;
            for (; currentIndex < m_usageCountEntries.Count; currentIndex++)
            {
                UsageCountEntry value = m_usageCountEntries[currentIndex];
                if (endTime <= value.Time)
                {
                    if (endTime == value.Time)
                    {
                        requiresFinishInsert = false;
                    }

                    currentCount = previousCount;
                    break;
                }

                value.Count++;
                previousCount = value.Count;
            }

            if (requiresFinishInsert)
            {
                m_usageCountEntries.Insert(currentIndex, new UsageCountEntry { Time = endTime, Count = previousCount - 1 });
            }
        }

        internal class UsageCountEntry
        {
            public TimeSpan Time;
            public int Count;
        }
    }

    /// <summary>
    /// Class that wraps a BuildXLStats file and provides helpers
    /// </summary>
    public class BuildXLStats
    {
        private readonly Dictionary<string, long> m_stats;

        /// <nodoc />
        private BuildXLStats(Dictionary<string, long> stats)
        {
            Contract.Requires(stats != null);
            m_stats = stats;
        }

        /// <summary>
        /// Returns the stat as a sting, or NA if not found.
        /// </summary>
        public string this[string key] => GetString(key);

        /// <summary>
        /// Returns a value. Null if the key is not found
        /// </summary>
        public long? GetValue(string key)
        {
            if (m_stats.TryGetValue(key, out var value))
            {
                return value;
            }

            return null;
        }

        /// <summary>
        /// Returns a value. Null if the key is not found
        /// </summary>
        public TimeSpan? GetMiliSeconds(string key)
        {
            if (m_stats.TryGetValue(key, out var value))
            {
                return TimeSpan.FromMilliseconds(value);
            }

            return null;
        }

        /// <summary>
        /// Returns the stat as a string.
        /// </summary>
        public string GetString(string key)
        {
            if (m_stats.TryGetValue(key, out var value))
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            return "N/A";
        }

        /// <summary>
        /// Checks if the key is present and has a positive value.
        /// </summary>
        /// <remarks>
        /// Returns false if the key is not there or if the value is 0. true otherwise.
        /// </remarks>
        public bool GetBoolean(string key)
        {
            if (m_stats.TryGetValue(key, out var value))
            {
                return value > 0;
            }

            return false;
        }


        /// <summary>
        /// Checks if the key exists
        /// </summary>
        public bool Contains(string key)
        {
            return m_stats.ContainsKey(key);
        }

        /// <remarks>
        /// Parses and constructs a BuildXLStats object.
        /// </remarks>
        public static bool TryLoadStatsFile(string statsFile, out BuildXLStats buildXlStats)
        {
            buildXlStats = null;
            var stats = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var stream = new FileStream(statsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    while(!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();

                        var parts = line.Split('=');
                        if (parts.Length != 2)
                        {
                            Console.Error.WriteLine("Unexpected stats line: " + line);
                            return false;
                        }

                        long value;
                        if (!long.TryParse(parts[1], out value))
                        {
                            Console.Error.WriteLine("Unexpected stats line. After equals is not an integer:" + line);
                            return false;
                        }

                        if (string.IsNullOrEmpty(parts[0]))
                        {
                            Console.Error.WriteLine("Unexpected stats line. No Key before '=':" + line);
                            return false;
                        }

                        stats[parts[0]] = value;
                    }
                }
            }
            catch (IOException e)
            {
                Console.Error.WriteLine("Error reading stats file: " + statsFile + ": " + e.Message);
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                Console.Error.WriteLine("Error reading stats file: " + statsFile + ": " + e.Message);
                return false;
            }

            buildXlStats = new BuildXLStats(stats);
            return true;
        }
    }
}
