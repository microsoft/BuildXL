// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Execution.Analyzer.Analyzers
{
    internal sealed class SummaryAnalyzerHtmlWritter
    {
        private const int AddScrollToTableRowCountLimit = 10;

        private readonly SummaryAnalyzer m_analyzer;
        private readonly HashSet<string> m_changesToReferenceList = new HashSet<string>();

        public SummaryAnalyzerHtmlWritter(SummaryAnalyzer analyzer)
        {
            m_analyzer = analyzer;
        }

        public void PrintHtmlReport(SummaryAnalyzer analyzer, XmlWriter writer)
        {
            writer.WriteRaw("<!DOCTYPE html>");
            writer.WriteStartElement("html");
            writer.WriteStartElement("head");
            RenderStylesheet(writer);
            RenderScript(writer);
            writer.WriteEndElement();
            writer.WriteStartElement("body");
            RenderExecutiveSummaryTable(writer, analyzer);
            RenderPipExecutionSection(writer, analyzer);
            RenderPipExecutedTrackedProcessPips(writer, analyzer);
            RenderArtifactsSummary(writer, analyzer);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private void RenderPipExecutionSection(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            writer.WriteStartElement("div");
            writer.WriteStartElement("h2");
            writer.WriteString("Executed Process Analysis ");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "Current");
            writer.WriteString(" Execution Log");
            writer.WriteEndElement();
            writer.WriteStartElement("p");
            writer.WriteString("This section lists executed process pips in longest pole order in ");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "current");
            writer.WriteString(" log. Reason for execution by comparing to matching pip in ");
            RenderFileAcronym(writer, analyzer.ComparedFilePath, "previous");
            writer.WriteString(" log. Dependent process pips executed, critical path of transitive down dependent pips. Dependent list of process Pips (optional)");
            writer.WriteEndElement();

            var summariesToReport = m_analyzer.GetDifferecesToReport();
            RenderPipExecutions(writer, summariesToReport);
            writer.WriteEndElement(); // closing div
        }

        private void RenderPipExecutedTrackedProcessPips(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            writer.WriteStartElement("div");
            writer.WriteStartElement("h2");
            writer.WriteString("Executed Process Pips In Critical Path");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "Current");
            writer.WriteString(" Execution Log");
            writer.WriteEndElement();
            writer.WriteStartElement("p");
            writer.WriteString("This section lists executed process pips referenced in critical path. ");
            writer.WriteString(" Reason for execution by comparing to matching pip in ");
            RenderFileAcronym(writer, analyzer.ComparedFilePath, "previous");
            writer.WriteString(" log and pip outputs");
            writer.WriteEndElement();

            // Allow collapsing of this section
            writer.WriteStartElement("button");
            writer.WriteAttributeString("type", "button");
            writer.WriteAttributeString("onclick", @"toggleMe('executedTrackedProcessPips')");
            writer.WriteString("Collapse");
            writer.WriteEndElement();

            writer.WriteStartElement("div");
            writer.WriteAttributeString("id", "executedTrackedProcessPips");
            writer.WriteAttributeString("style", "display: block;");
            RenderPipExecutions(writer, m_analyzer.PipSummaryTrackedProcessPips, false);
            writer.WriteEndElement();
            writer.WriteEndElement(); // closing div
        }

