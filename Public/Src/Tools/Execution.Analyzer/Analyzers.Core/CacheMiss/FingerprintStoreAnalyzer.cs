// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Execution.Analyzer.Analyzers.CacheMiss;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeFingerprintStoreAnalyzer(AnalysisInput oldAnalysisInput, AnalysisInput newAnalysisInput)
        {
            string outputDirectory = null;
            bool allPips = false;
            bool noBanner = false;
            long sshValue = -1;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseSingletonPathOption(opt, outputDirectory);
                }
                else if(opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    sshValue = ParseSemistableHash(opt);
                }
                else if (opt.Name.StartsWith("allPips", StringComparison.OrdinalIgnoreCase))
                {
                    allPips = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("nobanner", StringComparison.OrdinalIgnoreCase))
                {
                    noBanner = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for cache miss analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new Exception("'outputDirectory' is required.");
            }

            if (allPips && sshValue != -1)
            {
                throw new Exception("'allPips' can't be true if pipId is set.");
            }

            return new FingerprintStoreAnalyzer(oldAnalysisInput, newAnalysisInput)
            {
                OutputDirectory = outputDirectory,
                AllPips = allPips,
                SemiStableHashToRun = sshValue,
                NoBanner = noBanner
            };
        }

        private static void WriteFingerprintStoreAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("(BETA) Cache Miss Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.CacheMiss), "Computes cache miss reasons for pips");
            writer.WriteOption("outputDirectory", "Required. The directory where to write the results", shortName: "o");
            writer.WriteOption("allPips", "Optional. Defaults to false.");
            writer.WriteOption("pipId", "Optional. Run for specific pip.", shortName: "p");
        }
    }

    internal class FingerprintStoreAnalyzer : Analyzer
    {
        /// <summary>
        /// The name of the output file for the analysis.
        /// </summary>
        public const string AnalysisFileName = "analysis.txt";

        /// <summary>
        /// The path to the output directory.
        /// </summary>
        public string OutputDirectory;

        /// <summary>
        /// Whether all pip cache misses should be analyzed or just the frontier of cache misses.
        /// </summary>
        public bool AllPips;

        /// <summary>
        /// Whether or not to display the header text.
        /// </summary>
        public bool NoBanner;

        /// <summary>
        /// Run cache miss for this pip
        /// </summary>
        public long SemiStableHashToRun;

        /// <summary>
        /// Analysis model based on the new build.
        /// </summary>
        private readonly AnalysisModel m_model;

        /// <summary>
        /// Analysis file writer.
        /// </summary>
        private TextWriter m_writer;

        private FingerprintStoreReader m_oldReader;

        private FingerprintStoreReader m_newReader;

        private FingerprintStoreReader m_newCacheLookupReader;

        private readonly string m_oldStoreLocation;

        private readonly string m_newStoreLocation;

        private readonly string m_newCacheLookupStoreLocation;

        /// <summary>
        /// Constructor.
        /// </summary>
        public FingerprintStoreAnalyzer(AnalysisInput analysisInputOld, AnalysisInput analysisInputNew)
            : base(analysisInputNew)
        {
            if (analysisInputOld.ExecutionLogPath.Equals(analysisInputNew.ExecutionLogPath, StringComparison.OrdinalIgnoreCase))
            {
                var fingerprintStoreDirectories = Directory.GetDirectories(Path.GetDirectoryName(analysisInputNew.ExecutionLogPath), Scheduler.Scheduler.FingerprintStoreDirectory, SearchOption.AllDirectories);
                if (fingerprintStoreDirectories.Length != 2)
                {
                    throw new BuildXLException($"Expecting two '{Scheduler.Scheduler.FingerprintStoreDirectory}' directories under log path '{Path.GetDirectoryName(analysisInputNew.ExecutionLogPath)}'");
                }
                else
                {
                    m_oldStoreLocation = fingerprintStoreDirectories.First(x => !x.EndsWith(Scheduler.Scheduler.FingerprintStoreDirectory));
                    m_newStoreLocation = fingerprintStoreDirectories.First(x => x.EndsWith(Scheduler.Scheduler.FingerprintStoreDirectory));
                    m_newCacheLookupStoreLocation = fingerprintStoreDirectories.FirstOrDefault(x => x.EndsWith(Scheduler.Scheduler.FingerprintStoreDirectory + LogFileExtensions.CacheLookupFingerprintStore));

                    Console.WriteLine($"Comparing old fingerprint store {m_oldStoreLocation} with new one {m_newStoreLocation}");
                    m_model = new AnalysisModel(CachedGraph);
                }
            }
            else
            {
                m_oldStoreLocation = GetStoreLocation(analysisInputOld);
                m_newStoreLocation = GetStoreLocation(analysisInputNew);
                try
                {
                    m_newCacheLookupStoreLocation = GetStoreLocation(analysisInputNew, LogFileExtensions.CacheLookupFingerprintStore);
                }
                catch (BuildXLException)
                {
                    m_newCacheLookupStoreLocation = null;
                }
            }

            m_model = new AnalysisModel(CachedGraph);
        }

        protected override bool ReadEvents()
        {
            // Do nothing. This analyzer does not read events.
            return true;
        }

        public static string GetStoreLocation(AnalysisInput analysisInput, string storeSuffix = "")
        {
            var fingerprintStoreDirectories = Directory.GetDirectories(Path.GetDirectoryName(analysisInput.ExecutionLogPath), Scheduler.Scheduler.FingerprintStoreDirectory + storeSuffix, SearchOption.AllDirectories);
            if (fingerprintStoreDirectories.Length == 0)
            {
                throw new BuildXLException($"Zero '{Scheduler.Scheduler.FingerprintStoreDirectory}' directories under log path '{Path.GetDirectoryName(analysisInput.ExecutionLogPath)}'");
            }
            else if (fingerprintStoreDirectories.Length > 1)
            {
                throw new BuildXLException($"More than one '{Scheduler.Scheduler.FingerprintStoreDirectory}' directories under log path '{Path.GetDirectoryName(analysisInput.ExecutionLogPath)}'");
            }

            return fingerprintStoreDirectories[0];
        }

        private Process HydratePip(PipId pipId)
        {
           return (Process)PipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
        }

        /// <summary>
        /// Writes the header for the cache miss analyzer.
        /// </summary>
        private void WriteHeader()
        {
            WriteLine("Comparing executions");
            WriteLine($"Old: {m_oldStoreLocation}");
            WriteLine($"New: {m_newStoreLocation}");
            WriteLine();

            WriteLine(($"For details about analyzer output see: {Strings.ExecutionAnalyzer_HelpLink}"));
            WriteLine();
        }

        /// <summary>
        /// Prepares the analyzer for producing outputs.
        /// </summary>
        public override void Prepare()
        {
            m_oldReader = FingerprintStoreReader.Create(m_oldStoreLocation, Path.Combine(OutputDirectory, "old")).Result;
            m_newReader = FingerprintStoreReader.Create(m_newStoreLocation, Path.Combine(OutputDirectory, "new")).Result;

            m_newCacheLookupReader = m_newCacheLookupStoreLocation != null ? FingerprintStoreReader.Create(m_newCacheLookupStoreLocation, Path.Combine(OutputDirectory, "new" + LogFileExtensions.CacheLookupFingerprintStore)).Result : null;

            m_writer = new StreamWriter(Path.Combine(OutputDirectory, AnalysisFileName));
            if (!NoBanner)
            {
                WriteHeader();
            }
        }

        /// <summary>
        /// Analyzes cache misses.
        /// </summary>
        public override int Analyze()
        {
            Console.WriteLine("Starting fingerprint store analysis at: " + DateTime.Now.ToString());
            if (!m_newReader.TryGetCacheMissList(out var cacheMissList))
            {
                WriteLine("Could not retrieve cache miss list for analysis. There may have been a failure during the build.");
                return 0;
            }

            Console.WriteLine("Finished getting list of pips that had a cache miss at: " + DateTime.Now.ToString());

            if (m_oldReader.StoreVersion != m_newReader.StoreVersion)
            {
                WriteLine($"WARNING: Format version numbers of the fingerprint store do not match. Old: {m_oldReader.StoreVersion}, New: {m_newReader.StoreVersion}.");
            }

            if (SemiStableHashToRun != -1)
            {
                var firstMiss = cacheMissList.FirstOrDefault(x => PipTable.GetPipSemiStableHash(x.PipId) == SemiStableHashToRun);
                if (firstMiss.CacheMissType == PipCacheMissType.Invalid)
                {
                    foreach (var pipId in PipTable.StableKeys)
                    {
                        var possibleMatch = PipTable.GetPipSemiStableHash(pipId);
                        if (possibleMatch == SemiStableHashToRun)
                        {
                            firstMiss = new PipCacheMissInfo(pipId, PipCacheMissType.Hit);
                        }
                    }
                }

                Console.WriteLine("Analyzing single pip starting at: " + DateTime.Now.ToString());

                AnalyzePip(firstMiss, HydratePip(firstMiss.PipId), m_writer);
            }
            else
            {
                var frontierCacheMissList = new List<PipCacheMissInfo>();
                foreach (var miss in cacheMissList)
                {
                    if (m_model.HasChangedDependencies(miss.PipId) && !AllPips)
                    {
                        continue;
                    }

                    frontierCacheMissList.Add(miss);
                    m_model.MarkChanged(miss.PipId);
                }

                Console.WriteLine("Finding frontier pips done at " + DateTime.Now.ToString());
                int i = 0;
                foreach (var miss in frontierCacheMissList)
                {
                    if (i % 10 == 0)
                    {
                        Console.WriteLine("Done " + i + " of " + cacheMissList.Count);
                    }

                    var pip = HydratePip(miss.PipId);
                    WriteLine($"================== Analyzing pip ========================");

                    AnalyzePip(miss, pip, m_writer);
                    WriteLine("================== Complete pip ========================");
                    WriteLine();
                    i++;
                }
            }

            return 0;
        }

        private void AnalyzePip(PipCacheMissInfo miss, Process pip, TextWriter writer)
        {
            string pipUniqueOutputHashStr = null;

            if ((pip as Process).TryComputePipUniqueOutputHash(PathTable, out var pipUniqueOutputHash, m_model.CachedGraph.MountPathExpander))
            {
                pipUniqueOutputHashStr = pipUniqueOutputHash.ToString();
            }
            WriteLine(pip.GetDescription(PipGraph.Context));

            var analysisResult = CacheMissAnalysisResult.Invalid;
            if (m_newCacheLookupReader != null
                && miss.CacheMissType == PipCacheMissType.MissForDescriptorsDueToStrongFingerprints
                && m_newCacheLookupReader.Store.ContainsFingerprintStoreEntry(pip.FormattedSemiStableHash, pipUniqueOutputHashStr))
            {
                // Strong fingerprint miss analysis is most accurate when compared to the fingerprints computed at cache lookup time
                // because those fingerprints capture the state of the disk at cache lookup time, including dynamic observations
                analysisResult = CacheMissAnalysisUtilities.AnalyzeCacheMiss(
                    writer,
                    miss,
                    () => m_oldReader.StartPipRecordingSession(pip, pipUniqueOutputHashStr),
                    () => m_newCacheLookupReader.StartPipRecordingSession(pip, pipUniqueOutputHashStr));
            }
            else
            {
                analysisResult = CacheMissAnalysisUtilities.AnalyzeCacheMiss(
                    m_writer,
                    miss,
                    () => m_oldReader.StartPipRecordingSession(pip, pipUniqueOutputHash.ToString()),
                    () => m_newReader.StartPipRecordingSession(pip, pipUniqueOutputHash.ToString()));
            }

            if (analysisResult == CacheMissAnalysisResult.MissingFromOldBuild)
            {
                Tracing.Logger.Log.FingerprintStorePipMissingFromOldBuild(LoggingContext);
            }
            else if (analysisResult == CacheMissAnalysisResult.MissingFromNewBuild)
            {
                Tracing.Logger.Log.FingerprintStorePipMissingFromNewBuild(LoggingContext);
            }
            else if (analysisResult == CacheMissAnalysisResult.UncacheablePip)
            {
                Tracing.Logger.Log.FingerprintStoreUncacheablePipAnalyzed(LoggingContext);
            }
        }

        private void WriteLine(params TextWriter[] pipFileWriters)
        {
            m_writer.WriteLine();

            foreach (var writer in pipFileWriters)
            {
                writer?.WriteLine();
            }
        }

        private void WriteLine(string message, params TextWriter[] pipFileWriters)
        {
            m_writer.WriteLine(message);

            foreach (var writer in pipFileWriters)
            {
                writer?.WriteLine(message);
            }
        }

        public override void Dispose()
        {
            m_oldReader.Dispose();
            m_newReader.Dispose();
            m_writer.Dispose();
            base.Dispose();
        }
    }
}
