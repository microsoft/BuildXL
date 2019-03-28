// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Execution.Analyzer;

namespace BuildXL.Execution.Analyzer.Analyzers
{
    internal sealed class SummaryAnalyzerTextWriter
    {
        private readonly SummaryAnalyzer m_analyzer;

        public SummaryAnalyzerTextWriter(SummaryAnalyzer analyzer)
        {
            m_analyzer = analyzer;
        }

        public void PrintTextReport(SummaryAnalyzer analyzer, StreamWriter writer)
        {
            WriteSummary(analyzer, writer);
            PrintTransitiveImpact(writer);
            writer.WriteLine(" Summary Section");
            writer.WriteLine("=====================================================================");
            writer.WriteLine("Environment variables Analysis");
            writer.WriteLine("=====================================================================");
            writer.WriteLine(CompareEnvironmentSummary(analyzer.Summary.EnvironmentSummary));
            writer.WriteLine();
            writer.WriteLine("=====================================================================");
            writer.WriteLine("Directory dependency summary analysis");
            writer.WriteLine("=====================================================================");
            writer.WriteLine(CompareDirectoryDependencySummary(analyzer.Summary.DirectorySummary, analyzer.ExecutionLogPath));
            writer.WriteLine();

            writer.WriteLine("=====================================================================");
            writer.WriteLine("File dependency summary analysis");
            writer.WriteLine("=====================================================================");
            var compareHeader = "Summary of file dependencies with mismatch hash" + Environment.NewLine +
                                "defined in process pips between execution log current and previous." +
                                Environment.NewLine +
                                "=====================================================================" +
                                Environment.NewLine +
                                "Top File Dependency Differences: orderd by Pip reference count" +
                                Environment.NewLine +
                                "File Name, source/output, origin, % direct PIP referenced, direct PIP reference Count, File Hash - Current, File Hash - Previous";
            var fileArtifactDiff = CompareFileDependencySummary(
                compareHeader,
                m_analyzer.Summary.FileArtifactSummary,
                analyzer.Summary.FileArtifactSummary);
            if (string.IsNullOrEmpty(fileArtifactDiff))
            {
                fileArtifactDiff = "Top File Dependency Differences: None";
            }

            writer.WriteLine(fileArtifactDiff);
            writer.WriteLine();

            writer.WriteLine("=====================================================================");
            writer.WriteLine("Observed inputs dependency summary analysis");
            writer.WriteLine("=====================================================================");
            compareHeader = "List differences between a summary of observed inputs" + Environment.NewLine +
                            "between execution log current and previous." + Environment.NewLine +
                            "=====================================================================" +
                            Environment.NewLine +
                            "Top Observed File Dependency Differences: orderd by Pip reference count" +
                            Environment.NewLine +
                            "File Name, Observed Input Type, % PIP referenced, PIP dependent Count , File Hash - Current, File Hash - Previous";
            var observedInputDiff = CompareObservedDependencySummary(
                compareHeader,
                m_analyzer.Summary.ObservedSummary,
                analyzer.Summary.ObservedSummary);
            if (string.IsNullOrEmpty(observedInputDiff))
            {
                observedInputDiff = "Top Observed File Dependency Differences: None";
            }

            writer.WriteLine(observedInputDiff);
            writer.WriteLine();
            writer.WriteLine("=====================================================================");
            writer.WriteLine("Directory membership difference");
            writer.WriteLine("=====================================================================");
            writer.WriteLine(CompareDirectoryMembership(analyzer));
            writer.WriteLine();
        }

