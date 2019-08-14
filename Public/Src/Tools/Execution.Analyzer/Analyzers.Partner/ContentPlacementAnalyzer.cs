using System;
using System.Collections.Generic;
using System.Linq;

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Storage;
using BuildXL.Pips.Operations;
using BuildXL.Pips.Artifacts;
using Newtonsoft.Json;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

// @cesar: I added this file
namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeContentPlacementAnalyzer()
        {
            int sampleSize = 0;
            int sampleSizeHardLimit = int.MaxValue;
            string buildQueue = null;
            string buildId = null;
            long buildStartTicks = 0;
            double buildDurationMs = 0.0;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.StartsWith("sampleProportion", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("sp", StringComparison.OrdinalIgnoreCase))
                {
                    sampleSize = ParseInt32Option(opt, 0, int.MaxValue);
                }
                if (opt.Name.StartsWith("sampleCountHardLimit", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("schl", StringComparison.OrdinalIgnoreCase))
                {
                    sampleSizeHardLimit = ParseInt32Option(opt, 0, int.MaxValue);
                }
                if (opt.Name.StartsWith("buildQueue", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("bq", StringComparison.OrdinalIgnoreCase))
                {
                    buildQueue = ParseStringOption(opt);
                }
                if (opt.Name.StartsWith("buildId", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("bid", StringComparison.OrdinalIgnoreCase))
                {
                    buildId = ParseStringOption(opt);
                }
                if (opt.Name.StartsWith("buildStartTicks", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("bst", StringComparison.OrdinalIgnoreCase))
                {
                    buildStartTicks = ParseInt64Option(opt, 0, int.MaxValue);
                }
                if (opt.Name.StartsWith("buildDurationMs", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.StartsWith("bd", StringComparison.OrdinalIgnoreCase))
                {
                    buildDurationMs = ParseDoubleOption(opt, 0, double.MaxValue);
                }

            }
            return new ContentPlacementAnalyzer(GetAnalysisInput(), sampleSize, sampleSizeHardLimit, buildQueue, buildId, buildStartTicks, buildDurationMs) { };
        }

        private static void WriteContentPlacementAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Content Placement Analyzer");
            writer.WriteModeOption(nameof(AnalysisMode.ContentPlacement), "This analyzer parses a whole build (master perspective) and outputs content/workload-related data for machine learning purposes. It should only be run at the master node.");
            writer.WriteOption("sampleProportion", "Required. The number of artifacts that will be analyzed/saved/written. This number is a minimun, since we try to make sure to take at least one file of each extension.", shortName: "sp");
            writer.WriteOption("buildQueue", "Optional. The build queue in which this build ran. If not set, its necessary to get it when processing the output.", shortName: "bq");
            writer.WriteOption("buildId", "Optional. The BuildId for this build. If not set, its necessary to get it when processing the output.", shortName: "bid");
            writer.WriteOption("buildStartTicks", "Optional. The time (ticks) when the build started. If not set, its necessary to get it when processing the output.", shortName: "bst");
            writer.WriteOption("buildDurationMs", "Optional. The duration of this build (milliseconds). If not set, its necessary to get it when processing the output.", shortName: "bd");
            writer.WriteOption("sampleCountHardLimit", "Optional. This is a hard limit on the number of samples. We prioritize getting at least a file from each extension, but with this cap we cannot ensure that. It defaults to universe_size/2", shortName: "schl");

        }
    }

    /// <summary>
    /// Analyzer used to get stats on events (count and total size)
    /// </summary>
    internal sealed class ContentPlacementAnalyzer : Analyzer
    {

        private readonly string m_relativeOutputFile = "CpResults.json";
        private readonly Dictionary<uint, IList<PipExecutionPerformanceEventData>> m_scheduledPipExecutionData = new Dictionary<uint, IList<PipExecutionPerformanceEventData>>(capacity: 10 * 1000);
        private readonly Dictionary<uint, int> m_scheduledPipDependencyCount = new Dictionary<uint, int>(capacity: 10 * 1000);
        private readonly Dictionary<uint, int> m_scheduledPipInputCount = new Dictionary<uint, int>(capacity: 10 * 1000);
        private readonly Dictionary<uint, int> m_scheduledPipOutputCount = new Dictionary<uint, int>(capacity: 10 * 1000);
        private readonly Dictionary<FileArtifact, FileContentInfo> m_fileContentMap = new Dictionary<FileArtifact, FileContentInfo>(capacity: 10 * 1000);
        private readonly Dictionary<FileArtifact, IList<Pip>> m_artifactInputPips = new Dictionary<FileArtifact, IList<Pip>>(capacity: 10 * 1000);
        private readonly Dictionary<FileArtifact, IList<Pip>> m_artifactOutputPips = new Dictionary<FileArtifact, IList<Pip>>(capacity: 10 * 1000);
        private readonly Dictionary<string, IList<FileArtifact>> m_artifactsPerExtension = new Dictionary<string, IList<FileArtifact>>(capacity: 10 * 1000);
        private readonly Dictionary<string, int> m_countFilesPerExtension = new Dictionary<string, int>(capacity: 10 * 1000);
        private double m_sampleSize = 0;
        private double m_sampleSizeHardLimit = 0;
        private JsonTextWriter m_outputWriter = null;
        private string m_buildQueue = null;
        private string m_buildId = null;
        private long m_buildStartTicks = 0;
        private double m_buildDurationMs = 0.0;

        public ContentPlacementAnalyzer(AnalysisInput input, int sampleSize, int sampleSizeHardLimit, string buildQueue, string buildId, long buildStartTicks, double buildDurationMs) : base(input)
        {
            m_sampleSize = sampleSize;
            m_sampleSizeHardLimit = sampleSizeHardLimit;
            m_buildQueue = buildQueue;
            m_buildId = buildId;
            m_buildStartTicks = buildStartTicks;
            m_buildDurationMs = buildDurationMs;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            Console.WriteLine($"Processing pip graph (mapping input/outputs)");
            // fix this guy first here
            if (m_sampleSizeHardLimit == int.MaxValue)
            {
                m_sampleSizeHardLimit = m_fileContentMap.Count / 2;
            }
            // for each pip in the graph...
            int totalPips = 0;
            foreach (Pip pip in CachedGraph.PipGraph.RetrieveScheduledPips())
            {
                if (IncludePip(pip))
                {
                    // these are for the pip inputs. We want to identify who is the input of who,
                    // since we need to know their relationship...
                    PipArtifacts.ForEachInput(pip, input =>
                    {
                        if (input.IsFile && input.FileArtifact.IsSourceFile)
                        {
                            AddToDictionaryWithListAsValue(m_artifactInputPips, input.FileArtifact, pip);
                            CountInDictionary(m_scheduledPipInputCount, pip.PipId.Value);
                        }

                        return true;

                    },
                    includeLazyInputs: true);
                    // these are for the pip outputs. 
                    PipArtifacts.ForEachOutput(pip, output =>
                    {
                        if (output.IsFile)
                        {
                            AddToDictionaryWithListAsValue(m_artifactOutputPips, output.FileArtifact, pip);
                            CountInDictionary(m_scheduledPipOutputCount, pip.PipId.Value);
                        }

                        return true;
                    },
                    includeUncacheable: true);
                    // the dependencies, cause we want to count them...
                    foreach (var dependency in CachedGraph.PipGraph.RetrievePipImmediateDependencies(pip))
                    {
                        CountInDictionary(m_scheduledPipDependencyCount, pip.PipId.Value);
                    }
                    ++totalPips;
                }

            }
            Console.WriteLine($"Total artifacts is {m_fileContentMap.Count}, sampling (max possible samples = {m_sampleSizeHardLimit})");
            // now, for each file artifact...
            int processed = 0;
            int errors = 0;
            // sample
            var samples = SampleAccordingToOverallDistribution(m_sampleSize, new Random(Environment.TickCount));
            int printStep = samples.Count > 100000 ? 100000 : 1000;
            // go ahead...
            Console.WriteLine($"{samples.Count} after sampling ({m_fileContentMap.Count} total), printing...");
            try
            {
                m_outputWriter.WriteStartArray();
                foreach (var artifactEntry in samples)
                {
                    try
                    {
                        var artifact = artifactEntry.Key;
                        var artifactInfo = artifactEntry.Value;
                        var bxlArtifact = new BXLArtifact
                        {
                            Hash = artifactInfo.Hash.ToHex(),
                            ReportedFile = Path.GetFileName(artifact.Path.ToString(CachedGraph.Context.PathTable)),
                            ReportedSize = artifactInfo.Length,
                            BuidId = m_buildId,
                            BuildQueue = m_buildQueue,
                            BuildDurationMs = m_buildDurationMs,
                            BuildStartTimeTicks = m_buildStartTicks,
                            TotalPips = totalPips
                        };

                        var inputPipsForThisArtifact = GetOrReturnNull(m_artifactInputPips, artifact);
                        var outputPipsForThisArtifact = GetOrReturnNull(m_artifactOutputPips, artifact);
                        // and process this entry
                        if (inputPipsForThisArtifact != null)
                        {
                            foreach (var artifactPip in inputPipsForThisArtifact)
                            {
                                IList<PipExecutionPerformanceEventData> pipExecutionData;
                                if (m_scheduledPipExecutionData.TryGetValue(artifactPip.PipId.Value, out pipExecutionData))
                                {
                                    var executions = BXLPipData.BuildFromExecutionData(artifactPip, pipExecutionData, m_scheduledPipDependencyCount, m_scheduledPipInputCount, m_scheduledPipOutputCount, CachedGraph.Context.PathTable, CachedGraph.Context.StringTable);
                                    bxlArtifact.NumInputPips += executions.Count;
                                    foreach (var exec in executions)
                                    {
                                        bxlArtifact.InputPips.Add(exec);
                                    }
                                }
                            }
                        }
                        if (outputPipsForThisArtifact != null)
                        {
                            foreach (var artifactPip in outputPipsForThisArtifact)
                            {
                                IList<PipExecutionPerformanceEventData> pipExecutionData;
                                if (m_scheduledPipExecutionData.TryGetValue(artifactPip.PipId.Value, out pipExecutionData))
                                {
                                    var executions = BXLPipData.BuildFromExecutionData(artifactPip, pipExecutionData, m_scheduledPipDependencyCount, m_scheduledPipInputCount, m_scheduledPipOutputCount, CachedGraph.Context.PathTable, CachedGraph.Context.StringTable);
                                    bxlArtifact.NumOutputPips += executions.Count;
                                    foreach (var exec in executions)
                                    {
                                        bxlArtifact.OutputPips.Add(exec);
                                    }
                                }
                            }
                        }
                        // and save
                        WriteOutput(bxlArtifact);
                    }
#pragma warning disable ERP022
                    catch
                    {
                        // I dont really want to log this one, but is not bad to count the errors
                        ++errors;
                    }
#pragma warning restore ERP022
                    ++processed;
                    if (processed % printStep == 0)
                    {
                        Console.WriteLine($"{samples.Count - processed} artifacts remaining ({errors} errors...)");
                    }
                }
            }
            finally
            {
                m_outputWriter.WriteEndArray();
                Console.WriteLine($"{processed} artifacts printed, done...");
            }

            return 0;
        }


        public Dictionary<FileArtifact, FileContentInfo> SampleAccordingToOverallDistribution(double sampleSize, Random random)
        {
            if (m_fileContentMap.Count <= m_sampleSize)
            {
                return m_fileContentMap;
            }
            else
            {
                var fileArtifacts = new Dictionary<FileArtifact, FileContentInfo>();
                foreach (var extensionEntry in m_countFilesPerExtension)
                {
                    // get counts for each type of file
                    double weightedCount = Math.Ceiling(extensionEntry.Value * sampleSize / m_fileContentMap.Count);
                    // if we have less that the size, then take all
                    if (extensionEntry.Value < weightedCount)
                    {
                        foreach (var a in m_artifactsPerExtension[extensionEntry.Key])
                        {
                            fileArtifacts[a] = m_fileContentMap[a];
                            if (fileArtifacts.Count >= m_sampleSizeHardLimit)
                            {
                                return fileArtifacts;
                            }
                        }
                    }
                    else
                    {
                        // now, take a random set of files for each extension
                        var randomSet = new HashSet<int>();
                        while (randomSet.Count < weightedCount)
                        {
                            randomSet.Add(random.Next(extensionEntry.Value));
                        }
                        foreach (int position in randomSet)
                        {
                            var randomArtifact = m_artifactsPerExtension[extensionEntry.Key].ElementAt(position);
                            fileArtifacts[randomArtifact] = m_fileContentMap[randomArtifact];
                            if (fileArtifacts.Count >= m_sampleSizeHardLimit)
                            {
                                return fileArtifacts;
                            }
                        }
                    }

                }
                return fileArtifacts;
            }

        }

        public void WriteOutput(BXLArtifact artifact)
        {
            m_outputWriter.WriteStartObject();
            // so, we are going to write a json here. The first line hast the main info
            // (hash, build, queue, etc...). The following have only pip info
            WriteJsonPropertyToStream("Hash", artifact.Hash);
            WriteJsonPropertyToStream("BuildId", artifact.BuidId);
            WriteJsonPropertyToStream("BuildQueue", artifact.BuildQueue);
            WriteJsonPropertyToStream("BuildStartTimeTicks", artifact.BuildStartTimeTicks);
            WriteJsonPropertyToStream("BuildDurationMs", artifact.BuildDurationMs);
            WriteJsonPropertyToStream("ReportedFile", artifact.ReportedFile);
            WriteJsonPropertyToStream("ReportedSize", artifact.ReportedSize);
            WriteJsonPropertyToStream("TotalPips", artifact.TotalPips);
            WriteJsonPropertyToStream("NumInputPips", artifact.NumInputPips);
            WriteJsonPropertyToStream("NumOutputPips", artifact.NumOutputPips);
            m_outputWriter.WritePropertyName("InputPips");
            m_outputWriter.WriteStartArray();
            for (int i = 0; i < artifact.NumInputPips; ++i)
            {
                m_outputWriter.WriteStartObject();
                WriteJsonPropertyToStream("SemiStableHash", artifact.InputPips[i].SemiStableHash);
                WriteJsonPropertyToStream("Priority", artifact.InputPips[i].Priority);
                WriteJsonPropertyToStream("Weight", artifact.InputPips[i].Weight);
                WriteJsonPropertyToStream("TagCount", artifact.InputPips[i].TagCount);
                WriteJsonPropertyToStream("DependencyCount", artifact.InputPips[i].DependencyCount);
                WriteJsonPropertyToStream("InputCount", artifact.InputPips[i].InputCount);
                WriteJsonPropertyToStream("OutputCount", artifact.InputPips[i].OutputCount);
                WriteJsonPropertyToStream("SemaphoreCount", artifact.InputPips[i].SemaphoreCount);
                WriteJsonPropertyToStream("StartTimeTicks", artifact.InputPips[i].StartTimeTicks);
                WriteJsonPropertyToStream("Type", artifact.InputPips[i].Type);
                WriteJsonPropertyToStream("ExecutionLevel", artifact.InputPips[i].ExecutionLevel);
                m_outputWriter.WriteEndObject();
            }
            m_outputWriter.WriteEndArray();
            m_outputWriter.WritePropertyName("OutputPips");
            m_outputWriter.WriteStartArray();
            for (int i = 0; i < artifact.NumOutputPips; ++i)
            {
                m_outputWriter.WriteStartObject();
                WriteJsonPropertyToStream("SemiStableHash", artifact.OutputPips[i].SemiStableHash);
                WriteJsonPropertyToStream("Priority", artifact.OutputPips[i].Priority);
                WriteJsonPropertyToStream("Weight", artifact.OutputPips[i].Weight);
                WriteJsonPropertyToStream("TagCount", artifact.OutputPips[i].TagCount);
                WriteJsonPropertyToStream("DependencyCount", artifact.OutputPips[i].DependencyCount);
                WriteJsonPropertyToStream("InputCount", artifact.OutputPips[i].InputCount);
                WriteJsonPropertyToStream("OutputCount", artifact.OutputPips[i].OutputCount);
                WriteJsonPropertyToStream("SemaphoreCount", artifact.OutputPips[i].SemaphoreCount);
                WriteJsonPropertyToStream("StartTimeTicks", artifact.OutputPips[i].StartTimeTicks);
                WriteJsonPropertyToStream("Type", artifact.OutputPips[i].Type);
                WriteJsonPropertyToStream("ExecutionLevel", artifact.OutputPips[i].ExecutionLevel);
                m_outputWriter.WriteEndObject();
            }
            m_outputWriter.WriteEndArray();
            m_outputWriter.WriteEndObject();
            // done...
        }

        private void WriteJsonPropertyToStream<T>(string name, T value)
        {
            m_outputWriter.WritePropertyName(name);
            m_outputWriter.WriteValue(value);
        }

        public bool IncludePip(Pip pip)
        {
            return pip.PipType != PipType.HashSourceFile;
        }

        public override void Prepare()
        {
            string outputFile = $"{Path.Combine(Input.CachedGraphDirectory, m_relativeOutputFile)}";
            Console.WriteLine($"{GetType().FullName} starting. Output will be written to [{outputFile}]");
            m_outputWriter = new JsonTextWriter(new StreamWriter(outputFile));
            Contract.Requires(File.Exists(outputFile), $"Could not create [{outputFile}]...");
        }

        public override void Dispose()
        {
            if (m_outputWriter != null)
            {
                m_outputWriter.Close();
            }
            base.Dispose();
        }

        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            string extension = Path.GetExtension(data.FileArtifact.Path.ToString(CachedGraph.Context.PathTable));
            AddToDictionaryWithListAsValue(m_artifactsPerExtension, extension, data.FileArtifact);
            CountInDictionary(m_countFilesPerExtension, extension);
            m_fileContentMap[data.FileArtifact] = data.FileContentInfo;
        }

        /// <inheritdoc />
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            AddToDictionaryWithListAsValue(m_scheduledPipExecutionData, data.PipId.Value, data);
        }


        private IList<ValType> GetOrReturnNull<KeyType, ValType>(Dictionary<KeyType, IList<ValType>> dictionary, KeyType key)
        {
            if (dictionary.ContainsKey(key))
            {
                return dictionary[key];
            }
            else
            {
                return null;
            }
        }

        private void AddToDictionaryWithListAsValue<KeyType, ValType>(Dictionary<KeyType, IList<ValType>> dictionary, KeyType key, ValType value)
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key].Add(value);
            }
            else
            {
                dictionary.Add(key, new List<ValType>(new ValType[] { value }));
            }
        }

        private void CountInDictionary<KeyType>(Dictionary<KeyType, int> dictionary, KeyType key)
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] += 1;
            }
            else
            {
                dictionary.Add(key, 1);
            }
        }

    }

    public sealed class BXLPipData
    {
        public string SemiStableHash { get; set; } = null;
        public int Priority { get; set; } = 0;
        public int Weight { get; set; } = 0;
        public int TagCount { get; set; } = 0;
        public int SemaphoreCount { get; set; } = 0;
        public double DurationMs { get; set; } = -1;
        public string Type { get; set; } = null;
        public string ExecutionLevel { get; set; } = null;
        public int DependencyCount { get; set; } = 0;
        public int InputCount { get; set; } = 0;
        public int OutputCount { get; set; } = 0;
        public long StartTimeTicks { get; set; } = 0;

        public static List<BXLPipData> BuildFromExecutionData(Pip pip, IList<PipExecutionPerformanceEventData> executionData,
            Dictionary<uint, int> scheduledPipDependencyCount,
            Dictionary<uint, int> scheduledPipInputCount,
            Dictionary<uint, int> scheduledPipOutputCount,
            PathTable pathTable,
            StringTable stringTable)
        {
            var output = new List<BXLPipData>();
            foreach (var eData in executionData)
            {
                output.Add(BuildFromExecutionData(pip, eData, scheduledPipDependencyCount, scheduledPipInputCount, scheduledPipOutputCount, pathTable, stringTable));
            }
            return output;
        }

        public static BXLPipData BuildFromExecutionData(Pip pip, PipExecutionPerformanceEventData executionData,
            Dictionary<uint, int> scheduledPipDependencyCount,
            Dictionary<uint, int> scheduledPipInputCount,
            Dictionary<uint, int> scheduledPipOutputCount,
            PathTable pathTable,
            StringTable stringTable)
        {
            var pipData = new BXLPipData();
            pipData.SemiStableHash = pip.FormattedSemiStableHash;
            pipData.DurationMs = (executionData.ExecutionPerformance.ExecutionStop - executionData.ExecutionPerformance.ExecutionStart).TotalMilliseconds;
            pipData.Type = pip.PipType.ToString();
            pipData.ExecutionLevel = executionData.ExecutionPerformance.ExecutionLevel.ToString();
            pipData.StartTimeTicks = executionData.ExecutionPerformance.ExecutionStart.Ticks;
            pipData.TagCount = pip.Tags.Count();
            int depCount;
            int iCount;
            int oCount;
            scheduledPipDependencyCount.TryGetValue(pip.PipId.Value, out depCount);
            scheduledPipInputCount.TryGetValue(pip.PipId.Value, out iCount);
            scheduledPipOutputCount.TryGetValue(pip.PipId.Value, out oCount);
            // and set these ones too
            pipData.DependencyCount = depCount;
            pipData.InputCount = iCount;
            pipData.OutputCount = oCount;
            if (pip.PipType == PipType.Process)
            {
                var process = pip as Pips.Operations.Process;
                pipData.Priority = process.Priority;
                pipData.Weight = process.Weight;
                pipData.SemaphoreCount = process.Semaphores.Length;
            }
            // done...
            return pipData;
        }

    }

    public class BXLArtifact
    {
        public string Hash { get; set; } = null;
        public string ReportedFile { get; set; } = null;
        public long ReportedSize { get; set; } = -1;
        public string BuidId { get; set; } = null;
        public string BuildQueue { get; set; } = null;
        public long BuildStartTimeTicks { get; set; } = 0;
        public double BuildDurationMs { get; set; } = 0;
        public int TotalPips { get; set; } = 0;
        public int NumInputPips { get; set; } = 0;
        public List<BXLPipData> InputPips { get; set; } = new List<BXLPipData>();
        public int NumOutputPips { get; set; } = 0;
        public List<BXLPipData> OutputPips { get; set; } = new List<BXLPipData>();
    }
}

