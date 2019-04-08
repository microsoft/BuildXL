// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace PipExecutionSimulator
{
    class Program
    {
        public static void CreateSimulationImage(SimulationResult result, string path)
        {
            var spans = result.GetSpans();
            var totalTime = result.TotalTime;
            var threadCount = result.Threads.Count;

            CreateSpanImage(path, spans, totalTime, threadCount);
        }

        private static void CreateSpanImage(string path, List<PipSpan> spans, ulong totalTime, int threadCount)
        {
            Bitmap bitmap = new Bitmap(1200, 800);
            Graphics graphics = Graphics.FromImage(bitmap);

            int width = 1000;
            int height = 600;
            graphics.FillRectangle(Brushes.Gray, 100, 100, width, height);
            Pen pen = new Pen(Brushes.Green, 1);

            int bottomY = 700;
            ulong currentTime = 0;
            ulong timeInterval = totalTime / (ulong)width;


            ConcurrentDenseIndex<double> timeSliceConcurrencies = new ConcurrentDenseIndex<double>(false);
            ConcurrentDenseIndex<int> minConcurrencies = new ConcurrentDenseIndex<int>(false);
            ConcurrentDenseIndex<int> maxConcurrencies = new ConcurrentDenseIndex<int>(false);

            ConcurrentDenseIndex<int> concurrencyIndex = new ConcurrentDenseIndex<int>(false);
            for (int i = 0; i < spans.Count; i++)
            {
                PipSpan s = spans[i];
                uint firstTimeSlice = (uint)(s.StartTime / timeInterval);
                uint lastTimeSlice = (uint)(s.EndTime / timeInterval);
                ((uint)width).Min(ref lastTimeSlice);


                uint firstFullTimeSlice = firstTimeSlice + 1;
                uint lastFullTimeSlice = lastTimeSlice - 1;

                for (uint sliceIndex = firstTimeSlice; sliceIndex <= lastTimeSlice; sliceIndex++)
                {
                    ulong sliceStart = sliceIndex * timeInterval;
                    ulong sliceEnd = sliceStart + timeInterval;
                    s.StartTime.Max(ref sliceStart);
                    s.EndTime.Min(ref sliceEnd);

                    double concurrency = ((double)(sliceEnd - sliceStart)) / (double)timeInterval;

                    timeSliceConcurrencies[sliceIndex] = timeSliceConcurrencies[sliceIndex] + concurrency;
                }
            }

            int lastConcurrency = 0;
            for (int i = 0; i < width; i++)
            {
                int x = 100 + i;
                Point bottom = new Point(x, bottomY);

                int concurrency = (int)lastConcurrency;


                int concurrencyHeight = (int)(timeSliceConcurrencies[(uint)i] * height / threadCount);
                var top = bottom;
                top.Offset(0, -concurrencyHeight);

                graphics.DrawLine(pen, top, bottom);

                currentTime += timeInterval;
            }


            bitmap.Save(path, ImageFormat.Png);
        }

        class Assert
        {
            internal static void AreEqual<T>(T expected, T actual)
            {
                bool areEqual = EqualityComparer<T>.Default.Equals(expected, actual);
                Debug.Assert(areEqual);
            }

            internal static void IsFalse(bool condition)
            {
                Debug.Assert(!condition);
            }
        }

        static void Main(string[] args)
        {
            PipExecutionData data = new PipExecutionData();
            if (args.Length != 1)
            {
                throw new ArgumentException("Specify directory path as only argument on command line");
            }

            string directory = args[0];
            string graphPath = Path.Combine(args[0], "graph.json");
            if (!File.Exists(graphPath))
            {
                throw new ArgumentException(string.Format("Graph does not exist at location: {0}", graphPath));
            }


            string resultsFormat = string.Format(@"{0}\sim\{{0}}", directory);
            Console.WriteLine("Loading graph...");

            Directory.CreateDirectory(string.Format(resultsFormat, ""));
            data.ReadAndCacheJsonGraph(Path.Combine(directory, "graph.json"));

            Console.WriteLine("Done");
            Console.WriteLine();

            Console.WriteLine("Saving runtime time table:");

            PipRuntimeTimeTable runtimeTable = new PipRuntimeTimeTable();

            foreach (var node in data.DataflowGraph.Nodes)
            {
                if (data.PipTypes[node] == PipType.Process)
                {
                    var stableId = (long)data.SemiStableHashes[node];
                    var duration = TimeSpan.FromTicks((long)data.Durations[node]);
                    uint milliseconds = (uint)Math.Min(duration.TotalMilliseconds, uint.MaxValue);
                    runtimeTable[stableId] = new PipHistoricPerfData(PipHistoricPerfData.DefaultTimeToLive, milliseconds);
                }
            }

            runtimeTable.SaveAsync(Path.Combine(directory, "RunningTimeTable")).Wait();

            Console.WriteLine("Done");
            Console.WriteLine();


            using (StreamWriter sw = new StreamWriter(resultsFormat.FormatWith("result.txt")))
            {
                var writers = new MultiWriter(sw, Console.Out);
                writers.WriteLine("Edge Count: {0}", data.DataflowGraph.EdgeCount);
                writers.WriteLine("Pip Type Counts:");
                foreach (var pipType in EnumTraits<PipType>.EnumerateValues())
                {
                    writers.WriteLine("{0}: {1}", data.PipTypeCounts[(int)pipType].ToString().PadLeft(10), pipType);
                }

                writers.WriteLine("Processes with timing information:{0} ", data.DataflowGraph.Nodes.Where(node => data.PipTypes[node] == PipType.Process && data.StartTimes[node] != 0).Count());

                File.WriteAllLines(resultsFormat.FormatWith("actual.durations.csv"), data.Spans.Where(ps => data.PipTypes[ps.Id] == PipType.Process).Select(ps => string.Join(",", data.GetName(ps.Id), ps.Duration.ToMinutes().ToString())));
                File.WriteAllLines(resultsFormat.FormatWith("actual.durations.txt"), data.Spans.Select(ps => ps.Duration.ToMinutes().ToString()));
                File.WriteAllLines(resultsFormat.FormatWith("actual.starts.txt"), data.Spans.Select(ps => ps.StartTime.ToMinutes().ToString()));
                File.WriteAllLines(resultsFormat.FormatWith("actual.ends.txt"), data.Spans.Select(ps => ps.EndTime.ToMinutes().ToString()));

                CreateSpanImage(resultsFormat.FormatWith("actual.png"), data.Spans, data.TotalDuration, Math.Max(data.ActualConcurrency, 1));

                SimulationResult actualSimulation = null;
                if (true)
                {
                    Console.WriteLine("Simulating actual build");
                    actualSimulation = new SimulationResult(data, data.AggregateCosts);
                    actualSimulation.Simulate((uint)data.ActualConcurrency);

                    File.WriteAllLines(resultsFormat.FormatWith("actualSimulation.durations.txt"), actualSimulation.GetSpans().Select(ps => ps.Duration.ToMinutes().ToString()));
                    File.WriteAllLines(resultsFormat.FormatWith("actualSimulation.starts.txt"), actualSimulation.GetSpans().Select(ps => ps.StartTime.ToMinutes().ToString()));
                    File.WriteAllLines(resultsFormat.FormatWith("actualSimulation.ends.txt"), actualSimulation.GetSpans().Select(ps => ps.EndTime.ToMinutes().ToString()));

                    string csvFormat = "{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}";


                    File.WriteAllLines(resultsFormat.FormatWith("actualSimulation.txt"), new string[] { csvFormat.FormatWith("Id", "Thread", "Minimum Start Time", "Start Time", "End Time", "Duration", "Incoming", "Outgoing") }.Concat(actualSimulation.GetSpans().Select(ps =>
                        csvFormat.FormatWith(
                            ps.Id.Value,
                            ps.Thread,
                            actualSimulation.MinimumStartTimes[ps.Id].ToMinutes(),
                            ps.StartTime.ToMinutes(),
                            ps.EndTime.ToMinutes(),
                            ps.Duration.ToMinutes(),
                            data.DataflowGraph.GetIncomingEdgesCount(ps.Id),
                            data.DataflowGraph.GetIncomingEdgesCount(ps.Id)))));

                    CreateSimulationImage(actualSimulation, resultsFormat.FormatWith("actualSimulation.png"));

                    Console.WriteLine("Done");
                }

                Console.WriteLine();


                writers.WriteLine("Actual Total Build Time: {0} min", data.TotalDuration.ToMinutes());
                writers.WriteLine("Actual Concurrency: {0}", data.ActualConcurrency);
                if (actualSimulation != null)
                {
                    writers.WriteLine("Simulated total build time (using actual concurrency): {0} min", actualSimulation.TotalTime.ToMinutes());
                }

                int nameWidth;

                int count = 0;
                ulong criticalPathCost = 0;
                foreach (var p in data.GetSortedPips(20, true, n => data.PipTypes[n] == PipType.Process, n => data.AggregateCosts[n]))
                {
                    List<NodeId> criticalPath = new List<NodeId>();
                    NodeId criticalChainNode = p.Node;
                    while (criticalChainNode.IsValid())
                    {
                        criticalPath.Add(criticalChainNode);
                        criticalChainNode = data.CriticalChain[criticalChainNode];
                    }

                    p.Priority.Max(ref criticalPathCost);

                    writers.WriteLine("Critical Path {0}:", count++);
                    writers.WriteLine("Critical Path Cost: {0} min", p.Priority.ToMinutes());
                    writers.WriteLine("Critical Path Length: {0}", criticalPath.Count);
                    nameWidth = criticalPath.Select(n => data.GetName(n).Length).Max();
                    writers.WriteLine("Critical Path:\n    {0}", string.Join(Environment.NewLine + " -> ", criticalPath
                        //.Where(n => data.Durations[n] > 0)
                        .Select(n => string.Format("{0} ({1} min) [{2}] <{3}>", data.GetName(n).PadLeft(nameWidth), data.Durations[n].ToMinutes(), data.PipIds[n], data.PipTypes[n]))));
                }
                    

                nameWidth = data.LongestRunningPips.Select(n => data.GetName(n.Node).Length).Max();
                writers.WriteLine();
                writers.WriteLine("Top 20 Long Running Pips:");
                foreach (var p in data.LongestRunningPips)
                {
                    writers.WriteLine("{0} [{2}]: {1} min", data.GetName(p.Node).PadLeft(nameWidth), p.Priority.ToMinutes(), data.PipIds[p.Node]);
                }

                writers.WriteLine();
                writers.WriteLine("Bottom 20 Shortest Running Processes:");
                foreach (var p in data.ShortestRunningProcesses)
                {
                    writers.WriteLine("{0} [{2}]: {1} min", data.GetName(p.Node).PadLeft(nameWidth), p.Priority.ToMinutes(), data.PipIds[p.Node]);
                }

                int simulationCount = 1;
                int increment = 70;
                int?[] threadCounts = new int?[simulationCount];
                threadCounts[0] = 60;
                //threadCounts[0] = 60;
                //threadCounts[4] = 330;
                //threadCounts[5] = 400;
                //threadCounts[6] = 470;
                SimulationResult[] results = new SimulationResult[simulationCount];
                Parallel.For(0, simulationCount, i =>
                    {
                        var threadCount = threadCounts[i] ?? ((i + 1) * increment);
                        Console.WriteLine("Simulating {0}...", threadCount);
                        SimulationResult result = new SimulationResult(data, data.AggregateCosts);
                        result.Simulate((uint)threadCount);
                        results[i] = result;
                        Console.WriteLine("Done {0}", threadCount);

                        //Console.WriteLine("Simulating adjusted{0}...", threadCount);
                        //SimulationResult adjusted = new SimulationResult(data, data.ComputeAggregateCosts(result.GetAdjustedDurations(criticalPathCost)));
                        //result.Simulate((uint)threadCount);
                        //results[i + simulationCount] = result;
                        //Console.WriteLine("Done adjusted {0}", threadCount);

                        //i = i + (results.Length / 2);

                        //Console.WriteLine("Simulating {0}...", i);
                        //SimulationResult adjusted = new SimulationResult(data, result.EndTimes);
                        //adjusted.Simulate((uint)result.Threads.Count);
                        //results[i] = adjusted;
                        //Console.WriteLine("Done {0}", i);
                    });

                results = results.OrderBy(s => s.Threads.Count).ToArray();

                string format = "{0}, {1}, {5}, {6}";
                writers.WriteLine();
                writers.WriteLine("Results");
                writers.WriteLine(format,
                    "Thread Count", // 0 
                    "Total Execution Time (min)", // 1
                    "Critical Path Length", // 2
                    "Critical Path Tail Pip", // 3
                    "Critical Path Idle Time (min)", // 4
                    "Average Utilization", // 5
                    "Max Path Length", // 6
                    "Executed Pips" // 7
                    );

                for (int i = 0; i < results.Length; i++)
                {
                    var result = results[i];
                    writers.WriteLine(format,
                        result.Threads.Count,
                        result.TotalTime.ToMinutes(),
                        result.CriticalPath.Executions.Count,
                        result.CriticalPath.Executions.Last().Id.Value,
                        result.CriticalPath.IdleTime.ToMinutes(),
                        Math.Round((double)result.TotalActiveTime / (double)((ulong)result.Threads.Count * result.TotalTime), 3),
                        result.Threads.Select(t => t.Executions.Count).Max(),
                        result.Threads.Select(t => t.Executions.Count).Sum());

                    CreateSimulationImage(result, resultsFormat.FormatWith("simulation.{0}.png".FormatWith(result.Threads.Count)));

                }
            }
        }

        public class MultiWriter
        {
            TextWriter[] Writers;

            public MultiWriter(params TextWriter[] writers)
            {
                Writers = writers;
            }

            public void WriteLine()
            {
                foreach (var writer in Writers)
                {
                    writer.WriteLine();
                }
            }

            public void WriteLine(string value)
            {
                foreach (var writer in Writers)
                {
                    writer.WriteLine(value);
                }
            }

            public void WriteLine(string value, params object[] args)
            {
                foreach (var writer in Writers)
                {
                    writer.WriteLine(value, args);
                }
            }
        }
    }

}