        private void WriteSummary(SummaryAnalyzer otherAnalyzer, StreamWriter writer)
        {
            writer.WriteLine("Current Execution Log = {0}", m_analyzer.ExecutionLogPath);
            writer.WriteLine("Previous Execution Log: {0}", otherAnalyzer.ExecutionLogPath);
            writer.WriteLine("Number of Process Pips :  {0}", m_analyzer.GetProcessPipCount());
            writer.WriteLine("Number of executed Process Pips :  {0}", m_analyzer.GetExecutedProcessPipCount());

            var fileDependencyDiff = SummaryAnalyzer.GetFileDependencyDiff(m_analyzer.Summary.FileArtifactSummary, otherAnalyzer.Summary.FileArtifactSummary);
            writer.WriteLine("Number of dependency files with mismatch hash : {0}", fileDependencyDiff.Count());

            var observedDiff = SummaryAnalyzer.GetFileDependencyDiff(m_analyzer.Summary.ObservedSummary, otherAnalyzer.Summary.ObservedSummary);
            writer.WriteLine("Number of observed inputs with mismatch hash : {0}", observedDiff.Count());

            if (m_analyzer.Summary.DirectoryMembership.Count == 0 || otherAnalyzer.Summary.DirectoryMembership.Count == 0)
            {
                var fileName = m_analyzer.Summary.DirectoryMembership.Count == 0 ? m_analyzer.ExecutionLogPath : otherAnalyzer.ExecutionLogPath;
                writer.WriteLine("No directory membership event in execution log : {0}", fileName);
            }
            else
            {
                var directoryMembershipNotInTwo = SummaryAnalyzer.GetDirectoryMembershipDiff(m_analyzer.Summary.DirectoryMembership, otherAnalyzer.Summary.DirectoryMembership);
                var directoryMembershipNotInOne = SummaryAnalyzer.GetDirectoryMembershipDiff(otherAnalyzer.Summary.DirectoryMembership, m_analyzer.Summary.DirectoryMembership);
                writer.WriteLine("Number of directory membership differences : {0}", directoryMembershipNotInTwo.Count() + directoryMembershipNotInOne.Count);
            }

            int produced = m_analyzer.GetProducedFileCount();
            var cached = m_analyzer.GetCachedFileCount();
            var upToDate = m_analyzer.GetUpToDateFileCount();
            writer.WriteLine("Current XLG Output Files : {0}/{1}/{2}(Produced/FromCache/UpToDate)", produced, cached, upToDate);

            produced = otherAnalyzer.GetProducedFileCount();
            cached = otherAnalyzer.GetCachedFileCount();
            upToDate = otherAnalyzer.GetUpToDateFileCount();
            writer.WriteLine("Previous XLG Output Files : {0}/{1}/{2}(Produced/FromCache/UpToDate)", produced, cached, upToDate);
            writer.WriteLine();
        }

