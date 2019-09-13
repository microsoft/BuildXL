#if NET_FRAMEWORK

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Storage;
using BuildXL.Pips.Operations;
using BuildXL.Pips.Artifacts;
using Newtonsoft.Json;
using ContentPlamentAnalysisTools.Core.Analyzer;
using BuildXL.Utilities.Collections;
using System.Diagnostics;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeContentPlacementAnalyzer()
        {
            double sampleProportion = 0;
            int sampleSizeHardLimit = int.MaxValue;
            string buildQueue = null;
            string buildId = null;
            long buildStartTicks = 0;
            double buildDurationMs = 0.0;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("sampleProportion", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("sp", StringComparison.OrdinalIgnoreCase))
                {
                    sampleProportion = ParseDoubleOption(opt, 0.01, 1.0);
                }
                if (opt.Name.Equals("sampleCountHardLimit", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("schl", StringComparison.OrdinalIgnoreCase))
                {
                    sampleSizeHardLimit = ParseInt32Option(opt, 0, int.MaxValue);
                }
                if (opt.Name.Equals("buildQueue", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("bq", StringComparison.OrdinalIgnoreCase))
                {
                    buildQueue = ParseStringOption(opt);
                }
                if (opt.Name.Equals("buildId", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("bid", StringComparison.OrdinalIgnoreCase))
                {
                    buildId = ParseStringOption(opt);
                }
                if (opt.Name.Equals("buildStartTicks", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("bst", StringComparison.OrdinalIgnoreCase))
                {
                    buildStartTicks = ParseInt64Option(opt, 0, long.MaxValue);
                }
                if (opt.Name.Equals("buildDurationMs", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("bd", StringComparison.OrdinalIgnoreCase))
                {
                    buildDurationMs = ParseDoubleOption(opt, 0, double.MaxValue);
                }
            }
            Contract.Requires(sampleProportion > 0, "Sample proportion needs to be specified and be within bounds"); 
            return new ContentPlacementAnalyzer(GetAnalysisInput(), sampleProportion, sampleSizeHardLimit, buildQueue, buildId, buildStartTicks, buildDurationMs) { };
        }

        private static void WriteContentPlacementAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Content Placement Analyzer");
            writer.WriteModeOption(nameof(AnalysisMode.ContentPlacement), "This analyzer parses a whole build (master perspective) and outputs content/workload-related data for machine learning purposes. It should only be run at the master node logs.");
            writer.WriteOption("sampleProportion", "Required ( 0.01 <= sampleProportion <= 1.0). The proportion of the artifacts (per extension) that will be sampled.", shortName: "sp");
            writer.WriteOption("buildQueue", "Optional. The build queue in which this build ran. If not set, its necessary to get it when processing the output.", shortName: "bq");
            writer.WriteOption("buildId", "Optional. The BuildId for this build. If not set, its necessary to get it when processing the output.", shortName: "bid");
            writer.WriteOption("buildStartTicks", "Optional. The time (ticks) when the build started. If not set, its necessary to get it when processing the output.", shortName: "bst");
            writer.WriteOption("buildDurationMs", "Optional. The duration of this build (milliseconds). If not set, its necessary to get it when processing the output.", shortName: "bd");
            writer.WriteOption("sampleCountHardLimit", "Optional. This is a hard limit on the number of samples, if set no more than this amount of artifacts will be in the sample.", shortName: "schl");
        }
    }

    /// <summary>
    /// Analyzer used to get data from file artifacts. It outputs a file that contains all the sampled artifact data. The samples are taken proportionally for each extension.
    /// For example, if we have a build with 100 artifacts and sampleProportion=0.8, then from each extension, 80% of the files
    /// are taken at random and rounding UP, meaning that at least one file of each extension will be taken. This could lead to take more than 80% of the files, but that is ok
    /// from my point of view. If you want to put a hard cap on the count, then use sampleCountHardLimit.
    /// </summary>
    internal sealed class ContentPlacementAnalyzer : Analyzer
    {

        private readonly string m_relativeOutputFile = "CpResults.json";
        private readonly MultiValueDictionary<uint, byte> m_scheduledPipDependencyCount = new MultiValueDictionary<uint, byte>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<uint, byte> m_scheduledPipInputCount = new MultiValueDictionary<uint, byte>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<uint, byte> m_scheduledPipOutputCount = new MultiValueDictionary<uint, byte>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<string, byte> m_countFilesPerExtension = new MultiValueDictionary<string, byte>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<FileArtifact, byte> m_countEmptyArtifacts = new MultiValueDictionary<FileArtifact, byte>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<uint, PipExecutionPerformanceEventData> m_scheduledPipExecutionData = new MultiValueDictionary<uint, PipExecutionPerformanceEventData>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<FileArtifact, Pip> m_artifactInputPips = new MultiValueDictionary<FileArtifact, Pip>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<FileArtifact, Pip> m_artifactOutputPips = new MultiValueDictionary<FileArtifact, Pip>(capacity: 10 * 1000);
        private readonly MultiValueDictionary<string, FileArtifact> m_artifactsPerExtension = new MultiValueDictionary<string, FileArtifact>(capacity: 10 * 1000);
        private readonly Dictionary<FileArtifact, FileContentInfo> m_fileContentMap = new Dictionary<FileArtifact, FileContentInfo>(capacity: 10 * 1000);
        private double m_sampleProportion = 0;
        private double m_sampleSizeHardLimit = 0;
        private string m_buildQueue = null;
        private string m_buildId = null;
        private long m_buildStartTicks = 0;
        private double m_buildDurationMs = 0.0;
        private JsonTextWriter m_outputWriter = null;

        public ContentPlacementAnalyzer(AnalysisInput input, double sampleProportion, int sampleSizeHardLimit, string buildQueue, string buildId, long buildStartTicks, double buildDurationMs) : base(input)
        {
            m_sampleProportion = sampleProportion;
            m_sampleSizeHardLimit = sampleSizeHardLimit;
            m_buildQueue = buildQueue;
            m_buildId = buildId;
            m_buildStartTicks = buildStartTicks;
            m_buildDurationMs = buildDurationMs;
        }

        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            // before doing anything, lets skip the empty file, since it does not bring anything to the analysis
            if(data.FileContentInfo.Length > 0)
            {
                string extension = Path.GetExtension(data.FileArtifact.Path.ToString(CachedGraph.Context.PathTable));
                m_artifactsPerExtension.Add(extension, data.FileArtifact);
                m_countFilesPerExtension.Add(extension, 1);
                m_fileContentMap[data.FileArtifact] = data.FileContentInfo;
            }
            else
            {
                m_countEmptyArtifacts.Add(data.FileArtifact, 1);
            }
        }

        /// <inheritdoc />
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            m_scheduledPipExecutionData.Add(data.PipId.Value, data);
        }

        public override void Prepare()
        {
            string outputFile = $"{Path.Combine(Directory.GetParent(Input.CachedGraphDirectory).FullName, m_relativeOutputFile)}";
            Console.WriteLine($"Output will be written to [{outputFile}]");
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


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            Console.WriteLine($"Analyzing log file...");
            var stopWatch = Stopwatch.StartNew();
            try
            {
                // process the pips in the graph
                int totalPips = ProcessPips();
                if(totalPips > 0)
                {
                    var samples = SampleArtifacts();
                    if(samples.Count > 0)
                    {
                        // so, start by creating the build itself
                        var build = new BxlBuild
                        {
                            Meta = new BxlBuildMeta()
                            {
                                BuidId = m_buildId,
                                BuildQueue = m_buildQueue,
                                BuildDurationMs = m_buildDurationMs,
                                BuildStartTimeTicks = m_buildStartTicks,
                                TotalPips = totalPips,
                                TotalArtifacts = m_fileContentMap.Count,
                                EmptyArtifacts = m_countEmptyArtifacts.Count,
                                SampledArtifacts = samples.Count
                            }
                        };
                        // and now for each artifact, add it to the build
                        PopulateBuild(samples, build);
                        // and finally write
                        WriteToJsonStream(build);
                    }
                    else
                    {
                        Console.WriteLine("After sampling no files remained, so output will be empty...");
                    }
                    
                }
                else
                {
                    Console.WriteLine("This build does not seem to have pips, no output will be written...");
                }
                return 0;
            }
            catch(Exception e)
            {
                Console.WriteLine("An error ocurred when analyzing build...");
                Console.WriteLine(e.ToString());
                throw;
            }
            finally
            {
                stopWatch.Stop();
                Console.WriteLine($"Done, file analized in {stopWatch.ElapsedMilliseconds}ms");
            }
        }

        private int ProcessPips()
        {
            var pipCount = 0;
            Console.WriteLine($"Processing pip graph (mapping input/outputs)");
            Stopwatch stopWatch = null;
            try
            {
                stopWatch = Stopwatch.StartNew();
                foreach (var pip in CachedGraph.PipGraph.RetrieveScheduledPips())
                {
                    if (IncludePip(pip))
                    {
                        // these are for the pip inputs. We want to identify who is the input of who,
                        // since we need to know their relationship...
                        PipArtifacts.ForEachInput(pip, input =>
                        {
                            if (input.IsFile && input.FileArtifact.IsSourceFile)
                            {
                                m_artifactInputPips.Add(input.FileArtifact, pip);
                                m_scheduledPipInputCount.Add(pip.PipId.Value, 1);
                            }
                            return true;
                        },
                        includeLazyInputs: true);
                        // these are for the pip outputs. 
                        PipArtifacts.ForEachOutput(pip, output =>
                        {
                            if (output.IsFile)
                            {
                                m_artifactOutputPips.Add(output.FileArtifact, pip);
                                m_scheduledPipOutputCount.Add(pip.PipId.Value, 1);
                            }
                            return true;
                        },
                        includeUncacheable: true);
                        // the dependencies, cause we want to count them...
                        foreach (var dependency in CachedGraph.PipGraph.RetrievePipImmediateDependencies(pip))
                        {
                            m_scheduledPipDependencyCount.Add(pip.PipId.Value, 1);
                        }
                        ++pipCount;
                    }
                }
                return pipCount;
            }
            catch(Exception)
            {
                Console.WriteLine("Propagating exception when processing pips...");
                throw;
            }
            finally
            {
                if(stopWatch != null)
                {
                    stopWatch.Stop();
                    Console.WriteLine($"Done, processed {pipCount} pips in {stopWatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    Console.WriteLine("There is an error when creating a stopwatch...");
                }
            }
        }

        private Dictionary<FileArtifact, FileContentInfo> SampleArtifacts()
        {
            Console.WriteLine($"Sampling artifacts, going to ignore {m_countEmptyArtifacts.Count} empty artifacts");
            var stopWatch = Stopwatch.StartNew();
            var samples = SampleAccordingToExtensionDistribution(m_sampleProportion, new Random(Environment.TickCount));
            stopWatch.Stop();
            Console.WriteLine($"Done, processed {m_fileContentMap.Count} artifacts in {stopWatch.ElapsedMilliseconds}ms. {samples.Count} artifacts will be used as a sample");
            return samples;
        }

        private void PopulateBuild(Dictionary<FileArtifact, FileContentInfo> samples, BxlBuild build)
        {
            int processed = 0;
            int errors = 0;
            Console.WriteLine($"Populating build with samples");
            var stopWatch = Stopwatch.StartNew();
            foreach (var artifactEntry in samples)
            {
                try
                {
                    var artifact = artifactEntry.Key;
                    var artifactInfo = artifactEntry.Value;
                    var bxlArtifact = new BxlArtifact
                    {
                        Hash = artifactInfo.Hash.ToHex(),
                        ReportedFile = artifact.Path.ToString(CachedGraph.Context.PathTable),
                        ReportedSize = artifactInfo.Length
                    };
                    if (m_artifactInputPips.TryGetValue(artifact, out var inputPipsForThisArtifact))
                    {
                        foreach (var artifactPip in inputPipsForThisArtifact)
                        {
                            if (m_scheduledPipExecutionData.TryGetValue(artifactPip.PipId.Value, out var pipExecutionData))
                            {
                                var executions = BuildBxlPipsFromExecutionData(artifactPip, pipExecutionData, m_scheduledPipDependencyCount, m_scheduledPipInputCount, m_scheduledPipOutputCount);
                                foreach (var exec in executions)
                                {
                                    bxlArtifact.InputPips.Add(exec);
                                }
                            }
                        }
                    }
                    if (m_artifactOutputPips.TryGetValue(artifact, out var outputPipsForThisArtifact))
                    {
                        foreach (var artifactPip in outputPipsForThisArtifact)
                        {
                            if (m_scheduledPipExecutionData.TryGetValue(artifactPip.PipId.Value, out var pipExecutionData))
                            {
                                var executions = BuildBxlPipsFromExecutionData(artifactPip, pipExecutionData, m_scheduledPipDependencyCount, m_scheduledPipInputCount, m_scheduledPipOutputCount);
                                foreach (var exec in executions)
                                {
                                    bxlArtifact.OutputPips.Add(exec);
                                }
                            }
                        }
                    }
                    build.Artifacts.Add(bxlArtifact);
                }
#pragma warning disable ERP022
                catch
                {
                    ++errors;
                }
#pragma warning restore ERP022
                ++processed;
            }
            stopWatch.Stop();
            Console.WriteLine($"Done, {processed} artifacts added in {stopWatch.ElapsedMilliseconds}ms ({errors} errors)");
        }

        private void WriteToJsonStream(BxlBuild build)
        {
            Console.WriteLine($"Writing output ({build.Artifacts.Count} artifacts)");
            var stopWatch = Stopwatch.StartNew();
            build.WriteToJsonStream(m_outputWriter);
            stopWatch.Stop();
            Console.WriteLine($"Done, output written in {stopWatch.ElapsedMilliseconds}ms");
        }

        private Dictionary<FileArtifact, FileContentInfo> SampleAccordingToExtensionDistribution(double sampleProportion, Random random)
        {
            var fileArtifacts = new Dictionary<FileArtifact, FileContentInfo>();
            foreach(var entry in m_countFilesPerExtension)
            {
                var name = entry.Key;
                var count = entry.Value.Count;
                double weightedCount = Math.Ceiling(count * sampleProportion);
                // now, take a random set of files for each extension
                var randomSet = new HashSet<int>();
                while (randomSet.Count < weightedCount)
                {
                    randomSet.Add(random.Next(count));
                }
                foreach (var position in randomSet)
                {
                    var randomArtifact = m_artifactsPerExtension[name].ElementAt(position);
                    fileArtifacts[randomArtifact] = m_fileContentMap[randomArtifact];
                    if (fileArtifacts.Count >= m_sampleSizeHardLimit)
                    {
                        return fileArtifacts;
                    }
                }
            }
            return fileArtifacts;
        }

        private bool IncludePip(Pip pip)
        {
            return pip.PipType != PipType.HashSourceFile;
        }

        private List<BxlPipData> BuildBxlPipsFromExecutionData(
            Pip pip, 
            IReadOnlyList<PipExecutionPerformanceEventData> executionData,
            MultiValueDictionary<uint, byte> scheduledPipDependencyCount,
            MultiValueDictionary<uint, byte> scheduledPipInputCount,
            MultiValueDictionary<uint, byte> scheduledPipOutputCount)
        {
            var output = new List<BxlPipData>();
            foreach (var eData in executionData)
            {
                output.Add(BuildBxlPipFromExecutionData(pip, eData, scheduledPipDependencyCount, scheduledPipInputCount, scheduledPipOutputCount));
            }
            return output;
        }

        private BxlPipData BuildBxlPipFromExecutionData(
            Pip pip, 
            PipExecutionPerformanceEventData executionData,
            MultiValueDictionary<uint, byte> scheduledPipDependencyCount,
            MultiValueDictionary<uint, byte> scheduledPipInputCount,
            MultiValueDictionary<uint, byte> scheduledPipOutputCount)
        {
            var pipData = new BxlPipData
            {
                SemiStableHash = pip.FormattedSemiStableHash,
                DurationMs = (executionData.ExecutionPerformance.ExecutionStop - executionData.ExecutionPerformance.ExecutionStart).TotalMilliseconds,
                Type = pip.PipType.ToString(),
                ExecutionLevel = executionData.ExecutionPerformance.ExecutionLevel.ToString(),
                StartTimeTicks = executionData.ExecutionPerformance.ExecutionStart.Ticks,
                TagCount = pip.Tags.Count()
            };
            if(scheduledPipDependencyCount.TryGetValue(pip.PipId.Value, out var deps))
            {
                pipData.DependencyCount = deps.Count;
            }

            if(scheduledPipInputCount.TryGetValue(pip.PipId.Value, out var inputs))
            {
                pipData.InputCount = inputs.Count;
            }
            if(scheduledPipOutputCount.TryGetValue(pip.PipId.Value, out var outputs))
            {
                pipData.OutputCount = outputs.Count;
            }
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

}

#endif
