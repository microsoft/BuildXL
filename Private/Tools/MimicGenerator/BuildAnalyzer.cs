// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Analyzes json build graphs to pull out various statistics and make comparisons. This created to pull various stats
    /// into a csv for excel.
    /// </summary>
    public static class BuildAnalyzer
    {
        private const string PhoneSpecDirRootMarker = "\\wm";
        private const string ConvertedSpecName = "sources.ds";

        private struct ProjectStats
        {
            public int ProcessCount;
            public int CopyFileCount;
            public int WriteFileCount;
            public int CppFileCount;
            public int CsFileCount;
            public int OutputFileCount;
        }

        /// <summary>
        /// Compares a wrapper spec and native spec build
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204")]
        public static void CompareWrapperAndConvPhoneBuilds()
        {
            // TODO:= Set these paths if you intend on using this
            var wrapperTask = ReadGraph(@"D:\mimic\graphs\\DBuild.M.x86fre.json");
            var convTask = ReadGraph(@"D:\mimic\graphs\PhonePartialNativeSpecX86fre.json");

            BuildGraph wrapperGraph = wrapperTask.Result;
            BuildGraph convGraph = convTask.Result;

            Dictionary<string, Tuple<List<Pip>, List<Pip>>> pipsBySpec = new Dictionary<string, Tuple<List<Pip>, List<Pip>>>();
            HashSet<string> convertedPaths = new HashSet<string>();

            foreach (var pip in wrapperGraph.Pips.Values)
            {
                string relativeSpec = PrepareCollection(pip, pipsBySpec);
                if (relativeSpec == null)
                {
                    continue;
                }

                pipsBySpec[relativeSpec].Item1.Add(pip);
            }

            foreach (var pip in convGraph.Pips.Values)
            {
                string relativeSpec = PrepareCollection(pip, pipsBySpec);

                if (pip.Spec.EndsWith(ConvertedSpecName, StringComparison.OrdinalIgnoreCase))
                {
                    convertedPaths.Add(relativeSpec);
                }

                if (relativeSpec == null)
                {
                    continue;
                }

                pipsBySpec[relativeSpec].Item2.Add(pip);
            }

            Console.WriteLine("WrapperProcesses,WrapperCopyFile,WrapperWriteFile,WrapperOutputCount,WasConverted,ConvProcesses,ConvCopyFile,ConvWriteFile,CppCount,CsCount,ConvPrediction,SpecDir");
            foreach (var item in pipsBySpec)
            {
                ProjectStats wrapperStats = GetStats(item.Value.Item1, wrapperGraph);
                ProjectStats convStats = GetStats(item.Value.Item2, convGraph);

                int convPrediction = 0;
                if (wrapperStats.CppFileCount > 0)
                {
                    convPrediction = wrapperStats.CppFileCount + 2;
                }
                else
                {
                    convPrediction = wrapperStats.ProcessCount;
                }

                bool wasConverted = convertedPaths.Contains(item.Key);

                Console.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}", wrapperStats.ProcessCount, wrapperStats.CopyFileCount, wrapperStats.WriteFileCount, wrapperStats.OutputFileCount,
                    wasConverted,
                    convStats.ProcessCount, convStats.CopyFileCount, convStats.WriteFileCount,
                    wrapperStats.CppFileCount, wrapperStats.CsFileCount,
                    convPrediction,
                    item.Key);
            }
        }

        private static Task<BuildGraph> ReadGraph(string fullPath)
        {
            return Task.Factory.StartNew(() =>
                {
                    GraphReader reader = new GraphReader(fullPath, null);
                    return reader.ReadGraph();
                });
        }

        private static string PrepareCollection(Pip pip, Dictionary<string, Tuple<List<Pip>, List<Pip>>> pipsBySpec)
        {
            int start = pip.Spec.IndexOf(PhoneSpecDirRootMarker, StringComparison.OrdinalIgnoreCase);

            if (start == -1)
            {
                return null;
            }

            string directory = Path.GetDirectoryName(pip.Spec);

            string relativeSpec = directory.Substring(start, directory.Length - start);
            Tuple<List<Pip>, List<Pip>> tuple;
            if (!pipsBySpec.ContainsKey(relativeSpec))
            {
                tuple = new Tuple<List<Pip>, List<Pip>>(new List<Pip>(), new List<Pip>());
                pipsBySpec.Add(relativeSpec, tuple);
            }

            return relativeSpec;
        }

        private static ProjectStats GetStats(IEnumerable<Pip> pips, BuildGraph graph)
        {
            ProjectStats stats = default(ProjectStats);
            HashSet<string> inputFiles = new HashSet<string>();
            HashSet<string> outputFiles = new HashSet<string>();

            foreach (Pip pip in pips)
            {
                Process process = pip as Process;

                if (process != null)
                {
                    stats.ProcessCount++;
                    foreach (int consumes in process.Consumes)
                    {
                        File f;
                        if (graph.Files.TryGetValue(consumes, out f))
                        {
                            inputFiles.Add(f.Location);
                        }
                    }

                    foreach (int produces in process.Produces)
                    {
                        File f;
                        if (graph.Files.TryGetValue(produces, out f))
                        {
                            outputFiles.Add(f.Location);
                        }
                    }

                    continue;
                }

                CopyFile copyFile = pip as CopyFile;
                if (copyFile != null)
                {
                    stats.CopyFileCount++;
                    continue;
                }

                WriteFile writeFile = pip as WriteFile;
                if (writeFile != null)
                {
                    stats.WriteFileCount++;
                    continue;
                }
            }

            stats.CppFileCount = inputFiles.Where(f => f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)).Count();
            stats.CsFileCount = inputFiles.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Count();
            stats.OutputFileCount = outputFiles.Count;

            return stats;
        }

        /// <summary>
        /// For debugging
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811")]
        private static string DescribeProcess(Process p, BuildGraph graph)
        {
            StringBuilder desc = new StringBuilder();
            desc.AppendLine("Consumes:");
            foreach (var file in p.Consumes)
            {
                File f;
                if (graph.Files.TryGetValue(file, out f))
                {
                    desc.Append(" ");
                    desc.AppendLine(f.Location);
                }
            }

            desc.AppendLine("Produces:");
            foreach (var file in p.Produces)
            {
                File f;
                if (graph.Files.TryGetValue(file, out f))
                {
                    desc.Append(" ");
                    desc.AppendLine(f.Location);
                }
            }

            return desc.ToString();
        }
    }
}