#region Single XLG log report
        public void PrintHtmlReport(XmlWriter writer)
        {
            writer.WriteString("<!DOCTYPE html>");
            writer.WriteStartElement("html");
            writer.WriteStartElement("head");
            RenderStylesheet(writer);
            RenderScript(writer);
            writer.WriteEndElement();
            writer.WriteStartElement("body");
            RenderExecutiveSummaryTable(writer);
            RenderPipExecutionSection(writer);
            RenderPipExecutedTrackedProcessPips(writer);

            // RenderArtifactsSummary(writer, analyzer);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private void RenderPipExecutionSection(XmlWriter writer)
        {
            writer.WriteStartElement("div");
            writer.WriteStartElement("h2");
            writer.WriteString("Executed Process");
            writer.WriteEndElement();
            writer.WriteStartElement("p");
            writer.WriteString("This section lists executed process pips in longest pole order. ");
            writer.WriteString("Possible reason for execution by comparing against dependencies from cached Pips");
            writer.WriteString("Dependent process pips executed, critical path of transitive down dependent pips.");
            writer.WriteEndElement();

            var summariesToReport = m_analyzer.GetExecutedProcessPipSummary();
            RenderPipExecutions(writer, summariesToReport);
            writer.WriteEndElement(); // closing div
        }

        private void RenderPipExecutedTrackedProcessPips(XmlWriter writer)
        {
            writer.WriteStartElement("div");
            writer.WriteStartElement("h2");
            writer.WriteString("Executed Process Pips In Critical Path");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "Current");
            writer.WriteString(" Execution Log");
            writer.WriteEndElement();
            writer.WriteStartElement("p");
            writer.WriteString("This section lists executed process pips referenced in critical path.");
            writer.WriteEndElement();

            // Allow collapsing of this section
            writer.WriteStartElement("button");
            writer.WriteAttributeString("type", "button");
            writer.WriteAttributeString("onclick", @"toggleMe('executedTrackedProcessPips')");
            writer.WriteString("Collapse");
            writer.WriteEndElement();

            writer.WriteStartElement("div");
            writer.WriteAttributeString("id", "executedTrackedProcessPips");
            writer.WriteAttributeString("style", "display: block;");
            var critPathPips = m_analyzer.GetSummaryPipsReferencedInCriticalPath();
            RenderPipExecutions(writer, critPathPips, false);
            writer.WriteEndElement();
            writer.WriteEndElement(); // closing div
        }

        private void RenderPipExecutions(XmlWriter writer, IEnumerable<SummaryAnalyzer.ProcessPipSummary> summariesToReport, bool isRootChange = true)
        {
            foreach (var pipSummary in summariesToReport)
            {
                var pip = pipSummary.Pip;
                bool weakFingerprintCacheMiss;
                m_analyzer.TryGetWeakFingerprintCacheMiss(pipSummary.Pip.PipId, out weakFingerprintCacheMiss);

                // If is weak fingerprint miss then show direct dependency suspects only
                var fileArtifactSuspects = weakFingerprintCacheMiss ?
                    SummaryAnalyzer.GenerateFileArtifactSuspects(pipSummary.DependencySummary) : new List<FileArtifactSummary>();
                var environmentSuspects = weakFingerprintCacheMiss ?
                    SummaryAnalyzer.GenerateEnvironmentSuspects(pipSummary.EnvironmentSummary) : new List<DependencySummary<string>>();
                var observedInputsSuspects = !weakFingerprintCacheMiss ?
                    SummaryAnalyzer.GenerateObservedSuspects(pipSummary.ObservedInputSummary) : new List<ObservedInputSummary>();

                // TODO: save distinct suspects to print hygienator CSV output
                if (isRootChange)
                {
                    // List of root executed pips, add a button for each one
                    RenderPipExecutionButton(writer, pipSummary);
                }

                // Create a div for this output with id = pip hash
                writer.WriteStartElement("div");
                writer.WriteAttributeString("id", pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("style", "display: " + (isRootChange ? " none;" : " block;"));

                RenderTableHeader(
                    writer,
                    isRootChange ? string.Empty : m_analyzer.GetPipWorkingDirectory(pip),
                    isRootChange ? s_pipExecutionRootNodeHtmlTableColumns : s_pipExecutionHtmlTableColumns);
                writer.WriteStartElement("tbody");
                var reason = pipSummary.UncacheablePip
                    ? "Un-cacheable Pip"
                    : weakFingerprintCacheMiss ? "Weak Fingerprint Cache-Miss" : "Strong Fingerprint Cache-Miss";

                RenderPipExecutionSummaryTableRow(writer, pipSummary, isRootChange ? reason : "parent");
                writer.WriteEndElement();
                writer.WriteEndElement();  // Closing table tag

                // Add lists of changes in two colums
                writer.WriteStartElement("div");
                writer.WriteAttributeString("class", "row");
                writer.WriteStartElement("div");
                writer.WriteAttributeString("class", "column");

                if (isRootChange)
                {
                    List<List<string>> suspects = ArtifactsToStringList(fileArtifactSuspects);
                    RenderPipArtifactChangesColumn(writer, "File Suspects", suspects);
                    suspects = ArtifactsToStringList(observedInputsSuspects);
                    RenderPipArtifactChangesColumn(writer, "Observed input suspects", suspects);
                    suspects = ArtifactsToStringList(environmentSuspects);
                    RenderPipArtifactChangesColumn(writer, "Environment suspects", suspects);
                }

                writer.WriteEndElement();  // Closing div=column
                RenderPipOutputsColumn(writer, pip.FileOutputs); // Outputs
                writer.WriteEndElement();  // Closing div=row

                // Finally render critical path and tool status
                if (pipSummary.ExecutedDependentProcessCount > 0)
                {
                    RenderCriticalPath(writer, pipSummary.CriticalPath);
                }

                writer.WriteEndElement();  // Closing div
                writer.WriteString("<br>");
            }
        }

        // ArtifactSummary
        private static List<List<string>> ArtifactsToStringList(IEnumerable<DependencySummary<string>> depencySuspects)
        {
            List<List<string>> list = new List<List<string>>();
            foreach (var dependencySuspect in depencySuspects)
            {
                list.Add(dependencySuspect.ToList(0));
            }

            return list;
        }

        private static void RenderPipArtifactChangesColumn(XmlWriter writer, string header, IReadOnlyCollection<List<string>> changes)
        {
            // Only if there are any changes
            if (changes.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("h3");
            writer.WriteString(header);
            writer.WriteEndElement(); // H3
            writer.WriteStartElement("ul");
            foreach (var file in changes)
            {
                writer.WriteStartElement("li");
                writer.WriteStartElement("a");
                writer.WriteAttributeString("href", "#" + file[0]);
                writer.WriteString(file[0]);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // Ul
        }

        private void RenderExecutiveSummaryTable(XmlWriter writer)
        {
            writer.WriteStartElement("h1");
            writer.WriteString("Build Execution report");
            writer.WriteEndElement();

            writer.WriteStartElement("b");
            writer.WriteAttributeString("id", "current");
            writer.WriteString("Execution Log:");
            writer.WriteEndElement();
            writer.WriteString(m_analyzer.ExecutionLogPath);
            writer.WriteString("<br>");

            RenderTableHeader(writer, string.Empty, s_summaryDifferenceHtmlTableColumns);

            // Table body
            writer.WriteStartElement("tbody");

            RenderSummaryTableRow(m_analyzer, writer, "Current");

            // Closing  table tag
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

#endregion

        private void RenderPipExecutions(XmlWriter writer, IEnumerable<(SummaryAnalyzer.ProcessPipSummary pipSummary1, SummaryAnalyzer.ProcessPipSummary pipSummary2)> summariesToReport, bool isRootChange = true)
        {
            // TODO:: if count is large may need to add a DIV
            foreach (var pipDiff in summariesToReport)
            {
                var pipSummary = pipDiff.pipSummary1;
                var otherPipSummary = pipDiff.pipSummary2;
                var pip = pipSummary.Pip;

                var missDisplayReason = string.Empty;
                List<List<string>> environmentChanges = new List<List<string>>();
                List<List<string>> environmentMissing = new List<List<string>>();
                List<List<string>> fileArtifactChanges = new List<List<string>>();
                List<List<string>> fileArtifactMissing = new List<List<string>>();
                List<List<string>> observedInputsChanges = new List<List<string>>();
                List<List<string>> observedInputsMissing = new List<List<string>>();

                if (pipSummary.NewPip)
                {
                    missDisplayReason = "New Pip";
                }
                else if (pipSummary.UncacheablePip)
                {
                    missDisplayReason = "Un-cacheable Pip";
                }
                else
                {
                    bool weakFingerprintCacheMiss;
                    var weakFingerprintFound = m_analyzer.TryGetWeakFingerprintCacheMiss(pipSummary.Pip.PipId, out weakFingerprintCacheMiss);
                    if (weakFingerprintFound)
                    {
                        missDisplayReason = weakFingerprintCacheMiss ? "Weak Fingerprint Cache-Miss" : "Strong Fingerprint Cache-Miss";
                    }

                    if (!pipSummary.Fingerprint.Equals(otherPipSummary.Fingerprint))
                    {
                        var fileChanges = m_analyzer.GenerateFileArtifactDifference(pipSummary.DependencySummary, otherPipSummary.DependencySummary);
                        fileArtifactChanges = fileChanges.fileArtifactChanges;
                        fileArtifactMissing = fileChanges.fileArtifactMissing;
                        if (!weakFingerprintFound && (fileArtifactChanges.Count + fileArtifactMissing.Count > 0))
                        {
                            missDisplayReason += "Dependency";
                        }

                        var environmentDifference = SummaryAnalyzer.GenerateEnvironmentDifference(
                            pipSummary.EnvironmentSummary,
                            otherPipSummary.EnvironmentSummary);
                        environmentChanges = environmentDifference.enviromentChanges;
                        environmentMissing = environmentDifference.enviromentMissing;
                        if (!weakFingerprintFound && (environmentChanges.Count + environmentMissing.Count > 0))
                        {
                            missDisplayReason += " / Environment";
                        }
                    }
                    else if (weakFingerprintFound)
                    {
                        missDisplayReason += " : FP match";
                    }

                    var observedDifference = m_analyzer.GenerateObservedDifference(pipSummary.ObservedInputSummary, otherPipSummary.ObservedInputSummary);
                    observedInputsChanges = observedDifference.fileArtifactChanges;
                    observedInputsMissing = observedDifference.fileArtifactMissing;

                    // TODO: If dependency changes are in temp folder ignore because they have no impact.
                    if (!weakFingerprintFound && (observedInputsChanges.Count != 0 || observedInputsMissing.Count != 0))
                    {
                        missDisplayReason += " / Observed Dependency";
                    }
                }

                // TODO: add a flag to filter out common known changes to minimize noise
                // TODO: if the change impacts all pips then just list longest path instance
                AddChangesToReferenceMap(environmentChanges);
                AddChangesToReferenceMap(environmentMissing);
                AddChangesToReferenceMap(fileArtifactChanges);
                AddChangesToReferenceMap(fileArtifactMissing);
                AddChangesToReferenceMap(observedInputsChanges);
                AddChangesToReferenceMap(observedInputsMissing);

                if (isRootChange)
                {
                    // List of executed pips, add a button for each one
                    RenderPipExecutionButton(writer, pipSummary);
                }

                // Create a div for this output with id = pip hash
                writer.WriteStartElement("div");
                writer.WriteAttributeString("id", pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("style", "display: " + (isRootChange ? " none;" : " block;"));

                RenderTableHeader(
                    writer,
                    isRootChange ? string.Empty : m_analyzer.GetPipWorkingDirectory(pip),
                    isRootChange ? s_pipExecutionRootNodeHtmlTableColumns : s_pipExecutionHtmlTableColumns);
                writer.WriteStartElement("tbody");
                RenderPipExecutionSummaryTableRow(writer, pipSummary, missDisplayReason);
                writer.WriteEndElement();
                writer.WriteEndElement();  // Closing table tag

                // Add lists of changes in two colums
                writer.WriteStartElement("div");
                writer.WriteAttributeString("class", "row");
                writer.WriteStartElement("div");
                writer.WriteAttributeString("class", "column");
                RenderPipArtifactChangesColumn(writer, "Environment Changes", environmentChanges, environmentMissing);
                RenderPipArtifactChangesColumn(writer, "File Changes", fileArtifactChanges, fileArtifactMissing);
                RenderPipArtifactChangesColumn(writer, "Observed input changes", observedInputsChanges, observedInputsMissing);
                writer.WriteEndElement();  // Closing div=column
                RenderPipOutputsColumn(writer, pip.FileOutputs); // Outputs
                writer.WriteEndElement();  // Closing div=row

                // Finally render critical path and tool status
                if (pipSummary.ExecutedDependentProcessCount > 0)
                {
                    RenderCriticalPath(writer, pipSummary.CriticalPath);
                }

                writer.WriteEndElement();  // Closing div
                writer.WriteString("<br>");
            }
        }

        private void RenderPipExecutionButton(XmlWriter writer, SummaryAnalyzer.ProcessPipSummary pipSummary)
        {
            var pip = pipSummary.Pip;
            writer.WriteStartElement("button");
            writer.WriteAttributeString("id", "pip" + pip.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("type", "button");
            writer.WriteAttributeString(
                "onclick",
                @"toggleMe('" + pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture) + "')");
            var elapsedTime = pipSummary.CriticalPath.Node.IsValid
                ? pipSummary.CriticalPath.Time
                : m_analyzer.GetPipElapsedTime(pip);
            var executedPipsString = m_analyzer.IsPipFailed(pipSummary.Pip.PipId)
                ? @" <span style=""color: #FF6347;"">Executed Pips = </span>"
                : @" <span style=""color: #8FBC8F;"">Executed Pips = </span>";
            writer.WriteString(
                m_analyzer.GetPipWorkingDirectory(pip) + executedPipsString +
                pipSummary.ExecutedDependentProcessCount +
                @" <span style=""color: #8FBC8F;"">critPath =</span>" + elapsedTime.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        private void RenderCriticalPath(XmlWriter writer, BuildXL.Execution.Analyzer.Analyzer.NodeAndCriticalPath nodeAndCriticalPath)
        {
            var toolStats = new ConcurrentDictionary<PathAtom, TimeSpan>();
            RenderTableHeader(writer, "Critical Path : Calculated using wall time duration of each dependent pip", s_criticalPathHtmlTableColumns);
            writer.WriteStartElement("tbody");
            while (true)
            {
                Pip pip = m_analyzer.GetPipByPipId(new PipId(nodeAndCriticalPath.Node.Value));
                var process = pip as Process;

                var elapsed = m_analyzer.GetElapsed(nodeAndCriticalPath.Node);
                var kernelTime = m_analyzer.GetPipKernelTime(pip);
                var userTime = m_analyzer.GetPipUserTime(pip);

                writer.WriteStartElement("tr");

                writer.WriteStartElement("td");
                writer.WriteString(ToSeconds(elapsed));
                writer.WriteEndElement();

                writer.WriteStartElement("td");
                writer.WriteString(ToSeconds(kernelTime));
                writer.WriteEndElement();

                writer.WriteStartElement("td");
                writer.WriteString(ToSeconds(userTime));
                writer.WriteEndElement();

                writer.WriteStartElement("td");
                string pipDescription;
                if (process != null)
                {
                    toolStats.AddOrUpdate(m_analyzer.GetPipToolName(process), elapsed, (k, v) => v + elapsed);
                    pipDescription = m_analyzer.GetPipDescriptionName(pip);
                    if (m_analyzer.IsPipReferencedInCriticalPath(process))
                    {
                        writer.WriteStartElement("a");
                        writer.WriteAttributeString("href", "#" + pip.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
                    }

                    writer.WriteString(pipDescription);
                    if (m_analyzer.IsPipReferencedInCriticalPath(process))
                    {
                        writer.WriteEndElement();
                    }
                }
                else
                {
                    pipDescription = m_analyzer.GetPipDescription(pip);
                    pipDescription = pipDescription.Replace('<', ' ').Replace('>', ' ');
                    writer.WriteString(pipDescription);
                }

                writer.WriteEndElement();

                writer.WriteStartElement("td");
                var typeOrDir = process != null ? m_analyzer.GetPipWorkingDirectory(process) : pip.PipType.ToString();
                writer.WriteString(typeOrDir);
                writer.WriteEndElement();
                writer.WriteEndElement(); // tr

                if (!nodeAndCriticalPath.Next.IsValid)
                {
                    break;
                }

                nodeAndCriticalPath = m_analyzer.GetImpactPath(nodeAndCriticalPath.Next);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();

            // Table of tools stats for this critical path
            RenderTableHeader(writer, "Tools stats", new List<string> { "Seconds", "Tool" });
            writer.WriteStartElement("tbody");
            foreach (var toolStatEntry in toolStats.OrderByDescending(kvp => kvp.Value))
            {
                writer.WriteStartElement("tr");
                writer.WriteStartElement("td");
                writer.WriteString(ToSeconds(toolStatEntry.Value));
                writer.WriteEndElement();

                writer.WriteStartElement("td");
                writer.WriteString(m_analyzer.GetPathAtomToString(toolStatEntry.Key));
                writer.WriteEndElement();
                writer.WriteEndElement(); // tr
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        /// <summary>
        /// This keeps track of changes to be able to link summary table with a change in the
        /// document
        /// </summary>
        private void AddChangesToReferenceMap(IEnumerable<List<string>> changes)
        {
            foreach (var entry in changes)
            {
                m_changesToReferenceList.Add(entry[0]);
            }
        }

        private static void RenderPipArtifactChangesColumn(XmlWriter writer, string header, List<List<string>> changes, List<List<string>> missing)
        {
            // Only if there are any changes
            if (changes.Count + missing.Count == 0)
            {
                return;
            }

            writer.WriteStartElement("h3");
            writer.WriteString(header);
            writer.WriteEndElement(); // H3
            writer.WriteStartElement("ul");
            foreach (var file in changes)
            {
                writer.WriteStartElement("li");
                writer.WriteStartElement("a");
                writer.WriteAttributeString("href", "#" + file[0]);
                writer.WriteString(file[0]);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            foreach (var file in missing)
            {
                writer.WriteStartElement("li");
                writer.WriteStartElement("a");
                writer.WriteAttributeString("href", "#" + file[0]);
                writer.WriteString("Dependency removed: " + file[0]);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // Ul
        }

        private void RenderPipOutputsColumn(XmlWriter writer, ReadOnlyArray<FileArtifactWithAttributes> fileOutputs)
        {
            writer.WriteStartElement("div");
            writer.WriteAttributeString("class", "column");
            writer.WriteStartElement("h3");
            writer.WriteString("Outputs");
            writer.WriteEndElement(); // H3
            writer.WriteStartElement("ul");
            foreach (var output in fileOutputs)
            {
                writer.WriteStartElement("li");
                writer.WriteString(m_analyzer.GetAbsolutePathToString(output.Path));
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // Ul
            writer.WriteEndElement(); // Closing div=column of Outputs
        }

        private void RenderPipExecutionSummaryTableRow(XmlWriter writer, SummaryAnalyzer.ProcessPipSummary pipSummary, string missReason)
        {
            var pip = pipSummary.Pip;
            writer.WriteStartElement("tr");

            // Pip name
            writer.WriteStartElement("td");
            writer.WriteString(m_analyzer.GetPipDescriptionName(pip));
            writer.WriteEndElement();

            // Start time
            writer.WriteStartElement("td");
            var executionStart = m_analyzer.GetPipStartTime(pip);
            var executionStartText = executionStart.Equals(DateTime.MinValue) ? "-" : executionStart.ToString("h:mm:ss.ff", CultureInfo.InvariantCulture);
            writer.WriteString(executionStartText);
            writer.WriteEndElement();

            // Duration
            writer.WriteStartElement("td");
            writer.WriteString(m_analyzer.GetPipElapsedTime(pip).ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Time kernel
            writer.WriteStartElement("td");
            var kernelTime = pipSummary.CriticalPath.Node.IsValid ? pipSummary.CriticalPath.KernelTime : m_analyzer.GetPipKernelTime(pip);
            writer.WriteString(kernelTime.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Time user
            writer.WriteStartElement("td");
            var userTime = pipSummary.CriticalPath.Node.IsValid ? pipSummary.CriticalPath.UserTime : m_analyzer.GetPipUserTime(pip);
            writer.WriteString(userTime.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Pip stable hash
            writer.WriteStartElement("td");
            writer.WriteString(pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Reason for execution
            writer.WriteStartElement("td");
            writer.WriteStartElement("a");
            writer.WriteAttributeString("href", "https://www.1eswiki.com/wiki/Domino_execution_analyzer#Reasons_for_process_Pip_Execution_section");
            writer.WriteString(missReason);
            writer.WriteEndElement();
            writer.WriteEndElement();

            // Invalidated pips
            writer.WriteStartElement("td");
            writer.WriteString(
                pipSummary.CriticalPath.Node.IsValid ? pipSummary.ExecutedDependentProcessCount.ToString(CultureInfo.InvariantCulture) : "-");
            writer.WriteEndElement();
            writer.WriteEndElement(); // Closing Tr
        }

        private static void RenderFileAcronym(XmlWriter writer, string fileName, string abbr)
        {
            writer.WriteStartElement("acronym");
            writer.WriteAttributeString("title", fileName);
            writer.WriteString(abbr);
            writer.WriteEndElement();
        }

        private void RenderArtifactsSummary(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            writer.WriteStartElement("div");
            writer.WriteStartElement("h2");
            writer.WriteStartElement("button");
            writer.WriteAttributeString("type", "button");
            writer.WriteAttributeString("onclick", @"toggleMe('artifactSummarySection')");
            writer.WriteString("Summary");
            writer.WriteEndElement();

            writer.WriteEndElement();

            writer.WriteStartElement("p");
            writer.WriteString("This section compares the summary of the distinct Pip artifacts between ");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "current");
            writer.WriteString(" and ");
            RenderFileAcronym(writer, analyzer.ComparedFilePath, "previous");
            writer.WriteString(" logs, showing only those artifacts with distinct hash, missing or different value for environment variables.");
            writer.WriteEndElement();

            writer.WriteStartElement("div");
            writer.WriteAttributeString("id", "artifactSummarySection");
            writer.WriteAttributeString("style", "display: block;");
            writer.WriteStartElement("div");
            RenderEnvironmentSummaryTable(writer, analyzer);
            RenderFileArtifactSummaryTable(writer, analyzer);
            RenderObservedInputsSummaryTable(writer, analyzer);
            RenderDirectoryMembershipSummaryTable(writer, analyzer);
            RenderDirectorydependencySummaryTable(writer);
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        private static readonly List<string> s_criticalPathHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "Elapsed",
                                                                     "Kernel",
                                                                     "User",
                                                                     "Pip description",
                                                                     "Directory",
                                                                 };

        private static readonly List<string> s_pipExecutionRootNodeHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "Processes Pip executed",
                                                                     "Start Time",
                                                                     "Duration (s)",
                                                                     "Critical Path Kernel",
                                                                     "Critical Path User",
                                                                     "Pip Hash",
                                                                     "Reason for execution",
                                                                     "Transitive Process invalidated",
                                                                 };

        private static readonly List<string> s_pipExecutionHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "Processes Pip executed",
                                                                     "Start Time",
                                                                     "Duration (s)",
                                                                     "Kernel Time",
                                                                     "User Time",
                                                                     "Pip Hash",
                                                                     "Reason for execution",
                                                                     "Transitive Process invalidated",
                                                                 };

        private static readonly List<string> s_summaryDifferenceHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "Label",
                                                                     "Processes Pips",
                                                                     "Process Pip Cache-Misses",
                                                                     "% Processes Cached",
                                                                     "Failed Pips",
                                                                     "Files Produced",
                                                                     "Files FromCache",
                                                                     "Files UpToDate",
                                                                     "Uncacheable Pips",
                                                                 };

        private static readonly List<string> s_environmentDifferenceHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "Name",
                                                                     "Pips direct impact",
                                                                     @"<a href=""#current"">Current</a> Value",
                                                                     @"<a href=""#previous"">Previous</a> Value",
                                                                 };

        private static readonly List<string> s_fileArtifactDifferenceHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "File Name",
                                                                     "Type",
                                                                     "Origin",
                                                                     "Direct PIP reference",
                                                                     "PIP referenced %",
                                                                     @"<a href=""#current"">Current</a> File Hash",
                                                                     @"<a href=""#previous"">Previous</a> File Hash",
                                                                 };

        private static readonly List<string> s_observedInputsDifferenceHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "Name",
                                                                     "Type",
                                                                     "Direct PIP reference",
                                                                     "PIP referenced %",
                                                                     @"<a href=""#current"">Current</a> File Hash",
                                                                     @"<a href=""#previous"">Previous</a> File Hash",
                                                                 };

        private static readonly List<string> s_directoryMembershipDifferenceHtmlTableColumns = new List<string>()
                                                                 {
                                                                     "Directory",
                                                                     @"<a href=""#current"">Current</a> Execution Log",
                                                                     @"<a href=""#previous"">Previous</a> Execution Log",
                                                                 };

        private static void RenderTableHeader(XmlWriter writer, string caption, List<string> columns)
        {
            writer.WriteStartElement("table");

            writer.WriteStartElement("caption");
            writer.WriteStartElement("b");

            // Table caption
            writer.WriteString(caption);
            writer.WriteEndElement();
            writer.WriteEndElement();

            // Table header
            writer.WriteStartElement("thead");

            // Table columns row
            writer.WriteStartElement("tr");
            foreach (var column in columns)
            {
                writer.WriteStartElement("th");
                writer.WriteString(column);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private void RenderTableRow(XmlWriter writer, IEnumerable<List<string>> rows)
        {
            var rowCount = 0;
            foreach (var row in rows)
            {
                if (rowCount > m_analyzer.MaxDifferenceReportCount * 2 && !m_changesToReferenceList.Contains(row[0]))
                {
                    // limit number of rows changes, allow those referenced in executed Pips
                    continue;
                }

                writer.WriteStartElement("tr");
                foreach (var column in row)
                {
                    writer.WriteStartElement("td");
                    if (m_changesToReferenceList.Contains(column))
                    {
                        writer.WriteAttributeString("id", column);
                    }

                    writer.WriteString(column);
                    writer.WriteEndElement();
                }

                rowCount++;
                writer.WriteEndElement();
            }
        }

        private void RenderDifferenceSummaryTable(
            XmlWriter writer,
            string tableHeader,
            List<string> columnNames,
            List<List<string>> difference,
            List<List<string>> missing)
        {
            if (difference.Count + missing.Count > AddScrollToTableRowCountLimit)
            {
                writer.WriteStartElement("div");
                // add a div to scroll when the count of rows is over the limit
                writer.WriteAttributeString("style", "height: 500px; overflow-y: auto");
            }

            RenderTableHeader(writer, tableHeader, columnNames);

            // Table body
            writer.WriteStartElement("tbody");

            // Add each row of changes
            RenderTableRow(writer, difference);

            if (missing.Count > 0)
            {
                // TODO: will consider making this a different table
                // Add any missing items
                writer.WriteStartElement("tr");
                writer.WriteStartElement("td");
                writer.WriteAttributeString("colspan", missing.Count.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("Removed Dependencies");
                writer.WriteEndElement();
                writer.WriteEndElement();

                RenderTableRow(writer, missing);
            }

            writer.WriteEndElement();

            // Closing table tag
            writer.WriteEndElement();

            if (difference.Count + missing.Count > AddScrollToTableRowCountLimit)
            {
                // Closing div tag if any
                writer.WriteEndElement();
            }
        }

        private void RenderEnvironmentSummaryTable(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            var environmentDifference = SummaryAnalyzer.GenerateEnvironmentDifference(m_analyzer.Summary.EnvironmentSummary, analyzer.Summary.EnvironmentSummary);
            if (environmentDifference.enviromentChanges.Count == 0 && environmentDifference.enviromentMissing.Count == 0)
            {
                return;
            }

            RenderDifferenceSummaryTable(
                writer,
                "Environment variables difference",
                s_environmentDifferenceHtmlTableColumns,
                environmentDifference.enviromentChanges,
                environmentDifference.enviromentMissing);
        }

        private void RenderFileArtifactSummaryTable(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            var fileArtifactCompare = m_analyzer.GenerateFileArtifactDifference(m_analyzer.Summary.FileArtifactSummary, analyzer.Summary.FileArtifactSummary);
            if (fileArtifactCompare.fileArtifactChanges.Count == 0 && fileArtifactCompare.fileArtifactMissing.Count == 0)
            {
                return;
            }

            RenderDifferenceSummaryTable(
                writer,
                "File artifact summary",
                s_fileArtifactDifferenceHtmlTableColumns,
                fileArtifactCompare.fileArtifactChanges,
                fileArtifactCompare.fileArtifactMissing);
        }

        private void RenderObservedInputsSummaryTable(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            var observedDifference = m_analyzer.GenerateObservedDifference(m_analyzer.Summary.ObservedSummary, analyzer.Summary.ObservedSummary);
            if (observedDifference.fileArtifactChanges.Count == 0 && observedDifference.fileArtifactMissing.Count == 0)
            {
                return;
            }

            // TODO: Add directory enumerations link to
            RenderDifferenceSummaryTable(
                writer,
                "Observed inputs summary",
                s_observedInputsDifferenceHtmlTableColumns,
                observedDifference.fileArtifactChanges,
                observedDifference.fileArtifactMissing);
        }

        private void RenderDirectoryMembershipSummaryTable(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            // Find the top observed imput directories that have changed and make sure the enumeration is listed
            var observedDiff = SummaryAnalyzer.GetFileDependencyDiff(m_analyzer.Summary.ObservedSummary, analyzer.Summary.ObservedSummary);
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

            var directoryMembershipNotInTwo = SummaryAnalyzer.GetDirectoryMembershipDiff(m_analyzer.Summary.DirectoryMembership, analyzer.Summary.DirectoryMembership);
            var directoryMembershipNotInOne = SummaryAnalyzer.GetDirectoryMembershipDiff(analyzer.Summary.DirectoryMembership, m_analyzer.Summary.DirectoryMembership);

            if (directoryMembershipNotInTwo.Count == 0 && directoryMembershipNotInOne.Count == 0 && directoriesToEnumerate.Count == 0)
            {
                return;
            }

            if (directoryMembershipNotInTwo.Count + directoryMembershipNotInOne.Count > AddScrollToTableRowCountLimit)
            {
                // add a div to scroll when the count of rows is over the limit
                writer.WriteStartElement("div");
                writer.WriteAttributeString("style", "height: 500px; overflow-y: auto");
            }

            var writeCount = 0;
            RenderTableHeader(writer, "Directory membership difference", s_directoryMembershipDifferenceHtmlTableColumns);

            // Table body
            writer.WriteStartElement("tbody");

            // Enumerate changes in observed inputs first to show those relevant changes
            foreach (var s in directoryMembershipNotInTwo)
            {
                if (writeCount++ <= m_analyzer.MaxDifferenceReportCount || directoriesToEnumerate.Contains(s.Key))
                {
                    RenderDirectoryEnumeration(writer, s.Key, s.Value, true);
                }
            }

            foreach (var s in directoryMembershipNotInOne)
            {
                if (writeCount++ > m_analyzer.MaxDifferenceReportCount)
                {
                    break;
                }

                RenderDirectoryEnumeration(writer, s.Key, s.Value, false);
            }

            writer.WriteEndElement();

            // Closing table tag
            writer.WriteEndElement();

            if (directoryMembershipNotInTwo.Count + directoryMembershipNotInOne.Count > AddScrollToTableRowCountLimit)
            {
                // Closing div tag if any
                writer.WriteEndElement();
            }
        }

        private static void RenderDirectoryEnumeration(XmlWriter writer, string directory, List<string> rows, bool current)
        {
            writer.WriteStartElement("tr");
            writer.WriteStartElement("td");
            writer.WriteAttributeString("rowspan", (rows.Count + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteString(directory);
            writer.WriteEndElement();
            writer.WriteEndElement();

            foreach (var row in rows)
            {
                var firstColum = current ? row : "no";
                var secondColumn = current ? "no" : row;
                writer.WriteStartElement("tr");
                writer.WriteStartElement("td");
                writer.WriteString(firstColum);
                writer.WriteEndElement();
                writer.WriteStartElement("td");
                writer.WriteString(secondColumn);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        private static void RenderDirectorydependencySummaryTable(XmlWriter writer)
        {
            // TODO: fill the data
            writer.WriteStartElement("div");
            writer.WriteAttributeString("style", "height: 500px; overflow-y: auto");
            writer.WriteStartElement("div");
            RenderTableHeader(writer, "Directory dependency summary", s_observedInputsDifferenceHtmlTableColumns);

            // Table body
            writer.WriteStartElement("tbody");

            // TODO: fill the data
            // Add each row of changes
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        private void RenderExecutiveSummaryTable(XmlWriter writer, SummaryAnalyzer analyzer)
        {
            writer.WriteStartElement("h1");

            // Add a link to wiki documentation.
            writer.WriteStartElement("a");
            writer.WriteAttributeString("href", "https://www.1eswiki.com/wiki/Domino_execution_analyzer#HTML_Diff_Report_output");
            writer.WriteStartElement("a");
            writer.WriteString("Execution log compare report");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("b");
            writer.WriteAttributeString("id", "current");
            writer.WriteStartElement("b");
            writer.WriteString("Current Execution Log:");
            writer.WriteEndElement();
            writer.WriteString(m_analyzer.ExecutionLogPath);
            writer.WriteString("<br>");

            writer.WriteStartElement("b");
            writer.WriteAttributeString("id", "previous");
            writer.WriteStartElement("b");
            writer.WriteString("Previous Execution Log:");
            writer.WriteEndElement();
            writer.WriteString(analyzer.ExecutionLogPath);
            writer.WriteString("<br>");

            // Check difference in the salt flags which is a global change that impacts all fingerprints
            // Diplay this in red to show the global impact
            if (!m_analyzer.CompareSaltsEquals(analyzer))
            {
                writer.WriteStartElement("h3");
                writer.WriteStartElement("button");
                writer.WriteAttributeString("type", "button");
                writer.WriteAttributeString("onclick", @"toggleMe('Global_fingerprint_change')");
                writer.WriteAttributeString("style", "background-color:#FF6347");
                writer.WriteStartElement("button");
                writer.WriteString("Global fingerprint change");
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement("div");
                writer.WriteAttributeString("id", "Global_fingerprint_change");
                writer.WriteAttributeString("style", "display: none;");
                writer.WriteStartElement("div");

                var saltDiffs = m_analyzer.GetSaltsDifference(analyzer);
                writer.WriteStartElement("ul");

                foreach (var diff in saltDiffs)
                {
                    writer.WriteStartElement("li");
                    writer.WriteString(diff);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // Ul
                writer.WriteEndElement(); // Closing div
            }

            RenderTableHeader(writer, string.Empty, s_summaryDifferenceHtmlTableColumns);

            // Table body
            writer.WriteStartElement("tbody");

            RenderSummaryTableRow(m_analyzer, writer, "Current");
            RenderSummaryTableRow(analyzer, writer, "Previous");
            writer.WriteEndElement();

            // Closing  table tag
            writer.WriteEndElement();
        }

        private const float LowHitRateLimit = 75;

        private static void RenderSummaryTableRow(SummaryAnalyzer analyzer, XmlWriter writer, string fileReference)
        {
            writer.WriteStartElement("tr");

            writer.WriteStartElement("td");
            RenderFileAcronym(writer, analyzer.ExecutionLogPath, fileReference);
            writer.WriteEndElement();

            // Process pips
            writer.WriteStartElement("td");
            writer.WriteString(analyzer.GetProcessPipCount().ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Executed pips
            writer.WriteStartElement("td");
            writer.WriteString(analyzer.GetExecutedProcessPipCount().ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Cache hit
            float hitRate = analyzer.GetProcessPipHitRate();
            var backGroundColor = "#00FF66";
            if (hitRate < LowHitRateLimit)
            {
                backGroundColor = "#FF6347";
            }

            writer.WriteStartElement("td");
            writer.WriteAttributeString("bgcolor", backGroundColor);
            writer.WriteString(hitRate.ToString("F", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Failed Pips
            writer.WriteStartElement("td");
            writer.WriteString(analyzer.GetFailedProcessPipCount().ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Output files produced
            writer.WriteStartElement("td");
            writer.WriteString(analyzer.GetProducedFileCount().ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Output files from cache
            writer.WriteStartElement("td");
            writer.WriteString(analyzer.GetCachedFileCount().ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // Output files up to date
            writer.WriteStartElement("td");
            writer.WriteString(analyzer.GetUpToDateFileCount().ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            // uncacheable pips
            writer.WriteStartElement("td");
            writer.WriteString(analyzer.GetUncacheableProcessPipCount().ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        private static void RenderScript(XmlWriter writer)
        {
            writer.WriteStartElement("script");
            writer.WriteString(@"function toggleMe(a) {
                var e = document.getElementById(a);
                if (!e)return true;
                if (e.style.display === ""none"") {
                    e.style.display = ""block"";
                } else {
                    e.style.display = ""none"";
                }
                return true;
                }");
            writer.WriteEndElement();
        }

        /// <summary>
        /// Render the stylesheet (.css)
        /// </summary>
        private static void RenderStylesheet(XmlWriter writer)
        {
            writer.WriteStartElement("style");
            writer.WriteAttributeString("type", "text/css");
            writer.WriteStartElement("style");
            writer.WriteString(@"    
            h1 {
                font-size: 32px;
                line-height: 40px;
                color: gray;
                border-bottom: 1px solid black;
            }

            h2 {
                font-size: 28px;
                line-height: 40px;
                color: gray;
                border-bottom: 1px solid black;
            }

            h3 {
                font-size: 20px;
                line-height: 40px;
            }

            table {
                   border-collapse:separate;
                   border:solid black 1px;
                   border-radius:6px;
                   -moz-border-radius:6px;
            }

            th, td {
                border: 1px solid black;
                padding: 5px;
                border-top: none;
                border-left: none;
            }

            th {
                height: 50px;
                text-align: left;
                background-color: #AFEEEF;
            }

            tr:hover {
                background-color: #f5f5f5;
            }

            tr:nth-child(even) {
                background-color: #E0FFFF;
            }

            caption {
                    font-size: 20px;
                    line-height: 40px;
                    color: gray;
                    text-align: left;
                }
            .column {
                width: 50%;
                float: left;
            }
            .row {
                  clear:both;
                  height: 360px; 
                  overflow-y: auto
            }
            button {
                display: inline-block;
                font-size: 1.1em;
                font-weight: bold;
                text-transform: uppercase;
                padding: 10px 15px;
                margin: 10px auto;
                color: #fff;
                background-color: #1874CD;
                border: 0 none;
                border-radius: 8px;
                text-shadow: 0 -1px 0 #000;
                transition: all 0.5s;
                box-shadow: 0 1px 0 #666, 0 5px 0 #444, 0 6px 6px rgba(0,0,0,0.6);
                cursor: pointer;
              }
            button:hover {background-color: #1C1C1C}
            acronym {
                color: #104E8B;
            }");
            writer.WriteEndElement();
        }

        private static string ToSeconds(TimeSpan time)
        {
            return Math.Round(time.TotalSeconds, 3).ToString(CultureInfo.InvariantCulture);
        }
    }
}