        private void PrintTransitiveImpact(StreamWriter writer)
        {
            writer.WriteLine("=====================================================================");
            writer.WriteLine("Executed Process Analysis Current Execution Log : {0}", m_analyzer.ExecutionLogPath);
            writer.WriteLine("=====================================================================");
            writer.WriteLine("This section lists executed process pips in longest pole order in File A.");
            writer.WriteLine("Reason for execution by comparing to matching pip in File B.");
            writer.WriteLine("Dependent process pips executed");
            writer.WriteLine("Critical path of transitive down dependent pips.");
            writer.WriteLine("Dependent list of process Pips (optional)");
            writer.WriteLine("Reporting Top level executed pips");
            writer.WriteLine("=====================================================================");

            var summariesToReport = m_analyzer.GetDifferecesToReport();
            var invalidationReason = new HashSet<string>();
            foreach (var pipDiff in summariesToReport.OrderByDescending(a => a.Item1.CriticalPath.Time))
            {
                var pipSummary = pipDiff.pipSummary1;
                var otherPipSummary = pipDiff.pipSummary2;
                var pip = pipSummary.Pip;

                bool weakFingerprintCacheMiss;
                m_analyzer.TryGetWeakFingerprintCacheMiss(pipSummary.Pip.PipId, out weakFingerprintCacheMiss);
                var missReason = pipSummary.UncacheablePip
                    ? "Un-cacheable Pip"
                    : weakFingerprintCacheMiss ? "Weak Fingerprint Cache-Miss" : "Strong Fingerprint Cache-Miss";
                if (!pipSummary.NewPip)
                {
                    if (!pipSummary.Fingerprint.Equals(otherPipSummary.Fingerprint))
                    {
                        missReason = CompareFileDependencySummary(
                            "   File Dependency Changes:",
                            pipSummary.DependencySummary,
                            otherPipSummary.DependencySummary);
                        missReason += GetPipEnvironmentMissReport(
                            pipSummary.EnvironmentSummary,
                            otherPipSummary.EnvironmentSummary);
                    }

                    missReason += CompareObservedDependencySummary(
                        "   Observed File Dependency Changes:",
                        pipSummary.ObservedInputSummary, otherPipSummary.ObservedInputSummary);
                }

                if (invalidationReason.Contains(missReason))
                {
                    // This invalidation is known, so cut down the noise on the report
                    continue;
                }

                writer.WriteLine(m_analyzer.GetPipDescription(pip));
                writer.WriteLine(" Transitive Process invalidated count: {0}", pipSummary.ExecutedDependentProcessCount);
                writer.WriteLine(" Cache-Miss reason analysis:");
                writer.WriteLine(SummaryAnalyzer.GetFingerprintMismatch(pipSummary, otherPipSummary));
                writer.WriteLine(missReason);
                if (!string.IsNullOrEmpty(missReason))
                {
                    invalidationReason.Add(missReason);
                }

                var executionStart = m_analyzer.GetPipStartTime(pip);
                var executionStartText = executionStart.Equals(DateTime.MinValue) ? "-" : executionStart.ToString("h:mm:ss.ff", CultureInfo.InvariantCulture);

                writer.WriteLine(" Start time: {0}", executionStartText);
                writer.WriteLine(
                    " Critical path (seconds): Duration: ( {0} )  Kernel Time: ( {1} )  User Time: ( {2} )",
                    ToSeconds(pipSummary.CriticalPath.Time),
                    ToSeconds(pipSummary.CriticalPath.KernelTime),
                    ToSeconds(pipSummary.CriticalPath.UserTime));

                if (pipSummary.ExecutedDependentProcessCount > 0)
                {
                    var criticalPathReport = m_analyzer.GetCriticalPathText(pipSummary.CriticalPath);
                    writer.WriteLine();
                    writer.WriteLine(criticalPathReport);
                }

                if (m_analyzer.WriteTransitiveDownPips && pipSummary.ExecutedDependentProcessCount > 0)
                {
                    writer.WriteLine(WriteDependentProcess(pipSummary.DependentNodes));
                }

                writer.WriteLine("=====================================================================");
            }
        }

        private string WriteDependentProcess(HashSet<NodeId> dependentNodes)
        {
            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                output.WriteLine("Dependent executed processes");
                var timeWidthLength = dependentNodes.Select(node => ToSeconds(m_analyzer.GetElapsed(node)).Length).Concat(new[] { 0 }).Max();

                foreach (var nodeId in dependentNodes)
                {
                    var pipId = nodeId.ToPipId();
                    if (m_analyzer.IsCompletedPip(pipId) && m_analyzer.IsPipExecuted(pipId))
                    {
                        var elapsed = m_analyzer.GetElapsed(nodeId);
                        output.WriteLine(
                            "({0} sec) [{1}]",
                            ToSeconds(elapsed).PadLeft(timeWidthLength),
                            m_analyzer.GetPipDescription(pipId));
                    }
                }

                return output.ToString();
            }
        }

