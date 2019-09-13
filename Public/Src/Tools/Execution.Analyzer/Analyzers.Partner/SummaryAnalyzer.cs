// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Execution.Analyzer.Analyzers;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
#if !DISABLE_FEATURE_HTMLWRITER
using System.Web.UI;
#endif

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeSummaryAnalyzer(AnalysisInput analysisInput, bool ignoreProcessEvents = false)
        {
            string diffFilePath = null;
            var htmlOutput = false;
            var filterOptions = new FilterOptions()
            {
                NoCriticalPathReport = false,
                TransitiveDownPips = false,
                IgnoreAbsentPathProbe = false,
                ProcessPipsPerformance = false,
                IgnoreProcessEvents = ignoreProcessEvents,
            };

            int maximumCountOfDifferencesToReport = 20; // Report ordered by reference count so 20 gives an idea of most impactful.
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    diffFilePath = ParseSingletonPathOption(opt, diffFilePath);
                }
                else if (opt.Name.StartsWith("html", StringComparison.OrdinalIgnoreCase))
                {
                    htmlOutput = ParseBooleanOption(opt);
                }
                else if (opt.Name.StartsWith("count", StringComparison.OrdinalIgnoreCase))
                {
                    maximumCountOfDifferencesToReport = ParseInt32Option(opt, 10, 500);
                }
                else if (opt.Name.StartsWith("noCritical", StringComparison.OrdinalIgnoreCase))
                {
                    filterOptions.NoCriticalPathReport = true;
                }
                else if (opt.Name.StartsWith("dumpTransitiveDownPips", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("d", StringComparison.OrdinalIgnoreCase))
                {
                    filterOptions.TransitiveDownPips = true;
                }
                else if (opt.Name.StartsWith("AbsentPathProbe", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("app", StringComparison.OrdinalIgnoreCase))
                {
                    filterOptions.IgnoreAbsentPathProbe = true;
                }
                else if (opt.Name.StartsWith("ProcessPipsPerformance", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("pp", StringComparison.OrdinalIgnoreCase))
                {
                    filterOptions.ProcessPipsPerformance = true;
                }
                else
                {
                    throw Error("Unknown option for log compare analysis: {0}", opt.Name);
                }
            }

            if (htmlOutput)
            {
                // HTML reports are not performant for large number of differences
                // this limits the top pips to report on and its transitive impact.
                maximumCountOfDifferencesToReport = 20;
            }

            return new SummaryAnalyzer(analysisInput, maximumCountOfDifferencesToReport, filterOptions)
            {
                ComparedFilePath = diffFilePath,
                HtmlOutput = htmlOutput,
            };
        }

        private static void WriteSummaryAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Summary Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.LogCompare), "Generates report from compare two execution logs on high impact differences that produce cache invalidation");
            writer.WriteOption("outputFile", "Required. The directory containing the cached pip graph files.", shortName: "o");
            writer.WriteOption("html", "Optional. Generates HTML report output.");
            writer.WriteOption("count", "Optional. The maximum count of differences to report. Default = 20.");
            writer.WriteOption("noCritical", "Optional. Supress reporting critical paths");
            writer.WriteOption("dumpTransitiveDownPips", "Optional. Dumps dependent process Pips that were executed.", shortName: "d");
            writer.WriteOption("ProcessPipsPerformance (pp)", "Optional. Include process Pip performance only.");
        }
    }

    internal struct FilterOptions
    {
        public bool NoCriticalPathReport;
        public bool TransitiveDownPips;
        public bool IgnoreAbsentPathProbe;
        public bool IgnoreProcessEvents;
        public bool ProcessPipsPerformance;
        public bool WhatBuilt;
    }

    internal sealed class FileArtifactSummary : DependencySummary<string>
    {
        /// <summary>
        /// FileArtifact.
        /// </summary>
        public FileArtifact FileArtifact;

        /// <summary>
        /// FileArtifact content.
        /// </summary>
        public readonly FileContentInfo FileContentInfo;

        /// <summary>
        /// Origin either build, pulled, not materialized for sources from cache.
        /// </summary>
        public PipOutputOrigin OutputOrigin;

        /// <summary>
        /// True when artifact is referenced by process pip
        /// </summary>
        public bool ReferencedByProcessPip()
        {
            return Count > 0;
        }

        public FileArtifactSummary(string path, FileArtifact fileArtifact, FileContentInfo fileContentInfo, PipOutputOrigin outputOrigin)
            : base(path)
        {
            FileArtifact = fileArtifact;
            FileContentInfo = fileContentInfo;
            OutputOrigin = outputOrigin;
        }

        public bool IsUntrackedFile => FileContentInfo.Hash.Equals(WellKnownContentHashes.UntrackedFile);

        public bool IsAbsentFile => FileContentInfo.Hash.Equals(WellKnownContentHashes.AbsentFile);

        public string GetHashName()
        {
            if (IsUntrackedFile)
            {
                return "UntrackedFile";
            }

            if (IsAbsentFile)
            {
                return "AbsentFile";
            }

            return FileContentInfo.Hash.ToHex();
        }

        /// <summary>
        /// Artifact list summary
        /// </summary>
        public override List<string> ToList(int totalPips)
        {
            var referencePercentage = totalPips > 0 ? (int)((Count * 100) / totalPips) : 0;
            var summary = new List<string>()
                          {
                              Name,
                              FileArtifact.IsSourceFile ? "source" : "output",
                              OutputOrigin.ToString(),
                              Count.ToString(CultureInfo.InvariantCulture),
                              referencePercentage.ToString(CultureInfo.InvariantCulture),
                              GetHashName(),
                          };
            return summary;
        }
    }

    internal sealed class ObservedInputSummary : DependencySummary<string>
    {
        public readonly ObservedInput ObservedInput;

        public ObservedInputSummary(string path, ObservedInput observedInput, bool cached)
            : base(path, cached)
        {
            ObservedInput = observedInput;
        }

        public bool IsUntrackedFile => ObservedInput.Hash.Equals(WellKnownContentHashes.UntrackedFile);

        public bool IsAbsentFile => ObservedInput.Hash.Equals(WellKnownContentHashes.AbsentFile);

        public bool AbsentPathProbe => IsAbsentFile && ObservedInput.Type == ObservedInputType.AbsentPathProbe;

        public string GetHashName()
        {
            if (ObservedInput.Hash.Equals(WellKnownContentHashes.UntrackedFile))
            {
                return "UntrackedFile";
            }

            if (ObservedInput.Hash.Equals(WellKnownContentHashes.AbsentFile))
            {
                return "AbsentFile";
            }

            return ObservedInput.Hash.ToHex();
        }

        public override List<string> ToList(int totalPips)
        {
            var referencePercentage = totalPips > 0 ? (int)((Count * 100) / totalPips) : 0;
            var summary = new List<string>()
                          {
                              Name,
                              ObservedInput.Type.ToString(),
                              Count.ToString(CultureInfo.InvariantCulture),
                              referencePercentage.ToString(CultureInfo.InvariantCulture),
                              GetHashName(),
                          };
            return summary;
        }
    }

    internal class DependencySummary<T>
    {
        private int m_unCachedRefCount;
        private int m_cachedRefCount;

        /// <summary>
        /// Nane=Name of variable combination
        /// </summary>
        public readonly T Name;

        /// <summary>
        /// Total number of references.
        /// </summary>
        public int Count => m_unCachedRefCount + m_cachedRefCount;

        /// <summary>
        /// Number of cached references.
        /// </summary>
        public int CachedCount
        {
            get { return m_cachedRefCount; }
            set { m_cachedRefCount = value; }
        }

        /// <summary>
        /// Number of un-cached references.
        /// </summary>
        public int UnCachedCount
        {
            get { return m_unCachedRefCount; }
            set { m_unCachedRefCount = value; }
        }

        public DependencySummary(T nameName)
        {
            Name = nameName;
            m_cachedRefCount = 0;
            m_unCachedRefCount = 0;
        }

        public DependencySummary(T nameName, bool isCached)
        {
            Name = nameName;
            m_cachedRefCount = isCached ? 1 : 0;
            m_unCachedRefCount = isCached ? 0 : 1;
        }

        public int UnCachedPipArtifactReferenced()
        {
            return Interlocked.Increment(ref m_unCachedRefCount);
        }

        public int CachedPipArtifactReferenced()
        {
            return Interlocked.Increment(ref m_cachedRefCount);
        }

        public virtual List<string> ToList(int count)
        {
            var value = new List<string>()
                        {
                            Name.ToString(),
                        };
            return value;
        }
    }

    internal struct LogDependencySummary
    {
        public ConcurrentDictionary<string, DependencySummary<string>> DirectorySummary;
        public ConcurrentDictionary<string, List<string>> DirectoryMembership;
        public ConcurrentDictionary<string, DependencySummary<string>> EnvironmentSummary;
        public ConcurrentDictionary<string, FileArtifactSummary> FileArtifactSummary;
        public ConcurrentDictionary<string, ObservedInputSummary> ObservedSummary;
    }

    internal struct ProcessPipsExectuionTypesCounts
    {
        public int Executed;
        public int Cached;
        public int UpToDate;
        public int Failed;
    }

    /// <summary>
    /// Analyzer used to compare two execution logs and report on high impact differences that produce cache invalidation.
    /// The normal usage is to compare Build-i to Build-(i-1)
    /// </summary>
    internal class SummaryAnalyzer : Analyzer
    {
        private readonly int m_maxDifferenceReportCount;
        private readonly ConcurrentBigMap<FileArtifact, FileArtifactSummary> m_fileContentMap = new ConcurrentBigMap<FileArtifact, FileArtifactSummary>();
        private readonly HashSet<PipId> m_completedPips;
        private readonly Dictionary<PipId, ReadOnlyArray<int>> m_observedInputs = new Dictionary<PipId, ReadOnlyArray<int>>();
        private readonly ConcurrentDenseIndex<PipPerformance> m_pipPerformance = new ConcurrentDenseIndex<PipPerformance>(false);
        private readonly Dictionary<PipId, bool> m_weakFingerprintCacheMiss = new Dictionary<PipId, bool>();

        // TODO: uncomment after workaround fix
        // private readonly Dictionary<long, PipId> m_semiStableHashProcessPips = new Dictionary<long, PipId>();
        private readonly ConcurrentDictionary<string, PipId> m_stableBuildPipIdProcessPips = new ConcurrentDictionary<string, PipId>();
        private readonly ConcurrentDenseIndex<NodeAndCriticalPath> m_impactPaths = new ConcurrentDenseIndex<NodeAndCriticalPath>(false);

        private readonly HashSet<Process> m_processPipsReferencedInCritPath = new HashSet<Process>();
        private readonly CompressedObservedInputs m_observedInputsCache = new CompressedObservedInputs();

        internal ProcessPipsExectuionTypesCounts ProcessPipsExectuionTypesCounts;
        private static readonly int s_maxDegreeOfParallelism = Environment.ProcessorCount;

        public string ComparedFilePath;
        public bool HtmlOutput;
        public bool SingleLog = false;
        internal readonly string ExecutionLogPath;
        internal readonly LogDependencySummary Summary;
        internal List<(ProcessPipSummary pipSummary1 , ProcessPipSummary pipSummary2)> LogCompareDifference = new List<(ProcessPipSummary pipSummary1, ProcessPipSummary pipSummary2)>();
        internal List<(ProcessPipSummary pipSummary1, ProcessPipSummary pipSummary2)> PipSummaryTrackedProcessPips =
            new List<(ProcessPipSummary pipSummary1, ProcessPipSummary pipSummary2)>();

        private readonly FilterOptions m_filterOptions;
        private PipContentFingerprinter m_contentFingerprinter;
        private long m_uncacheablePipCount;

        private ExtraFingerprintSalts? m_fingerprintSalts;

        internal bool WriteTransitiveDownPips => m_filterOptions.TransitiveDownPips;

        internal int MaxDifferenceReportCount => m_maxDifferenceReportCount;

        public SummaryAnalyzer(AnalysisInput input, int maxDifferenceReportCount, FilterOptions filterOptions)
            : base(input)
        {
            ExecutionLogPath = input.ExecutionLogPath;
            m_completedPips = new HashSet<PipId>();
            m_maxDifferenceReportCount = maxDifferenceReportCount;
            m_filterOptions = filterOptions;
            m_contentFingerprinter = null;
            Summary = new LogDependencySummary()
                      {
                          DirectorySummary = new ConcurrentDictionary<string, DependencySummary<string>>(),
                          DirectoryMembership = new ConcurrentDictionary<string, List<string>>(),
                          EnvironmentSummary = new ConcurrentDictionary<string, DependencySummary<string>>(),
                          FileArtifactSummary = new ConcurrentDictionary<string, FileArtifactSummary>(),
                          ObservedSummary = new ConcurrentDictionary<string, ObservedInputSummary>(),
                      };
        }

        private FileContentInfo LookupHash(FileArtifact artifact)
        {
            return m_fileContentMap[artifact].FileContentInfo;
        }

        private void UnCachedPipArtifactReferenced(FileArtifact artifact)
        {
            m_fileContentMap[artifact].UnCachedPipArtifactReferenced();
        }

        private void CachedPipArtifactReferenced(FileArtifact artifact)
        {
            m_fileContentMap[artifact].CachedPipArtifactReferenced();
        }

        public override int Analyze()
        {
            return GenerateSummaries();
        }

        /// <summary>
        /// Performs the compare of the two logs and writes the report and summary difference csv.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public int Compare(SummaryAnalyzer analyzer)
        {
            var csvPath = Path.ChangeExtension(ComparedFilePath, ".csv");
            using (var compareFileStream = File.Create(ComparedFilePath, bufferSize: 64 * 1024))
            using (var csvFileStream = File.Create(csvPath, bufferSize: 64 * 1024))
            {
                using (var writer = new StreamWriter(compareFileStream))
                using (var csvWriter = new StreamWriter(csvFileStream))
                {
                    ComputeTransitiveImpact(analyzer);
                    var summaryAnalyzerTextWriter = new SummaryAnalyzerTextWriter(this);

                    if (HtmlOutput)
                    {
#if DISABLE_FEATURE_HTMLWRITER
                        Console.WriteLine("HTMLWriter is not enabled in .NET Core implementation");
                        return 1;
#else

                        using (var htmlWriter = new HtmlTextWriter(writer))
                        {
                            var summaryAnalyzerHtmlWritter = new SummaryAnalyzerHtmlWritter(this);
                            summaryAnalyzerHtmlWritter.PrintHtmlReport(analyzer, htmlWriter);
                        }
#endif
                    }
                    else
                    {
                        summaryAnalyzerTextWriter.PrintTextReport(analyzer, writer);
                    }

                    summaryAnalyzerTextWriter.PrintHygienatorTwoLogsCsvOutput(csvWriter, analyzer);
                }
            }

            return 0;
        }

        /// <summary>
        /// Summarize each pip on this log to get:
        /// Distinct input file artifact dependency : Hash, Path, Pip reference count
        /// Distinct directory Dependencies :  Path, Pip reference count
        /// Distinct Environment variables : Name=value, Pip reference count
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        internal int GenerateSummaries()
        {
            var directorySummary = new ConcurrentDictionary<AbsolutePath, DependencySummary<AbsolutePath>>();

            List<PipReference> orderedPips = CachedGraph.PipGraph
                .RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => m_completedPips.Contains(lazyPip.PipId))
                .ToList();

            var pipsSumarized = orderedPips.AsParallel().AsOrdered()
                .WithDegreeOfParallelism(degreeOfParallelism: s_maxDegreeOfParallelism)
                .Select(p => SummaryFingerprintInfo(p, directorySummary));

            Console.WriteLine("Generating Summary of: {0}", ExecutionLogPath);
            int summaries = 0;
            var t = new Timer(
                o =>
                {
                    var done = summaries;
                    Console.WriteLine("Fingerprints Done: {0} of {1}", done, orderedPips.Count);
                },
                null,
                5000,
                5000);

            foreach (var b in pipsSumarized)
            {
                summaries += b ? 1 : 0;
            }

            using (var e = new AutoResetEvent(false))
            {
                t.Dispose(e);
                e.WaitOne();
            }

            // Observed input dependencies summary
            foreach (var observedInputEntry in m_observedInputs)
            {
                Parallel.ForEach(
                    observedInputEntry.Value,
                    observedInput =>
                    {
                        var observedInputValue = m_observedInputsCache.GetObservedInputById(observedInput);
                        var key = observedInputValue.Path.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
                        Summary.ObservedSummary.AddOrUpdate(
                            key,
                            new ObservedInputSummary(key, observedInputValue, !IsPipExecuted(observedInputEntry.Key)),
                            (k, v) =>
                            {
                                if (IsPipExecuted(observedInputEntry.Key))
                                {
                                    v.UnCachedCount += 1;
                                }
                                else
                                {
                                    v.CachedCount += 1;
                                }
                                return v;
                            });
                    });
            }

            // File artifact input dependencies summary
            Parallel.ForEach(
                m_fileContentMap,
                pair =>
                {
                    var entry = pair.Value;
                    if (entry.ReferencedByProcessPip())
                    {
                        var key = pair.Value.Name;
                        Summary.FileArtifactSummary.AddOrUpdate(
                            key,
                            entry,
                            (k, v) =>
                            {
                                if (entry.FileArtifact.RewriteCount > v.FileArtifact.RewriteCount)
                                {
                                    entry.CachedCount += v.CachedCount;
                                    entry.UnCachedCount += v.UnCachedCount;
                                    v = entry;
                                }
                                return v;
                            });
                    }
                });

            Parallel.ForEach(
                directorySummary,
                pair =>
                {
                    var key = pair.Value.Name.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
                    var entry = new DependencySummary<string>(key)
                    {
                        UnCachedCount = pair.Value.UnCachedCount,
                        CachedCount = pair.Value.CachedCount,
                    };
                    Summary.DirectorySummary.Add(key, entry);
                });

            if (SingleLog)
            {
#if !DISABLE_FEATURE_HTMLWRITER
                GenerateReport();
#endif
            }

            return 0;
        }

        internal string GetPipDescriptionName(Pip pip)
        {
            var pipDescription = string.Empty;
            var separator = string.Empty;
            if (pip.Provenance.OutputValueSymbol.IsValid)
            {
                pipDescription = pip.Provenance.OutputValueSymbol.ToString(CachedGraph.Context.SymbolTable);
                separator = " ";
            }

            if (pip.Provenance.QualifierId.IsValid)
            {
                pipDescription += separator +  CachedGraph.Context.QualifierTable.GetCanonicalDisplayString(pip.Provenance.QualifierId);
                separator = " ";
            }

            if (pip.Provenance.Usage.IsValid)
            {
                pipDescription += separator + pip.Provenance.Usage.ToString(CachedGraph.Context.PathTable);
            }

            return pipDescription;
        }

        /// <summary>
        /// Summarize each pip into the distinct entries dictionaries
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private bool SummaryFingerprintInfo(
            PipReference lazyPip,
            ConcurrentDictionary<AbsolutePath, DependencySummary<AbsolutePath>> directorySummary)
        {
            var pip = (Process)CachedGraph.PipGraph.GetPipFromPipId(lazyPip.PipId);

            var pipHashKey = pip.SemiStableHash + pip.Provenance.Token.Path.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
            m_stableBuildPipIdProcessPips.Add(pipHashKey, lazyPip.PipId);

            // Work around  until SemiStableHash becomes unique
            // m_semiStableHashProcessPips.Add(pip.SemiStableHash, data.PipId);

            // This checks for missing content info for pips.
            if (pip.Dependencies.Any(dependency => !m_fileContentMap.ContainsKey(dependency)))
            {
                return false;
            }

            // File dependencies
            foreach (var dependency in pip.Dependencies)
            {
                if (!IsPipExecuted(pip.PipId))
                {
                    CachedPipArtifactReferenced(dependency);
                }
                else
                {
                    UnCachedPipArtifactReferenced(dependency);
                }
            }

            // Environment variable depenedencies
            foreach (var environmentVariable in pip.EnvironmentVariables)
            {
                // Produce name and value string
                var key = GetEnvironmentNameValue(environmentVariable);
                if (key == null)
                {
                    continue;
                }

                Summary.EnvironmentSummary.AddOrUpdate(
                key,
                new DependencySummary<string>(key, !IsPipExecuted(pip.PipId)),
                (k, v) =>
                {
                    if (IsPipExecuted(pip.PipId))
                    {
                        v.CachedCount += 1;
                    }
                    else
                    {
                        v.UnCachedCount += 1;
                    }
                    return v;
                });
            }

            // Directory depenedencies
            foreach (var directoryDependency in pip.DirectoryDependencies)
            {
                directorySummary.AddOrUpdate(
                    directoryDependency.Path,
                    new DependencySummary<AbsolutePath>(directoryDependency.Path, !IsPipExecuted(pip.PipId)),
                    (k, v) =>
                    {
                        if (IsPipExecuted(pip.PipId))
                        {
                            v.CachedCount += 1;
                        }
                        else
                        {
                            v.UnCachedCount += 1;
                        }
                        return v;
                    });
            }

            return true;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private string GetEnvironmentNameValue(EnvironmentVariable environmentVariable)
        {
            if (environmentVariable.IsPassThrough)
            {
                // Ignore PassThrough variables because no fingerprint impact
                return null;
            }

            string name = environmentVariable.Name.IsValid ? CachedGraph.Context.PathTable.StringTable.GetString(environmentVariable.Name) : "??Invalid";

            // ignore these because they are internally used by BuildXL
            if (!environmentVariable.Value.IsValid ||
                name.Equals("TMP", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TESTOUTPUTDIR", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string environmentValue = string.Empty;
            string separator = string.Empty;
            if (environmentVariable.Value.FragmentCount > 1)
            {
                separator = CachedGraph.Context.PathTable.StringTable.GetString(environmentVariable.Value.FragmentSeparator);
            }

            foreach (var fragment in environmentVariable.Value)
            {
                if (fragment.FragmentType == PipFragmentType.AbsolutePath)
                {
                    // Normalize paths to lowercase for comparing
                    AbsolutePath fileFragmentPath = fragment.GetPathValue();
                    environmentValue += fileFragmentPath.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
                }
                else if (fragment.FragmentType == PipFragmentType.StringLiteral)
                {
                    environmentValue += CachedGraph.Context.PathTable.StringTable.GetString(fragment.GetStringIdValue());
                }

                if (!string.IsNullOrEmpty(separator))
                {
                    environmentValue += separator;
                }
            }

            string environmentNameValue = name + "=" + environmentValue;

            return environmentNameValue;
        }

        #region Direct impact summary compare

        internal (List<List<string>> fileArtifactChanges, List<List<string>> fileArtifactMissing) GenerateFileArtifactDifference(
            ConcurrentDictionary<string, FileArtifactSummary> fileDependencySummary,
            ConcurrentDictionary<string, FileArtifactSummary> otherFileDependencySummary)
        {
            var fileArtifactChanges = new List<List<string>>();
            var fileArtifactMissing = new List<List<string>>();

            var diff1 = GetFileDependencyDiff(fileDependencySummary, otherFileDependencySummary).ToList();
            var diff2 = GetFileDependencyDiff(otherFileDependencySummary, fileDependencySummary).ToList();
            foreach (var entry in diff1)
            {
                if (entry.CachedCount > 0)
                {
                    // This was reported cached in other pips so it has not changed
                    continue;
                }

                string otherHash = "missing in XLG";
                FileArtifactSummary otherEntry;
                if (otherFileDependencySummary.TryGetValue(entry.Name, out otherEntry))
                {
                    otherHash = otherEntry.GetHashName();
                }
                else if (entry.IsUntrackedFile)
                {
                    // Ignore untracked file when not present in previous xlg
                    continue;
                }

                var matchedSummary = entry.ToList(m_completedPips.Count);
                matchedSummary.Add(otherHash);
                fileArtifactChanges.Add(matchedSummary);
            }

            foreach (var entry in diff2)
            {
                // Dependencies in previous file not found in current file
                FileArtifactSummary dependency;
                if (!fileDependencySummary.TryGetValue(entry.Name, out dependency))
                {
                    if (entry.IsUntrackedFile)
                    {
                        // Ignore untracked file when not present in current xlg
                        continue;
                    }

                    fileArtifactMissing.Add(entry.ToList(m_completedPips.Count));
                }
            }

            return (fileArtifactChanges, fileArtifactMissing);
        }

        internal static (List<List<string>> enviromentChanges, List<List<string>> enviromentMissing) GenerateEnvironmentDifference(
            ConcurrentDictionary<string, DependencySummary<string>> environmentSummary,
            ConcurrentDictionary<string, DependencySummary<string>> otherEnvironmentSummary)
        {
            var environmentChanges = new List<List<string>>();
            var environmentMissing = new List<List<string>>();
            var environmentVariablesNotInTwo = GetDependencyDiff(environmentSummary, otherEnvironmentSummary);
            var environmentVariablesNotInOne = GetDependencyDiff(otherEnvironmentSummary, environmentSummary);

            var environmentVariablesOne = EnvironmentVariablesSummaryToStringDictionary(environmentVariablesNotInTwo);

            var environmentVariablesTwo = EnvironmentVariablesSummaryToStringDictionary(environmentVariablesNotInOne);

            foreach (var environment in environmentVariablesOne)
            {
                IReadOnlyList<string> otherVariableValue;
                if (environmentVariablesTwo.TryGetValue(environment.Key, out otherVariableValue))
                {
                    // Environment variable exists in both environments
                    foreach (var value in environment.Value)
                    {
                        environmentChanges.AddRange(
                            from otherValue in otherVariableValue
                            where !value.Equals(otherValue, StringComparison.Ordinal)
                            let count =
                                Math.Max(
                                    environmentSummary[string.Format(CultureInfo.InvariantCulture, "{0}={1}", environment.Key, value)].Count,
                                    otherEnvironmentSummary[string.Format(CultureInfo.InvariantCulture, "{0}={1}", environment.Key, otherValue)].Count)
                            select new List<string>() { environment.Key, count.ToString(CultureInfo.InvariantCulture), value, otherValue });
                    }
                }
                else
                {
                    environmentChanges.AddRange(
                        from value in environment.Value
                        let count = environmentSummary[string.Format(CultureInfo.InvariantCulture, "{0}={1}", environment.Key, value)].Count
                        select new List<string>() { environment.Key, count.ToString(CultureInfo.InvariantCulture), value, "missing" });
                }
            }

            foreach (var environment in environmentVariablesTwo.Where(environment => !environmentVariablesOne.ContainsKey(environment.Key)))
            {
                environmentMissing.AddRange(
                    from value in environment.Value
                    let count = otherEnvironmentSummary[environment.Key + "=" + value].Count
                    select new List<string>() { environment.Key, count.ToString(CultureInfo.InvariantCulture), "missing", value });
            }

            return (environmentChanges, environmentMissing);
        }

        internal static MultiValueDictionary<string, string> EnvironmentVariablesSummaryToStringDictionary(IEnumerable<DependencySummary<string>> environmentVariablesSummary)
        {
            var environmentVariablesOne = new MultiValueDictionary<string, string>();
            foreach (var environment in environmentVariablesSummary)
            {
                var key = environment.Name;
                var index = environment.Name.IndexOf("=", StringComparison.OrdinalIgnoreCase);
                var value = string.Empty;
                if (index > 0)
                {
                    key = environment.Name.Substring(0, index);
                    value = environment.Name.Substring(index + 1);
                }

                environmentVariablesOne.Add(key, value);
            }

            return environmentVariablesOne;
        }

        public (List<List<string>> fileArtifactChanges, List<List<string>> fileArtifactMissing) GenerateObservedDifference(
            ConcurrentDictionary<string, ObservedInputSummary> observedDependencySummary,
            ConcurrentDictionary<string, ObservedInputSummary> otherObservedDependencySummary)
        {
            var observedChanges = new List<List<string>>();
            var observedMissing = new List<List<string>>();
            var diff1 = GetFileDependencyDiff(observedDependencySummary, otherObservedDependencySummary).ToList();
            var diff2 = GetFileDependencyDiff(otherObservedDependencySummary, observedDependencySummary).ToList();

            foreach (var entry in diff1)
            {
                if (entry.CachedCount > 0)
                {
                    // This was reported cached in other pips so it has not changed
                    continue;
                }

                string otherHash = "missing in XLG";
                ObservedInputSummary otherEntry;
                if (otherObservedDependencySummary.TryGetValue(entry.Name, out otherEntry))
                {
                    otherHash = otherEntry.GetHashName();
                }

                // Ignore untracked file when not present in previous xlg
                if ((entry.IsUntrackedFile && otherEntry == null) || (m_filterOptions.IgnoreAbsentPathProbe && entry.AbsentPathProbe))
                {
                    continue;
                }

                var matchedSummary = entry.ToList(m_completedPips.Count);
                matchedSummary.Add(otherHash);
                observedChanges.Add(matchedSummary);
            }

            // Add any observed inputs not found current log but found in previous log (removed).
            foreach (var entry in diff2)
            {
                ObservedInputSummary observedInput;
                if (!observedDependencySummary.TryGetValue(entry.Name, out observedInput))
                {
                    // Ignore untracked file when not present in current xlg
                    if (entry.IsUntrackedFile || (m_filterOptions.IgnoreAbsentPathProbe && entry.IsAbsentFile))
                    {
                        continue;
                    }

                    var matchedSummary = entry.ToList(m_completedPips.Count);
                    observedMissing.Add(matchedSummary);
                }
            }

            return (observedChanges, observedMissing);
        }

        internal static IEnumerable<DependencySummary<T>> GetDependencyDiff<T>(ConcurrentDictionary<T, DependencySummary<T>> summary, ConcurrentDictionary<T, DependencySummary<T>> otherSummary)
        {
            return summary
                .Where(e => !otherSummary.ContainsKey(e.Key) || !e.Value.Name.Equals(otherSummary[e.Key].Name))
                .OrderByDescending(e => e.Value.Count)
                .Select(e => e.Value);
        }

        internal static IEnumerable<FileArtifactSummary> GetFileDependencyDiff(ConcurrentDictionary<string, FileArtifactSummary> fileDependencySummary, ConcurrentDictionary<string, FileArtifactSummary> otherFileDependencySummary)
        {
            return fileDependencySummary
                .Where(e => !otherFileDependencySummary.ContainsKey(e.Key) || !e.Value.FileContentInfo.Hash.Equals(otherFileDependencySummary[e.Key].FileContentInfo.Hash))
                .OrderByDescending(e => e.Value.Count)
                .ThenBy(e => e.Value.Name)
                .Select(e => e.Value);
        }

        internal static IEnumerable<ObservedInputSummary> GetFileDependencyDiff(ConcurrentDictionary<string, ObservedInputSummary> fileDependencySummary, ConcurrentDictionary<string, ObservedInputSummary> otherFileDependencySummary)
        {
            return fileDependencySummary
                .Where(e => !otherFileDependencySummary.ContainsKey(e.Key) || !e.Value.ObservedInput.Hash.Equals(otherFileDependencySummary[e.Key].ObservedInput.Hash))
                .OrderByDescending(e => e.Value.Count)
                .ThenBy(e => e.Value.Name)
                .Select(e => e.Value);
        }

        internal static Dictionary<string, List<string>> GetDirectoryMembershipDiff(ConcurrentDictionary<string, List<string>> directoryMembership, ConcurrentDictionary<string, List<string>> otherDirectoryMembership)
        {
            Dictionary<string, List<string>> membershipDiff = new Dictionary<string, List<string>>();
            foreach (var directory in directoryMembership)
            {
                List<string> otherMembers;
                List<string> diffMembers = new List<string>();
                if (otherDirectoryMembership.TryGetValue(directory.Key, out otherMembers))
                {
                    diffMembers.AddRange(directory.Value.Where(member => !otherMembers.Contains(member)));
                    if (diffMembers.Count > 0)
                    {
                        membershipDiff.Add(directory.Key, diffMembers);
                    }
                }
                else
                {
                    membershipDiff.Add(directory.Key, new List<string>() { "Missing" });
                }
            }

            return membershipDiff;
        }

        #endregion

        #region Transitive impact processing

        private struct PipPerformance
        {
            public PipExecutionPerformance ExecutionPerformance;
            public TimeSpan ElapsedTime;

            public TimeSpan KernelTime
            {
                get
                {
                    var processPipExecutionPerformance = ProcessPipExecutionPerformance;
                    return processPipExecutionPerformance?.KernelTime ?? TimeSpan.Zero;
                }
            }

            public TimeSpan UserTime
            {
                get
                {
                    var processPipExecutionPerformance = ProcessPipExecutionPerformance;
                    return processPipExecutionPerformance?.UserTime ?? TimeSpan.Zero;
                }
            }

            public bool UncacheablePip
            {
                get
                {
                    var processPipExecutionPerformance = ProcessPipExecutionPerformance;
                    return processPipExecutionPerformance?.FileMonitoringViolations.HasUncacheableFileAccesses ?? false;
                }
            }

            private ProcessPipExecutionPerformance ProcessPipExecutionPerformance
            {
                get
                {
                    var processPipExecutionPerformance = ExecutionPerformance as ProcessPipExecutionPerformance;
                    return processPipExecutionPerformance;
                }
            }
        }

        internal struct ProcessPipSummary
        {
            public Process Pip;
            public ContentFingerprint Fingerprint;
            public ConcurrentDictionary<string, FileArtifactSummary> DependencySummary;
            public ConcurrentDictionary<string, ObservedInputSummary> ObservedInputSummary;
            public ConcurrentDictionary<string, DependencySummary<string>> EnvironmentSummary;
            public bool NewPip;
            public bool UncacheablePip;
            public HashSet<NodeId> DependentNodes;
            public NodeAndCriticalPath CriticalPath;
            public int ExecutedDependentProcessCount;
        }

        /// <summary>
        /// Compares two graphs to report on reasons of executed process and the transitive down impact.
        /// Walks down the graph orderd by pip Id and start time. For executed Pips finds the matching Pip in the other
        /// log by senistable hash, finds reason for miss, computes transitive down pips that were executed.
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        internal int ComputeTransitiveImpact(SummaryAnalyzer otherAnalyzer)
        {
            List<PipReference> orderedPips = CachedGraph.PipGraph
                .RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => m_completedPips.Contains(lazyPip.PipId))
                .ToList();
            orderedPips.Sort((p1, p2) => m_pipPerformance[p1.PipId.Value].ExecutionPerformance.ExecutionStart.CompareTo(m_pipPerformance[p2.PipId.Value].ExecutionPerformance.ExecutionStart));

            var vistedNodes = new HashSet<NodeId>();
            var topLevelProcessCount = 0;
            foreach (var pipReference in orderedPips)
            {
                var node = new NodeId(pipReference.PipId.Value);
                if (vistedNodes.Contains(node))
                {
                    continue;
                }

                if (!(IsPipExecuted(pipReference.PipId) || IsPipFailed(pipReference.PipId)))
                {
                    continue;
                }

                // Process pip was executed try finding fingerprint on the other execution log to evaluate reason for miss
                var pip = (Process)pipReference.HydratePip();
                var pipSummary = GenerateProcessPipSummary(pip);

                // When calculating the transitive impact, a list of executed process pips is kept. This list
                // can grow large when there is 100% cache miss resulting in a very large html document.
                // So only track crit-path executed pips for the first three longest chains for performance.
                pipSummary = ComputeNodeChangeTransitiveImpact(node, pipSummary, topLevelProcessCount < 3);
                var otherPipSummary = default(ProcessPipSummary);

                // TODO: fix this Workaround when SemistableHash becomes unique
                var stablePipKey = pipReference.SemiStableHash + pip.Provenance.Token.Path.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
                if (LogCompareDifference.Count <= m_maxDifferenceReportCount)
                {
                    Process otherPip;
                    if (otherAnalyzer.TryGetPipBySemiStableHash(stablePipKey, out otherPip))
                    {
                        otherPipSummary = otherAnalyzer.GenerateProcessPipSummary(otherPip);
                    }
                    else
                    {
                        // No matching pip on other log, only present in this log
                        pipSummary.NewPip = true;
                    }
                }

                foreach (var nodeId in pipSummary.DependentNodes)
                {
                    vistedNodes.Add(nodeId);
                }

                // Add the current node to list of visited.
                vistedNodes.Add(node);
                LogCompareDifference.Add((pipSummary, otherPipSummary));
                topLevelProcessCount++;
            }

            // Generate pip summary from tracked process pips
            GenerateSummaryPipsReferencedInCriticalPath(otherAnalyzer);
            return 0;
        }

        #region Hygienator // Methods deal with analysis of single log
#if !DISABLE_FEATURE_HTMLWRITER
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public int GenerateReport()
        {
            // TODO: Gemerage CSV summary
            using (var compareFileStream = File.Create(ComparedFilePath, bufferSize: 64 * 1024 /* 64 KB */))
            {
                using (var writer = new StreamWriter(compareFileStream))
                {
                    using (var htmlWriter = new HtmlTextWriter(writer))
                    {
                        var summaryAnalyzerHtmlWritter = new SummaryAnalyzerHtmlWritter(this);
                        summaryAnalyzerHtmlWritter.PrintHtmlReport(htmlWriter);
                    }
                }
            }

            return 0;
        }
#endif

        internal List<Process> GetPipsExecutionLevel(PipExecutionLevel executionLevel)
        {
            var orderedPips = CachedGraph.PipGraph
                .RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => m_completedPips.Contains(lazyPip.PipId))
                .ToList();
            orderedPips.Sort((p1, p2) => m_pipPerformance[p1.PipId.Value].ExecutionPerformance.ExecutionStart.CompareTo(m_pipPerformance[p2.PipId.Value].ExecutionPerformance.ExecutionStart));

            return (from pipReference in orderedPips
                where m_pipPerformance[pipReference.PipId.Value].ExecutionPerformance.ExecutionLevel == executionLevel
                select (Process)pipReference.HydratePip()).ToList();
        }

        /// <summary>
        /// Generates the summary of the top graph root executed pips
        /// </summary>
        internal List<ProcessPipSummary> GetExecutedProcessPipSummary()
        {
            var executedPips = new List<ProcessPipSummary>();
            List<PipReference> orderedPips = CachedGraph.PipGraph
                .RetrievePipReferencesOfType(PipType.Process)
                .Where(lazyPip => m_completedPips.Contains(lazyPip.PipId))
                .ToList();
            orderedPips.Sort((p1, p2) => m_pipPerformance[p1.PipId.Value].ExecutionPerformance.ExecutionStart.CompareTo(m_pipPerformance[p2.PipId.Value].ExecutionPerformance.ExecutionStart));

            var vistedNodes = new HashSet<NodeId>();
            var topLevelProcessCount = 0;
            foreach (var pipReference in orderedPips)
            {
                var node = new NodeId(pipReference.PipId.Value);
                if (vistedNodes.Contains(node))
                {
                    continue;
                }

                if (!IsPipExecuted(pipReference.PipId))
                {
                    continue;
                }

                // Process pip was executed
                var pip = (Process)pipReference.HydratePip();
                var pipSummary = GenerateProcessPipSummary(pip);

                // When calculating the transitive impact, a list of executed process pips is kept. This list
                // can grow large when there is 100% cache miss resulting in a very large html document.
                // So only track crit-path executed pips for the first three longest chains for performance.
                pipSummary = ComputeNodeChangeTransitiveImpact(node, pipSummary, topLevelProcessCount < 3);
                foreach (var nodeId in pipSummary.DependentNodes)
                {
                    vistedNodes.Add(nodeId);
                }

                // Add the current node to list of visited.
                vistedNodes.Add(node);
                executedPips.Add(pipSummary);
                if (topLevelProcessCount > m_maxDifferenceReportCount)
                {
                    break;
                }

                topLevelProcessCount++;
            }

            return executedPips;
        }

        internal List<ProcessPipSummary> GetSummaryPipsReferencedInCriticalPath()
        {
            var critPathPips = new List<ProcessPipSummary>();
            foreach (var process in m_processPipsReferencedInCritPath)
            {
                var pipSummary = GenerateProcessPipSummary(process);
                critPathPips.Add(pipSummary);
            }

            return critPathPips;
        }

        /// <summary>
        /// Produces a set of files which are not present in the set of files found in cached pips,
        /// the caller sends a dependencies in root pip.
        /// </summary>
        internal static List<FileArtifactSummary> GenerateFileArtifactSuspects(ConcurrentDictionary<string, FileArtifactSummary> dependencySummary)
        {
            return (from dependency in dependencySummary where dependency.Value.CachedCount == 0 select dependency.Value).ToList();
        }

        /// <summary>
        /// Produces a set of environment which are not present in the set of environment found in cached pips,
        /// the caller sends a dependencies in root pip.
        /// </summary>
        internal static List<DependencySummary<string>> GenerateEnvironmentSuspects(ConcurrentDictionary<string, DependencySummary<string>> environmentSummary)
        {
            return (from dependency in environmentSummary where dependency.Value.CachedCount == 0 select dependency.Value).ToList();
        }

        /// <summary>
        /// Produces a set of observedInput which are not present in the set of observedInput found in cached pips,
        /// the caller sends a dependencies in root pip.
        /// </summary>
        internal static List<ObservedInputSummary> GenerateObservedSuspects(ConcurrentDictionary<string, ObservedInputSummary> observedInputSummary)
        {
            return (from dependency in observedInputSummary where dependency.Value.CachedCount == 0 select dependency.Value).ToList();
        }

        #endregion Hygienator // Single log

        internal IOrderedEnumerable<(ProcessPipSummary pipSummary1, ProcessPipSummary pipSummary2)> GetDifferecesToReport()
        {
            var summariesToReport = LogCompareDifference.Take(m_maxDifferenceReportCount).OrderByDescending(a => a.pipSummary1.CriticalPath.Time);
            return summariesToReport;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private void GenerateSummaryPipsReferencedInCriticalPath(SummaryAnalyzer otherAnalyzer)
        {
            foreach (var process in m_processPipsReferencedInCritPath)
            {
                var pipSummary = GenerateProcessPipSummary(process);
                var stablePipKey = process.SemiStableHash + process.Provenance.Token.Path.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
                var otherPipSummary = default(ProcessPipSummary);
                Process otherPip;
                if (otherAnalyzer.TryGetPipBySemiStableHash(stablePipKey, out otherPip))
                {
                    otherPipSummary = otherAnalyzer.GenerateProcessPipSummary(otherPip);
                }
                else
                {
                    pipSummary.NewPip = true;
                }

                PipSummaryTrackedProcessPips.Add((pipSummary, otherPipSummary));
            }
        }

        private ProcessPipSummary GenerateProcessPipSummary(Process pip)
        {
            var pipSummary = new ProcessPipSummary()
            {
                Pip = pip,
                Fingerprint = ComputeWeakFingerprint(pip), // m_pipPerformance[pip.PipId.Value].ProcessPipFingerprint,
                DependencySummary = GetPipDependencies(pip),
                EnvironmentSummary = GetEnvironmentSet(pip.EnvironmentVariables),
                ObservedInputSummary = GetObservedInputs(pip),
                UncacheablePip = !m_filterOptions.IgnoreProcessEvents && m_pipPerformance[pip.PipId.Value].UncacheablePip,
            };

            return pipSummary;
        }

        private ProcessPipSummary ComputeNodeChangeTransitiveImpact(NodeId node, ProcessPipSummary pipSummary, bool trackExecutedPips)
        {
            // Compute transitive impact for this executed Pip
            // How many pips were impacted, longest execution path
            var dependentNodes = new HashSet<NodeId>();
            var criticalPath = ComputeCriticalPath(node);
            if (trackExecutedPips)
            {
                TrackExecutedPipsCriticalPath(criticalPath);
            }

            pipSummary.CriticalPath = criticalPath;

            int executedDependentProcess = 0;
            if (LogCompareDifference.Count <= m_maxDifferenceReportCount)
            {
                // Calculate only for those that will reported
                // how many are process
                GetDependentNodes(node, dependentNodes);
                dependentNodes.Remove(node);

                executedDependentProcess = (from nodeId in dependentNodes
                    let dependentPipId = nodeId.ToPipId()
                    where
                        m_completedPips.Contains(dependentPipId) &&
                        IsPipExecuted(dependentPipId)
                    select nodeId).Count();
            }

            pipSummary.DependentNodes = dependentNodes;
            pipSummary.ExecutedDependentProcessCount = executedDependentProcess;
            return pipSummary;
        }

        private void TrackExecutedPipsCriticalPath(NodeAndCriticalPath nodeAndCriticalPath)
        {
            if (!nodeAndCriticalPath.Next.IsValid)
            {
                return;
            }

            // Skip the starting pip since is already tracked for rendering in html
            nodeAndCriticalPath = GetImpactPath(nodeAndCriticalPath.Next);

            while (true)
            {
                var pip = CachedGraph.PipGraph.GetPipFromPipId(new PipId(nodeAndCriticalPath.Node.Value));
                var process = pip as Process;

                // Keep only executed process pips
                if (process != null && m_pipPerformance[process.PipId.Value].ExecutionPerformance.ExecutionLevel == PipExecutionLevel.Executed)
                {
                    m_processPipsReferencedInCritPath.Add(process);
                }

                if (!nodeAndCriticalPath.Next.IsValid)
                {
                    break;
                }

                nodeAndCriticalPath = GetImpactPath(nodeAndCriticalPath.Next);
            }
        }

        private ConcurrentDictionary<string, DependencySummary<string>> GetEnvironmentSet(ReadOnlyArray<EnvironmentVariable> environmentVariables)
        {
            var environmentSet = new ConcurrentDictionary<string, DependencySummary<string>>();
            foreach (var value in environmentVariables.Select(GetEnvironmentNameValue).Where(value => value != null))
            {
                environmentSet.Add(value, Summary.EnvironmentSummary[value]);
            }

            return environmentSet;
        }

        internal static string GetFingerprintMismatch(ProcessPipSummary pipSummary, ProcessPipSummary otherPipSummary)
        {
            if (pipSummary.NewPip)
            {
                return string.Format(CultureInfo.InvariantCulture, "   Fingerprint: {0} : New Pip {1}",
                    pipSummary.Fingerprint,
                    pipSummary.UncacheablePip ? ":: Uncacheable Pip" : string.Empty);
            }

            var otherFingerprint = pipSummary.Fingerprint.Equals(otherPipSummary.Fingerprint)
                ? ": Matched"
                : string.Format(CultureInfo.InvariantCulture, "!= {0}", otherPipSummary.Fingerprint);

            return string.Format(CultureInfo.InvariantCulture, "   Fingerprint: {0} {1} {2}", pipSummary.Fingerprint,
                otherFingerprint,
                pipSummary.UncacheablePip ? ":: Uncacheable Pip" : string.Empty);
        }

        private void GetDependentNodes(NodeId node, HashSet<NodeId> visitedNodes)
        {
            visitedNodes.Add(node);
            foreach (var dependency in CachedGraph.DataflowGraph.GetOutgoingEdges(node))
            {
                if (visitedNodes.Contains(dependency.OtherNode))
                {
                    continue;
                }

                GetDependentNodes(dependency.OtherNode, visitedNodes);
            }
        }

        private NodeAndCriticalPath ComputeCriticalPath(NodeId node)
        {
            var impactPath = GetImpactPath(node);

            if (impactPath.Node.IsValid)
            {
                return impactPath;
            }

            NodeAndCriticalPath maxDependencyCriticalPath = default(NodeAndCriticalPath);
            foreach (var dependency in CachedGraph.DataflowGraph.GetOutgoingEdges(node))
            {
                var dependencyCriticalPath = ComputeCriticalPath(dependency.OtherNode);
                if (dependencyCriticalPath.Time > maxDependencyCriticalPath.Time)
                {
                    maxDependencyCriticalPath = dependencyCriticalPath;
                }
            }

            impactPath = new NodeAndCriticalPath()
            {
                Next = maxDependencyCriticalPath.Node,
                Node = node,
                Time = maxDependencyCriticalPath.Time + GetElapsed(node),
                KernelTime = maxDependencyCriticalPath.KernelTime + GetKernelTime(node),
                UserTime = maxDependencyCriticalPath.UserTime + GetUserTime(node),
            };

            SetImpactPath(node, impactPath);

            return impactPath;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private ConcurrentDictionary<string, FileArtifactSummary> GetPipDependencies(Process pip)
        {
            var summary = new ConcurrentDictionary<string, FileArtifactSummary>();

            // This checks for missing content info for pips.
            if (pip.Dependencies.Any(dependency => !m_fileContentMap.ContainsKey(dependency)))
            {
                // TODO: add this to the report
                Console.WriteLine(pip.Provenance.SemiStableHash + " XLG Missing content for this pip");
                return summary;
            }

            Parallel.ForEach(pip.Dependencies, dependency =>
            {
                var entry = m_fileContentMap[dependency];
                summary.AddOrUpdate(entry.Name, entry, (k, v) => v);
            });
            return summary;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private ConcurrentDictionary<string, ObservedInputSummary> GetObservedInputs(Process pip)
        {
            var summary = new ConcurrentDictionary<string, ObservedInputSummary>();
            if (m_observedInputs.ContainsKey(pip.PipId))
            {
                var observedInputs = m_observedInputs[pip.PipId];
                Parallel.ForEach(
                    observedInputs,
                    observedInput =>
                    {
                        var observedInputValue = m_observedInputsCache.GetObservedInputById(observedInput);
                        var key = observedInputValue.Path.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
                        summary.AddOrUpdate(key, Summary.ObservedSummary[key], (k, v) => v);
                    });
            }

            return summary;
        }

        private ContentFingerprint ComputeWeakFingerprint(Process pip)
        {
            // Make sure the extra event data has set the value properly here.
            Contract.Requires(m_fingerprintSalts.HasValue, "m_fingerprintSalts is not set.");

            if (m_contentFingerprinter == null)
            {
                m_contentFingerprinter = new PipContentFingerprinter(
                                            CachedGraph.Context.PathTable,
                                            LookupHash,
                                            m_fingerprintSalts.Value,
                                            pathExpander: CachedGraph.MountPathExpander,
                                            pipDataLookup: CachedGraph.PipGraph.QueryFileArtifactPipData)
                {
                    FingerprintTextEnabled = false,
                };
            }

            return m_contentFingerprinter.ComputeWeakFingerprint(pip);
        }

        // private bool TryGetPipBySemiStableHash(long semiStableHash, out Process pip)
        // {
        //    PipId pipId;
        //    if (m_semiStableHashProcessPips.TryGetValue(semiStableHash, out pipId))
        //    {
        //        pip = (Process)CachedGraph.PipGraph.GetPipFromPipId(pipId);
        //        return true;
        //    }
        //    pip = null;
        //    return false;
        // }
        internal bool IsPipReferencedInCriticalPath(Process process)
        {
            return m_processPipsReferencedInCritPath.Contains(process);
        }

        private bool TryGetPipBySemiStableHash(string stableKey, out Process pip)
        {
            PipId pipId;
            if (m_stableBuildPipIdProcessPips.TryGetValue(stableKey, out pipId))
            {
                pip = (Process)CachedGraph.PipGraph.GetPipFromPipId(pipId);
                return true;
            }

            pip = null;
            return false;
        }

        internal string GetCriticalPathText(NodeAndCriticalPath criticalPath)
        {
            if (!m_filterOptions.NoCriticalPathReport)
            {
                var criticalPathReport = PrintCriticalPath(criticalPath, GetElapsed, GetKernelTime, GetUserTime, GetImpactPath, out _);
                return criticalPathReport;
            }

            return string.Empty;
        }

        internal bool IsPipExecuted(PipId pipId)
        {
            var executionPerf = m_pipPerformance[pipId.Value].ExecutionPerformance;
            return (executionPerf != null) && executionPerf.ExecutionLevel == PipExecutionLevel.Executed;
        }

        internal bool IsPipFailed(PipId pipId)
        {
            return m_pipPerformance[pipId.Value].ExecutionPerformance.ExecutionLevel == PipExecutionLevel.Failed;
        }

        internal PipExecutionLevel GetPipExecutionLevel(PipId pipId)
        {
            return m_pipPerformance[pipId.Value].ExecutionPerformance.ExecutionLevel;
        }

        internal NodeAndCriticalPath GetImpactPath(NodeId node)
        {
            return m_impactPaths[node.Value];
        }

        private void SetImpactPath(NodeId node, NodeAndCriticalPath criticalPath)
        {
            m_impactPaths[node.Value] = criticalPath;
        }

        internal TimeSpan GetElapsed(NodeId node)
        {
            var pip = CachedGraph.PipGraph.GetPipFromPipId(new PipId(node.Value));
            return GetPipElapsedTime(pip);
        }

        private TimeSpan GetKernelTime(NodeId node)
        {
            var pip = CachedGraph.PipGraph.GetPipFromPipId(new PipId(node.Value));
            return GetPipKernelTime(pip);
        }

        private TimeSpan GetUserTime(NodeId node)
        {
            var pip = CachedGraph.PipGraph.GetPipFromPipId(new PipId(node.Value));
            return GetPipUserTime(pip);
        }

        internal TimeSpan GetPipElapsedTime(Pip pip)
        {
            if (pip.PipType == PipType.Process || !m_filterOptions.ProcessPipsPerformance)
            {
                return m_pipPerformance[pip.PipId.Value].ElapsedTime;
            }

            return TimeSpan.Zero;
        }

        internal TimeSpan GetPipKernelTime(Pip pip)
        {
            if (pip.PipType == PipType.Process || !m_filterOptions.ProcessPipsPerformance)
            {
                return m_pipPerformance[pip.PipId.Value].KernelTime;
            }

            return TimeSpan.Zero;
        }

        internal TimeSpan GetPipUserTime(Pip pip)
        {
            if (pip.PipType == PipType.Process || !m_filterOptions.ProcessPipsPerformance)
            {
                return m_pipPerformance[pip.PipId.Value].UserTime;
            }

            return TimeSpan.Zero;
        }

        internal DateTime GetPipStartTime(Pip pip)
        {
            if (pip.PipType == PipType.Process || !m_filterOptions.ProcessPipsPerformance)
            {
                return m_pipPerformance[pip.PipId.Value].ExecutionPerformance.ExecutionStart;
            }

            return DateTime.MinValue;
        }

        internal string GetPipWorkingDirectory(Process pip)
        {
            return pip.WorkingDirectory.ToString(CachedGraph.Context.PathTable);
        }

        internal PathAtom GetPipToolName(Process pip)
        {
            return pip.GetToolName(CachedGraph.Context.PathTable);
        }

        internal string GetPipDescription(Pip pip)
        {
            return pip.GetDescription(CachedGraph.Context);
        }

        internal string GetPipDescription(PipId pipId)
        {
            var pip = CachedGraph.PipGraph.GetPipFromPipId(pipId);
            return GetPipDescription(pip);
        }

        internal string GetPathAtomToString(PathAtom pathAtom)
        {
            return pathAtom.ToString(CachedGraph.Context.StringTable);
        }

        internal string GetAbsolutePathToString(AbsolutePath absolutePath)
        {
            return absolutePath.ToString(CachedGraph.Context.PathTable);
        }

        internal int GetProcessPipCount() => m_completedPips.Count;

        internal long GetExecutedProcessPipCount() => ProcessPipsExectuionTypesCounts.Executed;

        internal long GetFailedProcessPipCount() => ProcessPipsExectuionTypesCounts.Failed;

        internal float GetProcessPipHitRate()
            => (float)(m_completedPips.Count > 0 ? (m_completedPips.Count - ProcessPipsExectuionTypesCounts.Executed) / (float)m_completedPips.Count * 100.0 : 0.0);

        internal long GetUncacheableProcessPipCount() => m_uncacheablePipCount;

        internal int GetProducedFileCount() => m_fileContentMap.Count(e => e.Value.OutputOrigin == PipOutputOrigin.Produced);

        internal int GetCachedFileCount() => m_fileContentMap.Count(e => e.Value.OutputOrigin == PipOutputOrigin.DeployedFromCache);

        internal int GetUpToDateFileCount() => m_fileContentMap.Count(e => e.Value.OutputOrigin == PipOutputOrigin.UpToDate);

        internal Pip GetPipByPipId(PipId pipId) => CachedGraph.PipGraph.GetPipFromPipId(pipId);

        internal bool IsCompletedPip(PipId pipId) => m_completedPips.Contains(pipId);

        internal bool TryGetWeakFingerprintCacheMiss(PipId pipId, out bool miss)
        {
            return m_weakFingerprintCacheMiss.TryGetValue(pipId, out miss);
        }

        public bool CompareSaltsEquals(SummaryAnalyzer analyzer)
        {
            return m_fingerprintSalts.Equals(analyzer.m_fingerprintSalts);
        }

        public List<string> GetSaltsDifference(SummaryAnalyzer analyzer)
        {
            var flagsDifference = new List<string>();
            if (!(m_fingerprintSalts.HasValue && analyzer.m_fingerprintSalts.HasValue))
            {
                var currentValue = m_fingerprintSalts.HasValue ? string.Empty : "Current log: BuildXL flags have no value";
                var previousValue = analyzer.m_fingerprintSalts.HasValue ? string.Empty : " Previous log: BuildXL flags have no value";
                flagsDifference.Add(currentValue + previousValue);
                return flagsDifference;
            }

            var currentSalts = m_fingerprintSalts.Value;
            var previousSalts = analyzer.m_fingerprintSalts.Value;

            if (currentSalts.DisableDetours != previousSalts.DisableDetours)
            {
                flagsDifference.Add("DisableDetours");
            }

            if (currentSalts.ExistingDirectoryProbesAsEnumerations != previousSalts.ExistingDirectoryProbesAsEnumerations)
            {
                flagsDifference.Add("ExistingDirectoryProbesAsEnumerations");
            }

            if (currentSalts.IgnoreGetFinalPathNameByHandle != previousSalts.IgnoreGetFinalPathNameByHandle)
            {
                flagsDifference.Add("IgnoreGetFinalPathNameByHandle");
            }

            if (currentSalts.IgnoreNonCreateFileReparsePoints != previousSalts.IgnoreNonCreateFileReparsePoints)
            {
                flagsDifference.Add("IgnoreNonCreateFileReparsePoints");
            }

            if (currentSalts.IgnoreReparsePoints != previousSalts.IgnoreReparsePoints)
            {
                flagsDifference.Add("IgnoreReparsePoints");
            }

            if (currentSalts.IgnorePreloadedDlls != previousSalts.IgnorePreloadedDlls)
            {
                flagsDifference.Add("IgnorePreloadedDlls");
            }

            if (currentSalts.IgnoreSetFileInformationByHandle != previousSalts.IgnoreSetFileInformationByHandle)
            {
                flagsDifference.Add("IgnoreSetFileInformationByHandle");
            }

            if (currentSalts.IgnoreZwOtherFileInformation != previousSalts.IgnoreZwOtherFileInformation)
            {
                flagsDifference.Add("IgnoreZwOtherFileInformation");
            }

            if (currentSalts.IgnoreZwRenameFileInformation != previousSalts.IgnoreZwRenameFileInformation)
            {
                flagsDifference.Add("IgnoreZwRenameFileInformation");
            }

            if (currentSalts.MonitorNtCreateFile != previousSalts.MonitorNtCreateFile)
            {
                flagsDifference.Add("MonitorNtCreateFile");
            }

            if (currentSalts.MonitorZwCreateOpenQueryFile != previousSalts.MonitorZwCreateOpenQueryFile)
            {
                flagsDifference.Add("MonitorZwCreateOpenQueryFile");
            }

            if (currentSalts.FingerprintVersion != previousSalts.FingerprintVersion)
            {
                flagsDifference.Add("FingerprintVersion");
            }

            if (!currentSalts.FingerprintSalt.Equals(previousSalts.FingerprintSalt))
            {
                flagsDifference.Add("FingerprintSalt :" + currentSalts.FingerprintSalt + " | " + previousSalts.FingerprintSalt);
            }

            return flagsDifference;
        }

        #endregion

    #region Log processing

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            var name = data.FileArtifact.Path.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
            var artifactSummary = new FileArtifactSummary(name, data.FileArtifact, data.FileContentInfo,
                data.OutputOrigin);
            m_fileContentMap[data.FileArtifact] = artifactSummary;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            // Got a performance event for a process so register the process as completed
            if (CachedGraph.PipTable.GetPipType(data.PipId) == PipType.Process)
            {
                m_completedPips.Add(data.PipId);
                if (data.ExecutionPerformance != null)
                {
                    switch (data.ExecutionPerformance.ExecutionLevel)
                    {
                        case PipExecutionLevel.Executed:
                            ProcessPipsExectuionTypesCounts.Executed++;
                            break;
                        case PipExecutionLevel.Cached:
                            ProcessPipsExectuionTypesCounts.Cached++;
                            break;
                        case PipExecutionLevel.UpToDate:
                            ProcessPipsExectuionTypesCounts.UpToDate++;
                            break;
                        case PipExecutionLevel.Failed:
                            ProcessPipsExectuionTypesCounts.Failed++;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("data", "unknown execution level");
                    }

                    var processPipPerformance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
                    if (processPipPerformance != null && processPipPerformance.FileMonitoringViolations.HasUncacheableFileAccesses)
                    {
                        m_uncacheablePipCount++;
                    }
                }
            }

            if (data.ExecutionPerformance == null ||
                (m_filterOptions.ProcessPipsPerformance && CachedGraph.PipTable.GetPipType(data.PipId) != PipType.Process))
            {
                return;
            }

            m_pipPerformance[data.PipId.Value] = new PipPerformance()
                                                 {
                                                     ExecutionPerformance = data.ExecutionPerformance,
                                                     ElapsedTime = data.ExecutionPerformance.ExecutionStop -
                                                                   data.ExecutionPerformance.ExecutionStart,
                                                 };
        }

        /// <inheritdoc />
        public override void BuildSessionConfiguration(BuildSessionConfigurationEventData data)
        {
            m_fingerprintSalts = data.ToFingerprintSalts();
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            var key = data.Directory.ToString(CachedGraph.Context.PathTable).ToLowerInvariant();
            List<string> members;

            if (Summary.DirectoryMembership.TryGetValue(key, out members))
            {
                return;
            }

            members = new List<string>();
            members.AddRange(data.Members.Select(file => file.GetName(CachedGraph.Context.PathTable).ToString(CachedGraph.Context.StringTable).ToLowerInvariant()));
            Summary.DirectoryMembership.Add(key, members);
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            ReadOnlyArray<ObservedInput> observedInputs;
            if (data.Kind == FingerprintComputationKind.CacheCheck)
            {
                // a. Empy(ProcessStrongFingerprintComputationData)StrongFingerprintComputations, meaning there was a weak fingerprint miss
                //   b. All entries in StrongFingerprintComputations have IsHit = false.Meaning that there was a strong fingerprint miss.
                //      Do not keep the observed inputs of any but wait for the ex
                //   c. One of the entries in StrongFingerprintComputations has IsHit = true, meaning that there was a cache-hit,
                //      so use the ObservedInputs for this entry pips when doing comparisons
                //  Track WeakFingerprint cache-hit hit or miss.
                if (data.StrongFingerprintComputations.Count == 0)
                {
                    // set weak fingerprint miss.
                    m_weakFingerprintCacheMiss.Add(data.PipId, true);
                    return;
                }

                // weak fingerprint hit
                m_weakFingerprintCacheMiss.Add(data.PipId, false);

                foreach (var fingerprintComputation in data.StrongFingerprintComputations)
                {
                    if (fingerprintComputation.IsStrongFingerprintHit)
                    {
                        observedInputs = fingerprintComputation.ObservedInputs;
                        var compressedObservedInputs = m_observedInputsCache.GetCompressedObservedInputs(observedInputs);
                        m_observedInputs[data.PipId] = compressedObservedInputs;
                        break;
                    }
                }
            }
            else if (data.Kind == FingerprintComputationKind.Execution)
            {
                // Kind = Execution, means the pip executed and there is expected to have one ProcessStrongFingerprintComputationData,
                //         unless the pip is un-cacheable or failed
                //   a.Take the ObservedInputs for this pips when doing comparisons
                if (data.StrongFingerprintComputations == null || data.StrongFingerprintComputations.Count == 0)
                {
                    // un-cacheable pips do not get OI
                    return;
                }

                observedInputs = data.StrongFingerprintComputations.FirstOrDefault().ObservedInputs;
                var compressedObservedInputs = m_observedInputsCache.GetCompressedObservedInputs(observedInputs);
                m_observedInputs[data.PipId] = compressedObservedInputs;
            }
        }

        /// <inheritdoc />
        public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            // Return false to keep the event analyzer from parsing events it does not care
            switch (eventId)
            {
                case ExecutionEventId.DirectoryMembershipHashed:
                    return !m_filterOptions.WhatBuilt;
                case ExecutionEventId.ProcessFingerprintComputation:
                    return !m_filterOptions.WhatBuilt;
                case ExecutionEventId.BuildSessionConfiguration:
                case ExecutionEventId.FileArtifactContentDecided:
                case ExecutionEventId.PipExecutionPerformance:
                    return true;
                case ExecutionEventId.ObservedInputs:
                case ExecutionEventId.WorkerList:
                case ExecutionEventId.ProcessExecutionMonitoringReported:
                case ExecutionEventId.DependencyViolationReported:
                case ExecutionEventId.PipExecutionStepPerformanceReported:
                case ExecutionEventId.ResourceUsageReported:
                case ExecutionEventId.PipCacheMiss:
                    return false;
                default:
                    return false;
            }
        }
        #endregion
    }
}