        private string CompareEnvironmentSummary(ConcurrentDictionary<string, DependencySummary<string>> otherEnvironmentSummary)
        {
            var environmentCompare = SummaryAnalyzer.GenerateEnvironmentDifference(m_analyzer.Summary.EnvironmentSummary, otherEnvironmentSummary);
            if (environmentCompare.enviromentChanges.Count == 0 && environmentCompare.enviromentMissing.Count == 0)
            {
                return "Environments are identical.";
            }

            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                // Now compare
                output.WriteLine("List differences in distinct EnvironmentVariableName = value; pairs");
                output.WriteLine("defined in process pips between execution log current and previous.");
                output.WriteLine("=====================================================================");
                foreach (var values in environmentCompare.enviromentChanges)
                {
                    output.WriteLine("Environment variable ({0} pips): {1} = {2} <> {3}", values[1], values[0], values[2], values[3]);
                }

                if (environmentCompare.Item2.Count > 0)
                {
                    output.WriteLine("Current log missing following environmet variables:");
                    foreach (var values in environmentCompare.Item2)
                    {
                        output.WriteLine("Variable ({0} pips): {1} = {2}", values[1], values[0], values[3]);
                    }
                }

                return output.ToString();
            }
        }

        /// <summary>
        /// Summary of directory membership differences
        /// </summary>
        internal string CompareDirectoryMembership(SummaryAnalyzer otherSummaryAnalyzer)
        {
            var directoryMembership = otherSummaryAnalyzer.Summary.DirectoryMembership;
            var otherLogPath = otherSummaryAnalyzer.ExecutionLogPath;
            if (m_analyzer.Summary.DirectoryMembership.Count == 0 || otherSummaryAnalyzer.Summary.DirectoryMembership.Count == 0)
            {
                var fileName = m_analyzer.Summary.DirectoryMembership.Count == 0 ? m_analyzer.ExecutionLogPath : otherLogPath;
                return string.Format(CultureInfo.InvariantCulture, "No directory membership events in execution log : {0}", fileName);
            }

            // Find the top observed imput directories that have changed and make sure the enumeration is listed
            var observedDiff = SummaryAnalyzer.GetFileDependencyDiff(m_analyzer.Summary.ObservedSummary, otherSummaryAnalyzer.Summary.ObservedSummary).ToList();
            var count = 0;
            var directoriesToEnumerate = new HashSet<string>();
            foreach (var entry in observedDiff)
            {
                if (m_analyzer.Summary.DirectoryMembership.ContainsKey(entry.Name))
                {
                    directoriesToEnumerate.Add(entry.Name);
                }

                if (count++ > m_analyzer.MaxDifferenceReportCount)
                {
                    break;
                }
            }

            var directoryMembershipNotInTwo = SummaryAnalyzer.GetDirectoryMembershipDiff(m_analyzer.Summary.DirectoryMembership, directoryMembership);
            var directoryMembershipNotInOne = SummaryAnalyzer.GetDirectoryMembershipDiff(directoryMembership, m_analyzer.Summary.DirectoryMembership);
            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                var hadOutput = false;
                output.WriteLine("Top Directory Membership Differences:");
                output.WriteLine("Current Execution Log = {0}", m_analyzer.ExecutionLogPath);
                output.WriteLine("Previous Execution Log = {0}", otherLogPath);
                output.WriteLine("Directory  |  Current Execution Log   | Previous Execution Log");

                int outReportCount = 0;

                // Enumerate changes in observed inputs first to show those relevant changes
                foreach (var directory in directoriesToEnumerate)
                {
                    List<string> entry;
                    if (directoryMembershipNotInTwo.TryGetValue(directory, out entry))
                    {
                        hadOutput = PrintDirectoryEnumeration(output, directory, entry);
                        if (outReportCount++ > m_analyzer.MaxDifferenceReportCount)
                        {
                            break;
                        }
                    }
                }

                foreach (var s in directoryMembershipNotInTwo)
                {
                    if (directoriesToEnumerate.Contains(s.Key))
                    {
                        continue;
                    }

                    hadOutput = PrintDirectoryEnumeration(output, s.Key, s.Value);
                    if (outReportCount++ > m_analyzer.MaxDifferenceReportCount)
                    {
                        break;
                    }
                }

                foreach (var s in directoryMembershipNotInOne)
                {
                    hadOutput = PrintDirectoryEnumeration(output, s.Key, s.Value);
                    if (outReportCount++ > m_analyzer.MaxDifferenceReportCount)
                    {
                        break;
                    }
                }

                return hadOutput ? output.ToString() : "Directory Membership Differences: none";
            }
        }

        private bool PrintDirectoryEnumeration(StringWriter output, string directory, List<string> entry)
        {
            bool hadOutput = false;
            var outFileReportCount = 0;
            output.WriteLine("{0} [{1} files]", directory, entry.Count);
            foreach (var member in entry)
            {
                output.WriteLine("           {0,30}  |  yes  | no", member);
                hadOutput = true;

                if (outFileReportCount++ > m_analyzer.MaxDifferenceReportCount)
                {
                    output.WriteLine("..... {0} More", directory);
                    break;
                }
            }

            return hadOutput;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        internal string CompareDirectoryDependencySummary(ConcurrentDictionary<string, DependencySummary<string>> otherDirectorySummary, string otherLogPath)
        {
            var directoriesNotInTwo = SummaryAnalyzer.GetDependencyDiff(m_analyzer.Summary.DirectorySummary, otherDirectorySummary).ToList();
            var directoriesNotInOne = SummaryAnalyzer.GetDependencyDiff(otherDirectorySummary, m_analyzer.Summary.DirectorySummary).ToList();

            // Directory dependencies in some PIPs are distinct.
            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                var hadOutput = false;
                int longest = directoriesNotInOne.Select(s => s.Name.Length).Concat(new[] { 50 }).Max();
                longest = directoriesNotInTwo.Select(s => s.Name.Length).Concat(new[] { longest }).Max();

                // List the differences
                output.WriteLine("List differences between a summary of distinct directory dependencies");
                output.WriteLine("defined in process pips between execution log current and previous.");
                output.WriteLine("=====================================================================");
                string formatString = "{0,-" + longest + "} | {1,19} | {2, 15} | {3, 6} | {4, 6}";
                output.WriteLine("Top Directory Dependency Differences:");
                output.WriteLine("Current Execution Log = {0}", m_analyzer.ExecutionLogPath);
                output.WriteLine("Previous Execution Log = {0}", otherLogPath);
                output.WriteLine(formatString, "Directory Dependency", "Pip Reference count", "% Pip Reference", "Current", "Previous");

                foreach (var s in directoriesNotInTwo)
                {
                    var referencePercentage = m_analyzer.GetProcessPipCount() > 0
                        ? (int)((s.Count * 100) / m_analyzer.GetProcessPipCount())
                        : 0;
                    output.WriteLine(formatString, s.Name.ToLowerInvariant(), s.Count, referencePercentage, "yes", "no");
                    hadOutput = true;
                }

                foreach (var s in directoriesNotInOne)
                {
                    var referencePercentage = m_analyzer.GetProcessPipCount() > 0
                        ? (int)((s.Count * 100) / m_analyzer.GetProcessPipCount())
                        : 0;
                    output.WriteLine(formatString, s.Name.ToLowerInvariant(), s.Count, referencePercentage, "no", "yes");
                    hadOutput = true;
                }

                return hadOutput ? output.ToString() : "Top Directory Dependency Differences: none";
            }
        }

        internal string CompareFileDependencySummary(string header, ConcurrentDictionary<string, FileArtifactSummary> fileDependencySummary, ConcurrentDictionary<string, FileArtifactSummary> otherFileDependencySummary)
        {
            var fileArtifactCompare = m_analyzer.GenerateFileArtifactDifference(fileDependencySummary, otherFileDependencySummary);
            if (fileArtifactCompare.fileArtifactChanges.Count == 0 && fileArtifactCompare.fileArtifactMissing.Count == 0)
            {
                return string.Empty;
            }

            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                output.WriteLine(header);
                WriteArtifactDiffLine(fileArtifactCompare.fileArtifactChanges, output, string.Empty);
                WriteArtifactDiffLine(fileArtifactCompare.fileArtifactMissing, output, "missing, ");
                return output.ToString();
            }
        }

        private void WriteArtifactDiffLine(IEnumerable<List<string>> rows, StringWriter output, string prefix)
        {
            var count = 0;
            foreach (var row in rows)
            {
                output.Write(prefix);
                foreach (var column in row)
                {
                    output.Write("{0}, ", column);
                }

                output.WriteLine();
                if (count++ > m_analyzer.MaxDifferenceReportCount)
                {
                    break;
                }
            }
        }

        internal string CompareObservedDependencySummary(string header, ConcurrentDictionary<string, ObservedInputSummary> observedDependencySummary, ConcurrentDictionary<string, ObservedInputSummary> otherObservedDependencySummary)
        {
            var observedDifference = m_analyzer.GenerateObservedDifference(observedDependencySummary, otherObservedDependencySummary);
            if (observedDifference.fileArtifactChanges.Count == 0 && observedDifference.fileArtifactMissing.Count == 0)
            {
                return string.Empty;
            }

            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                output.WriteLine(header);
                WriteArtifactDiffLine(observedDifference.fileArtifactChanges, output, string.Empty);
                WriteArtifactDiffLine(observedDifference.fileArtifactMissing, output, "missing, ");
                return output.ToString();
            }
        }

        internal static string GetPipEnvironmentMissReport(
    ConcurrentDictionary<string, DependencySummary<string>> environmentSummary,
    ConcurrentDictionary<string, DependencySummary<string>> otherEnvironmentSummary)
        {
            var environmentCompare = SummaryAnalyzer.GenerateEnvironmentDifference(environmentSummary, otherEnvironmentSummary);
            if (environmentCompare.enviromentChanges.Count == 0 && environmentCompare.enviromentMissing.Count == 0)
            {
                return string.Empty;
            }

            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            {
                output.WriteLine("\n   Environment Changes:");
                foreach (var values in environmentCompare.enviromentChanges)
                {
                    output.WriteLine("Environment variable ({0} pips): {1} = {2} <> {3}", values[1], values[0], values[2], values[3]);
                }

                if (environmentCompare.Item2.Count > 0)
                {
                    output.WriteLine("Current log missing following environmet variables:");
                    foreach (var values in environmentCompare.Item2)
                    {
                        output.WriteLine("Variable ({0} pips): {1} = {2}", values[1], values[0], values[3]);
                    }
                }

                return output.ToString();
            }
        }

        #region HygienatorCSVOutput from comparing two files

        public void PrintHygienatorTwoLogsCsvOutput(StreamWriter writer, SummaryAnalyzer analyzer)
        {
            // TODO: allo adding prefix : SourceNamespace,Island,BuildName,Branch,BuildDate,ComputerName,Architecture,
            writer.WriteLine(
                "timeStart,Name,directCount,rootPipTransitiveCount,pipDuration,pipKernelTime,pipUserTime,pipDurationCriticalPath,pipKernelTimeCriticalPath,pipUserTimeCriticalPath,dependencyType,FileHash");
            if (!m_analyzer.CompareSaltsEquals(analyzer))
            {
                // Add the flag changes
                var saltDiffs = m_analyzer.GetSaltsDifference(analyzer);
                foreach (var diff in saltDiffs)
                {
                    writer.WriteLine(
                        "0,{0},{1},{2},0,0,0,0,0,0,GlobalFlagsChange,",
                        diff,
                        m_analyzer.GetProcessPipCount(),
                        m_analyzer.GetProcessPipCount());
                }
            }

            PrintTransitiveDependenciesImpact(writer);
        }

        /// <summary>
        /// Writes the artifact reason for execution when comparing the two pips into the csv file.
        /// The pips comparared are the roots in order of longest critical path and only its difference is written. No transitive down pips are compared.
        /// </summary>
        private void PrintTransitiveDependenciesImpact(StreamWriter writer)
        {
            var summariesToReport = m_analyzer.GetDifferecesToReport();

            // keep track of distinct dependencies
            var fileArtifacts = new HashSet<FileArtifactSummary>();
            var environmentVariables = new HashSet<DependencySummary<string>>();
            var observedInputs = new HashSet<ObservedInputSummary>();

            foreach (var pipDiff in summariesToReport.OrderByDescending(a => a.pipSummary1.CriticalPath.Time))
            {
                var pipSummary = pipDiff.pipSummary1;
                var otherPipSummary = pipDiff.pipSummary2;

                if (pipSummary.NewPip)
                {
                    continue;
                }

                // TODO: add removed dependencies in output
                if (!pipSummary.Fingerprint.Equals(otherPipSummary.Fingerprint))
                {
                    var filesDiff = SummaryAnalyzer.GetFileDependencyDiff(pipSummary.DependencySummary, otherPipSummary.DependencySummary).ToList();
                    foreach (var item in filesDiff)
                    {
                        if (item.CachedCount > 0)
                        {
                            // This was reported cached in other pips so it has not changed
                            continue;
                        }

                        if (item.OutputOrigin == PipOutputOrigin.Produced || fileArtifacts.Contains(item))
                        {
                            // Output file so not interesting or already written
                            continue;
                        }

                        PrintCsvArtifact(writer, pipSummary, item, "Input", item.GetHashName());
                        fileArtifacts.Add(item);
                    }

                    var environmentDiff = SummaryAnalyzer.GetDependencyDiff(pipSummary.EnvironmentSummary, otherPipSummary.EnvironmentSummary).ToList();
                    foreach (var item in environmentDiff)
                    {
                        if (!environmentVariables.Contains(item))
                        {
                            PrintCsvArtifact(writer, pipSummary, item, "EnvironmentVariable", string.Empty);
                            environmentVariables.Add(item);
                        }
                    }
                }

                var observedDiff = SummaryAnalyzer.GetFileDependencyDiff(pipSummary.ObservedInputSummary, otherPipSummary.ObservedInputSummary).ToList();
                foreach (var item in observedDiff)
                {
                    if (item.CachedCount > 0)
                    {
                        // This was reported cached in other pips so it has not changed
                        continue;
                    }

                    if (!observedInputs.Contains(item))
                    {
                        PrintCsvArtifact(writer, pipSummary, item, "ObservedInput", item.GetHashName());
                        observedInputs.Add(item);
                    }
                }
            }
        }

        private void PrintCsvArtifact(StreamWriter writer, SummaryAnalyzer.ProcessPipSummary pipSummary, DependencySummary<string> dependency, string hash, string dependencyType)
        {
            writer.WriteLine(
                    "{0:O},{1},{2},{3},{4:c},{5:c},{6:c},{7:c},{8:c},{9:c},{10},{11}",
                    m_analyzer.GetPipStartTime(pipSummary.Pip),
                    dependency.Name,
                    dependency.Count,
                    pipSummary.ExecutedDependentProcessCount,
                    m_analyzer.GetPipElapsedTime(pipSummary.Pip),
                    m_analyzer.GetPipKernelTime(pipSummary.Pip),
                    m_analyzer.GetPipUserTime(pipSummary.Pip),
                    pipSummary.CriticalPath.Time,
                    pipSummary.CriticalPath.KernelTime,
                    pipSummary.CriticalPath.UserTime,
                    dependencyType,
                    hash);
        }

        #endregion
        private static string ToSeconds(TimeSpan time)
        {
            return Math.Round(time.TotalSeconds, 3).ToString(CultureInfo.InvariantCulture);
        }
    }
}
