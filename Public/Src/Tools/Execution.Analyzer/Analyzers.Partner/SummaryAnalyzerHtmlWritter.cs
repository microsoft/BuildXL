// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_HTMLWRITER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.UI;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Execution.Analyzer;

namespace BuildXL.Execution.Analyzer.Analyzers
{
    internal sealed class SummaryAnalyzerHtmlWritter
    {
        private const int AddScrollToTableRowCountLimit = 10;

        private readonly SummaryAnalyzer m_analyzer;
        private HashSet<string> m_changesToReferenceList = new HashSet<string>();

        public SummaryAnalyzerHtmlWritter(SummaryAnalyzer analyzer)
        {
            m_analyzer = analyzer;
        }

        public void PrintHtmlReport(SummaryAnalyzer analyzer, HtmlTextWriter writer)
        {
            writer.Write("<!DOCTYPE html>");
            writer.RenderBeginTag(HtmlTextWriterTag.Html);
            writer.RenderBeginTag(HtmlTextWriterTag.Head);
            RenderStylesheet(writer);
            RenderScript(writer);
            writer.RenderEndTag();
            writer.RenderBeginTag(HtmlTextWriterTag.Body);
            RenderExecutiveSummaryTable(writer, analyzer);
            RenderPipExecutionSection(writer, analyzer);
            RenderPipExecutedTrackedProcessPips(writer, analyzer);
            RenderArtifactsSummary(writer, analyzer);
            writer.RenderEndTag();
            writer.RenderEndTag();
        }

        private void RenderPipExecutionSection(HtmlTextWriter writer, SummaryAnalyzer analyzer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            writer.RenderBeginTag(HtmlTextWriterTag.H2);
            writer.Write("Executed Process Analysis ");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "Current");
            writer.Write(" Execution Log");
            writer.RenderEndTag();
            writer.RenderBeginTag(HtmlTextWriterTag.P);
            writer.Write("This section lists executed process pips in longest pole order in ");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "current");
            writer.Write(" log. Reason for execution by comparing to matching pip in ");
            RenderFileAcronym(writer, analyzer.ComparedFilePath, "previous");
            writer.Write(" log. Dependent process pips executed, critical path of transitive down dependent pips. Dependent list of process Pips (optional)");
            writer.RenderEndTag();

            var summariesToReport = m_analyzer.GetDifferecesToReport();
            RenderPipExecutions(writer, summariesToReport);
            writer.RenderEndTag(); // closing div
        }

        private void RenderPipExecutedTrackedProcessPips(HtmlTextWriter writer, SummaryAnalyzer analyzer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            writer.RenderBeginTag(HtmlTextWriterTag.H2);
            writer.Write("Executed Process Pips In Critical Path");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "Current");
            writer.Write(" Execution Log");
            writer.RenderEndTag();
            writer.RenderBeginTag(HtmlTextWriterTag.P);
            writer.Write("This section lists executed process pips referenced in critical path. ");
            writer.Write(" Reason for execution by comparing to matching pip in ");
            RenderFileAcronym(writer, analyzer.ComparedFilePath, "previous");
            writer.Write(" log and pip outputs");
            writer.RenderEndTag();

            // Allow collapsing of this section
            writer.AddAttribute(HtmlTextWriterAttribute.Type, "button");
            writer.AddAttribute(HtmlTextWriterAttribute.Onclick, @"toggleMe('executedTrackedProcessPips')");
            writer.RenderBeginTag(HtmlTextWriterTag.Button);
            writer.Write("Collapse");
            writer.RenderEndTag();

            writer.AddAttribute(HtmlTextWriterAttribute.Id, "executedTrackedProcessPips");
            writer.AddAttribute(HtmlTextWriterAttribute.Style, "display: block;");
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            RenderPipExecutions(writer, m_analyzer.PipSummaryTrackedProcessPips, false);
            writer.RenderEndTag();
            writer.RenderEndTag(); // closing div
        }

#region Single XLG log report
        public void PrintHtmlReport(HtmlTextWriter writer)
        {
            writer.Write("<!DOCTYPE html>");
            writer.RenderBeginTag(HtmlTextWriterTag.Html);
            writer.RenderBeginTag(HtmlTextWriterTag.Head);
            RenderStylesheet(writer);
            RenderScript(writer);
            writer.RenderEndTag();
            writer.RenderBeginTag(HtmlTextWriterTag.Body);
            RenderExecutiveSummaryTable(writer);
            RenderPipExecutionSection(writer);
            RenderPipExecutedTrackedProcessPips(writer);

            // RenderArtifactsSummary(writer, analyzer);
            writer.RenderEndTag();
            writer.RenderEndTag();
        }

        private void RenderPipExecutionSection(HtmlTextWriter writer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            writer.RenderBeginTag(HtmlTextWriterTag.H2);
            writer.Write("Executed Process");
            writer.RenderEndTag();
            writer.RenderBeginTag(HtmlTextWriterTag.P);
            writer.Write("This section lists executed process pips in longest pole order. ");
            writer.Write("Possible reason for execution by comparing against dependencies from cached Pips");
            writer.Write("Dependent process pips executed, critical path of transitive down dependent pips.");
            writer.RenderEndTag();

            var summariesToReport = m_analyzer.GetExecutedProcessPipSummary();
            RenderPipExecutions(writer, summariesToReport);
            writer.RenderEndTag(); // closing div
        }

        private void RenderPipExecutedTrackedProcessPips(HtmlTextWriter writer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            writer.RenderBeginTag(HtmlTextWriterTag.H2);
            writer.Write("Executed Process Pips In Critical Path");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "Current");
            writer.Write(" Execution Log");
            writer.RenderEndTag();
            writer.RenderBeginTag(HtmlTextWriterTag.P);
            writer.Write("This section lists executed process pips referenced in critical path.");
            writer.RenderEndTag();

            // Allow collapsing of this section
            writer.AddAttribute(HtmlTextWriterAttribute.Type, "button");
            writer.AddAttribute(HtmlTextWriterAttribute.Onclick, @"toggleMe('executedTrackedProcessPips')");
            writer.RenderBeginTag(HtmlTextWriterTag.Button);
            writer.Write("Collapse");
            writer.RenderEndTag();

            writer.AddAttribute(HtmlTextWriterAttribute.Id, "executedTrackedProcessPips");
            writer.AddAttribute(HtmlTextWriterAttribute.Style, "display: block;");
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            var critPathPips = m_analyzer.GetSummaryPipsReferencedInCriticalPath();
            RenderPipExecutions(writer, critPathPips, false);
            writer.RenderEndTag();
            writer.RenderEndTag(); // closing div
        }

        private void RenderPipExecutions(HtmlTextWriter writer, IEnumerable<SummaryAnalyzer.ProcessPipSummary> summariesToReport, bool isRootChange = true)
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
                writer.AddAttribute(HtmlTextWriterAttribute.Id, pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
                writer.AddAttribute(HtmlTextWriterAttribute.Style, "display: " + (isRootChange ? " none;" : " block;"));
                writer.RenderBeginTag(HtmlTextWriterTag.Div);

                RenderTableHeader(
                    writer,
                    isRootChange ? string.Empty : m_analyzer.GetPipWorkingDirectory(pip),
                    isRootChange ? s_pipExecutionRootNodeHtmlTableColumns : s_pipExecutionHtmlTableColumns);
                writer.RenderBeginTag(HtmlTextWriterTag.Tbody);
                var reason = pipSummary.UncacheablePip
                    ? "Un-cacheable Pip"
                    : weakFingerprintCacheMiss ? "Weak Fingerprint Cache-Miss" : "Strong Fingerprint Cache-Miss";

                RenderPipExecutionSummaryTableRow(writer, pipSummary, isRootChange ? reason : "parent");
                writer.RenderEndTag();
                writer.RenderEndTag();  // Closing table tag

                // Add lists of changes in two colums
                writer.AddAttribute(HtmlTextWriterAttribute.Class, "row");
                writer.RenderBeginTag(HtmlTextWriterTag.Div);
                writer.AddAttribute(HtmlTextWriterAttribute.Class, "column");
                writer.RenderBeginTag(HtmlTextWriterTag.Div);

                if (isRootChange)
                {
                    List<List<string>> suspects = ArtifactsToStringList(fileArtifactSuspects);
                    RenderPipArtifactChangesColumn(writer, "File Suspects", suspects);
                    suspects = ArtifactsToStringList(observedInputsSuspects);
                    RenderPipArtifactChangesColumn(writer, "Observed input suspects", suspects);
                    suspects = ArtifactsToStringList(environmentSuspects);
                    RenderPipArtifactChangesColumn(writer, "Environment suspects", suspects);
                }

                writer.RenderEndTag();  // Closing div=column
                RenderPipOutputsColumn(writer, pip.FileOutputs); // Outputs
                writer.RenderEndTag();  // Closing div=row

                // Finally render critical path and tool status
                if (pipSummary.ExecutedDependentProcessCount > 0)
                {
                    RenderCriticalPath(writer, pipSummary.CriticalPath);
                }

                writer.RenderEndTag();  // Closing div
                writer.Write("<br>");
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

        private static void RenderPipArtifactChangesColumn(HtmlTextWriter writer, string header, IReadOnlyCollection<List<string>> changes)
        {
            // Only if there are any changes
            if (changes.Count == 0)
            {
                return;
            }

            writer.RenderBeginTag(HtmlTextWriterTag.H3);
            writer.Write(header);
            writer.RenderEndTag(); // H3
            writer.RenderBeginTag(HtmlTextWriterTag.Ul);
            foreach (var file in changes)
            {
                writer.RenderBeginTag(HtmlTextWriterTag.Li);
                writer.AddAttribute(HtmlTextWriterAttribute.Href, "#" + file[0]);
                writer.RenderBeginTag(HtmlTextWriterTag.A);
                writer.Write(file[0]);
                writer.RenderEndTag();
                writer.RenderEndTag();
            }

            writer.RenderEndTag(); // Ul
        }

        private void RenderExecutiveSummaryTable(HtmlTextWriter writer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.H1);
            writer.Write("Build Execution report");
            writer.RenderEndTag();

            writer.AddAttribute(HtmlTextWriterAttribute.Id, "current");
            writer.RenderBeginTag(HtmlTextWriterTag.B);
            writer.Write("Execution Log:");
            writer.RenderEndTag();
            writer.Write(m_analyzer.ExecutionLogPath);
            writer.Write("<br>");

            RenderTableHeader(writer, string.Empty, s_summaryDifferenceHtmlTableColumns);

            // Table body
            writer.RenderBeginTag(HtmlTextWriterTag.Tbody);

            RenderSummaryTableRow(m_analyzer, writer, "Current");

            // Closing  table tag
            writer.RenderEndTag();

            writer.RenderEndTag();
        }

#endregion

        private void RenderPipExecutions(HtmlTextWriter writer, IEnumerable<(SummaryAnalyzer.ProcessPipSummary pipSummary1, SummaryAnalyzer.ProcessPipSummary pipSummary2)> summariesToReport, bool isRootChange = true)
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
                writer.AddAttribute(HtmlTextWriterAttribute.Id, pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
                writer.AddAttribute(HtmlTextWriterAttribute.Style, "display: " + (isRootChange ? " none;" : " block;"));
                writer.RenderBeginTag(HtmlTextWriterTag.Div);

                RenderTableHeader(
                    writer,
                    isRootChange ? string.Empty : m_analyzer.GetPipWorkingDirectory(pip),
                    isRootChange ? s_pipExecutionRootNodeHtmlTableColumns : s_pipExecutionHtmlTableColumns);
                writer.RenderBeginTag(HtmlTextWriterTag.Tbody);
                RenderPipExecutionSummaryTableRow(writer, pipSummary, missDisplayReason);
                writer.RenderEndTag();
                writer.RenderEndTag();  // Closing table tag

                // Add lists of changes in two colums
                writer.AddAttribute(HtmlTextWriterAttribute.Class, "row");
                writer.RenderBeginTag(HtmlTextWriterTag.Div);
                writer.AddAttribute(HtmlTextWriterAttribute.Class, "column");
                writer.RenderBeginTag(HtmlTextWriterTag.Div);
                RenderPipArtifactChangesColumn(writer, "Environment Changes", environmentChanges, environmentMissing);
                RenderPipArtifactChangesColumn(writer, "File Changes", fileArtifactChanges, fileArtifactMissing);
                RenderPipArtifactChangesColumn(writer, "Observed input changes", observedInputsChanges, observedInputsMissing);
                writer.RenderEndTag();  // Closing div=column
                RenderPipOutputsColumn(writer, pip.FileOutputs); // Outputs
                writer.RenderEndTag();  // Closing div=row

                // Finally render critical path and tool status
                if (pipSummary.ExecutedDependentProcessCount > 0)
                {
                    RenderCriticalPath(writer, pipSummary.CriticalPath);
                }

                writer.RenderEndTag();  // Closing div
                writer.Write("<br>");
            }
        }

        private void RenderPipExecutionButton(HtmlTextWriter writer, SummaryAnalyzer.ProcessPipSummary pipSummary)
        {
            var pip = pipSummary.Pip;
            writer.AddAttribute(HtmlTextWriterAttribute.Id, "pip" + pip.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
            writer.AddAttribute(HtmlTextWriterAttribute.Type, "button");
            writer.AddAttribute(
                HtmlTextWriterAttribute.Onclick,
                @"toggleMe('" + pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture) + "')");
            writer.RenderBeginTag(HtmlTextWriterTag.Button);
            var elapsedTime = pipSummary.CriticalPath.Node.IsValid
                ? pipSummary.CriticalPath.Time
                : m_analyzer.GetPipElapsedTime(pip);
            var executedPipsString = m_analyzer.IsPipFailed(pipSummary.Pip.PipId)
                ? @" <span style=""color: #FF6347;"">Executed Pips = </span>"
                : @" <span style=""color: #8FBC8F;"">Executed Pips = </span>";
            writer.Write(
                m_analyzer.GetPipWorkingDirectory(pip) + executedPipsString +
                pipSummary.ExecutedDependentProcessCount +
                @" <span style=""color: #8FBC8F;"">critPath =</span>" + elapsedTime.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.RenderEndTag();
        }

        private void RenderCriticalPath(HtmlTextWriter writer, BuildXL.Execution.Analyzer.Analyzer.NodeAndCriticalPath nodeAndCriticalPath)
        {
            var toolStats = new ConcurrentDictionary<PathAtom, TimeSpan>();
            RenderTableHeader(writer, "Critical Path : Calculated using wall time duration of each dependent pip", s_criticalPathHtmlTableColumns);
            writer.RenderBeginTag(HtmlTextWriterTag.Tbody);
            while (true)
            {
                Pip pip = m_analyzer.GetPipByPipId(new PipId(nodeAndCriticalPath.Node.Value));
                var process = pip as Process;

                var elapsed = m_analyzer.GetElapsed(nodeAndCriticalPath.Node);
                var kernelTime = m_analyzer.GetPipKernelTime(pip);
                var userTime = m_analyzer.GetPipUserTime(pip);

                writer.RenderBeginTag(HtmlTextWriterTag.Tr);

                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write(ToSeconds(elapsed));
                writer.RenderEndTag();

                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write(ToSeconds(kernelTime));
                writer.RenderEndTag();

                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write(ToSeconds(userTime));
                writer.RenderEndTag();

                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                string pipDescription;
                if (process != null)
                {
                    toolStats.AddOrUpdate(m_analyzer.GetPipToolName(process), elapsed, (k, v) => v + elapsed);
                    pipDescription = m_analyzer.GetPipDescriptionName(pip);
                    if (m_analyzer.IsPipReferencedInCriticalPath(process))
                    {
                        writer.AddAttribute(HtmlTextWriterAttribute.Href, "#" + pip.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
                        writer.RenderBeginTag(HtmlTextWriterTag.A);
                    }

                    writer.Write(pipDescription);
                    if (m_analyzer.IsPipReferencedInCriticalPath(process))
                    {
                        writer.RenderEndTag();
                    }
                }
                else
                {
                    pipDescription = m_analyzer.GetPipDescription(pip);
                    pipDescription = pipDescription.Replace('<', ' ').Replace('>', ' ');
                    writer.Write(pipDescription);
                }

                writer.RenderEndTag();

                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                var typeOrDir = process != null ? m_analyzer.GetPipWorkingDirectory(process) : pip.PipType.ToString();
                writer.Write(typeOrDir);
                writer.RenderEndTag();
                writer.RenderEndTag(); // tr

                if (!nodeAndCriticalPath.Next.IsValid)
                {
                    break;
                }

                nodeAndCriticalPath = m_analyzer.GetImpactPath(nodeAndCriticalPath.Next);
            }

            writer.RenderEndTag();
            writer.RenderEndTag();

            // Table of tools stats for this critical path
            RenderTableHeader(writer, "Tools stats", new List<string> { "Seconds", "Tool" });
            writer.RenderBeginTag(HtmlTextWriterTag.Tbody);
            foreach (var toolStatEntry in toolStats.OrderByDescending(kvp => kvp.Value))
            {
                writer.RenderBeginTag(HtmlTextWriterTag.Tr);
                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write(ToSeconds(toolStatEntry.Value));
                writer.RenderEndTag();

                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write(m_analyzer.GetPathAtomToString(toolStatEntry.Key));
                writer.RenderEndTag();
                writer.RenderEndTag(); // tr
            }

            writer.RenderEndTag();
            writer.RenderEndTag();
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

        private static void RenderPipArtifactChangesColumn(HtmlTextWriter writer, string header, List<List<string>> changes, List<List<string>> missing)
        {
            // Only if there are any changes
            if (changes.Count + missing.Count == 0)
            {
                return;
            }

            writer.RenderBeginTag(HtmlTextWriterTag.H3);
            writer.Write(header);
            writer.RenderEndTag(); // H3
            writer.RenderBeginTag(HtmlTextWriterTag.Ul);
            foreach (var file in changes)
            {
                writer.RenderBeginTag(HtmlTextWriterTag.Li);
                writer.AddAttribute(HtmlTextWriterAttribute.Href, "#" + file[0]);
                writer.RenderBeginTag(HtmlTextWriterTag.A);
                writer.Write(file[0]);
                writer.RenderEndTag();
                writer.RenderEndTag();
            }

            foreach (var file in missing)
            {
                writer.RenderBeginTag(HtmlTextWriterTag.Li);
                writer.AddAttribute(HtmlTextWriterAttribute.Href, "#" + file[0]);
                writer.RenderBeginTag(HtmlTextWriterTag.A);
                writer.Write("Dependency removed: " + file[0]);
                writer.RenderEndTag();
                writer.RenderEndTag();
            }

            writer.RenderEndTag(); // Ul
        }

        private void RenderPipOutputsColumn(HtmlTextWriter writer, ReadOnlyArray<FileArtifactWithAttributes> fileOutputs)
        {
            writer.AddAttribute(HtmlTextWriterAttribute.Class, "column");
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            writer.RenderBeginTag(HtmlTextWriterTag.H3);
            writer.Write("Outputs");
            writer.RenderEndTag(); // H3
            writer.RenderBeginTag(HtmlTextWriterTag.Ul);
            foreach (var output in fileOutputs)
            {
                writer.RenderBeginTag(HtmlTextWriterTag.Li);
                writer.Write(m_analyzer.GetAbsolutePathToString(output.Path));
                writer.RenderEndTag();
            }

            writer.RenderEndTag(); // Ul
            writer.RenderEndTag(); // Closing div=column of Outputs
        }

        private void RenderPipExecutionSummaryTableRow(HtmlTextWriter writer, SummaryAnalyzer.ProcessPipSummary pipSummary, string missReason)
        {
            var pip = pipSummary.Pip;
            writer.RenderBeginTag(HtmlTextWriterTag.Tr);

            // Pip name
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(m_analyzer.GetPipDescriptionName(pip));
            writer.RenderEndTag();

            // Start time
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            var executionStart = m_analyzer.GetPipStartTime(pip);
            var executionStartText = executionStart.Equals(DateTime.MinValue) ? "-" : executionStart.ToString("h:mm:ss.ff", CultureInfo.InvariantCulture);
            writer.Write(executionStartText);
            writer.RenderEndTag();

            // Duration
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(m_analyzer.GetPipElapsedTime(pip).ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.RenderEndTag();

            // Time kernel
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            var kernelTime = pipSummary.CriticalPath.Node.IsValid ? pipSummary.CriticalPath.KernelTime : m_analyzer.GetPipKernelTime(pip);
            writer.Write(kernelTime.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.RenderEndTag();

            // Time user
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            var userTime = pipSummary.CriticalPath.Node.IsValid ? pipSummary.CriticalPath.UserTime : m_analyzer.GetPipUserTime(pip);
            writer.Write(userTime.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture));
            writer.RenderEndTag();

            // Pip stable hash
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(pip.Provenance.SemiStableHash.ToString("X", CultureInfo.InvariantCulture));
            writer.RenderEndTag();

            // Reason for execution
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.AddAttribute(HtmlTextWriterAttribute.Href, "https://www.1eswiki.com/wiki/Domino_execution_analyzer#Reasons_for_process_Pip_Execution_section");
            writer.RenderBeginTag(HtmlTextWriterTag.A);
            writer.Write(missReason);
            writer.RenderEndTag();
            writer.RenderEndTag();

            // Invalidated pips
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(
                pipSummary.CriticalPath.Node.IsValid ? pipSummary.ExecutedDependentProcessCount.ToString(CultureInfo.InvariantCulture) : "-");
            writer.RenderEndTag();
            writer.RenderEndTag(); // Closing Tr
        }

        private static void RenderFileAcronym(HtmlTextWriter writer, string fileName, string abbr)
        {
            writer.AddAttribute(HtmlTextWriterAttribute.Title, fileName);
            writer.RenderBeginTag(HtmlTextWriterTag.Acronym);
            writer.Write(abbr);
            writer.RenderEndTag();
        }

        private void RenderArtifactsSummary(HtmlTextWriter writer, SummaryAnalyzer analyzer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            writer.RenderBeginTag(HtmlTextWriterTag.H2);
            writer.AddAttribute(HtmlTextWriterAttribute.Type, "button");
            writer.AddAttribute(HtmlTextWriterAttribute.Onclick, @"toggleMe('artifactSummarySection')");
            writer.RenderBeginTag(HtmlTextWriterTag.Button);
            writer.Write("Summary");
            writer.RenderEndTag();

            writer.RenderEndTag();

            writer.RenderBeginTag(HtmlTextWriterTag.P);
            writer.Write("This section compares the summary of the distinct Pip artifacts between ");
            RenderFileAcronym(writer, m_analyzer.ComparedFilePath, "current");
            writer.Write(" and ");
            RenderFileAcronym(writer, analyzer.ComparedFilePath, "previous");
            writer.Write(" logs, showing only those artifacts with distinct hash, missing or different value for environment variables.");
            writer.RenderEndTag();

            writer.AddAttribute(HtmlTextWriterAttribute.Id, "artifactSummarySection");
            writer.AddAttribute(HtmlTextWriterAttribute.Style, "display: block;");
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            RenderEnvironmentSummaryTable(writer, analyzer);
            RenderFileArtifactSummaryTable(writer, analyzer);
            RenderObservedInputsSummaryTable(writer, analyzer);
            RenderDirectoryMembershipSummaryTable(writer, analyzer);
            RenderDirectorydependencySummaryTable(writer);
            writer.RenderEndTag();

            writer.RenderEndTag();
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

        private static void RenderTableHeader(HtmlTextWriter writer, string caption, List<string> columns)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Table);

            writer.RenderBeginTag(HtmlTextWriterTag.Caption);
            writer.RenderBeginTag(HtmlTextWriterTag.B);

            // Table caption
            writer.Write(caption);
            writer.RenderEndTag();
            writer.RenderEndTag();

            // Table header
            writer.RenderBeginTag(HtmlTextWriterTag.Thead);

            // Table columns row
            writer.RenderBeginTag(HtmlTextWriterTag.Tr);
            foreach (var column in columns)
            {
                writer.RenderBeginTag(HtmlTextWriterTag.Th);
                writer.Write(column);
                writer.RenderEndTag();
            }

            writer.RenderEndTag();
            writer.RenderEndTag();
        }

        private void RenderTableRow(HtmlTextWriter writer, IEnumerable<List<string>> rows)
        {
            var rowCount = 0;
            foreach (var row in rows)
            {
                if (rowCount > m_analyzer.MaxDifferenceReportCount * 2 && !m_changesToReferenceList.Contains(row[0]))
                {
                    // limit number of rows changes, allow those referenced in executed Pips
                    continue;
                }

                writer.RenderBeginTag(HtmlTextWriterTag.Tr);
                foreach (var column in row)
                {
                    if (m_changesToReferenceList.Contains(column))
                    {
                        writer.AddAttribute(HtmlTextWriterAttribute.Id, column);
                    }

                    writer.RenderBeginTag(HtmlTextWriterTag.Td);
                    writer.Write(column);
                    writer.RenderEndTag();
                }

                rowCount++;
                writer.RenderEndTag();
            }
        }

        private void RenderDifferenceSummaryTable(
            HtmlTextWriter writer,
            string tableHeader,
            List<string> columnNames,
            List<List<string>> difference,
            List<List<string>> missing)
        {
            if (difference.Count + missing.Count > AddScrollToTableRowCountLimit)
            {
                // add a div to scroll when the count of rows is over the limit
                writer.AddAttribute(HtmlTextWriterAttribute.Style, "height: 500px; overflow-y: auto");
                writer.RenderBeginTag(HtmlTextWriterTag.Div);
            }

            RenderTableHeader(writer, tableHeader, columnNames);

            // Table body
            writer.RenderBeginTag(HtmlTextWriterTag.Tbody);

            // Add each row of changes
            RenderTableRow(writer, difference);

            if (missing.Count > 0)
            {
                // TODO: will consider making this a different table
                // Add any missing items
                writer.RenderBeginTag(HtmlTextWriterTag.Tr);
                writer.AddAttribute(HtmlTextWriterAttribute.Colspan, missing.Count.ToString(CultureInfo.InvariantCulture));
                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write("Removed Dependencies");
                writer.RenderEndTag();
                writer.RenderEndTag();

                RenderTableRow(writer, missing);
            }

            writer.RenderEndTag();

            // Closing table tag
            writer.RenderEndTag();

            if (difference.Count + missing.Count > AddScrollToTableRowCountLimit)
            {
                // Closing div tag if any
                writer.RenderEndTag();
            }
        }

        private void RenderEnvironmentSummaryTable(HtmlTextWriter writer, SummaryAnalyzer analyzer)
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

        private void RenderFileArtifactSummaryTable(HtmlTextWriter writer, SummaryAnalyzer analyzer)
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

        private void RenderObservedInputsSummaryTable(HtmlTextWriter writer, SummaryAnalyzer analyzer)
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

        private void RenderDirectoryMembershipSummaryTable(HtmlTextWriter writer, SummaryAnalyzer analyzer)
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
                writer.AddAttribute(HtmlTextWriterAttribute.Style, "height: 500px; overflow-y: auto");
                writer.RenderBeginTag(HtmlTextWriterTag.Div);
            }

            var writeCount = 0;
            RenderTableHeader(writer, "Directory membership difference", s_directoryMembershipDifferenceHtmlTableColumns);

            // Table body
            writer.RenderBeginTag(HtmlTextWriterTag.Tbody);

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

            writer.RenderEndTag();

            // Closing table tag
            writer.RenderEndTag();

            if (directoryMembershipNotInTwo.Count + directoryMembershipNotInOne.Count > AddScrollToTableRowCountLimit)
            {
                // Closing div tag if any
                writer.RenderEndTag();
            }
        }

        private static void RenderDirectoryEnumeration(HtmlTextWriter writer, string directory, List<string> rows, bool current)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Tr);
            writer.AddAttribute(HtmlTextWriterAttribute.Rowspan, (rows.Count + 1).ToString(CultureInfo.InvariantCulture));
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(directory);
            writer.RenderEndTag();
            writer.RenderEndTag();

            foreach (var row in rows)
            {
                var firstColum = current ? row : "no";
                var secondColumn = current ? "no" : row;
                writer.RenderBeginTag(HtmlTextWriterTag.Tr);
                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write(firstColum);
                writer.RenderEndTag();
                writer.RenderBeginTag(HtmlTextWriterTag.Td);
                writer.Write(secondColumn);
                writer.RenderEndTag();
                writer.RenderEndTag();
            }
        }

        private static void RenderDirectorydependencySummaryTable(HtmlTextWriter writer)
        {
            // TODO: fill the data
            writer.AddAttribute(HtmlTextWriterAttribute.Style, "height: 500px; overflow-y: auto");
            writer.RenderBeginTag(HtmlTextWriterTag.Div);
            RenderTableHeader(writer, "Directory dependency summary", s_observedInputsDifferenceHtmlTableColumns);

            // Table body
            writer.RenderBeginTag(HtmlTextWriterTag.Tbody);

            // TODO: fill the data
            // Add each row of changes
            writer.RenderEndTag();
            writer.RenderEndTag();
            writer.RenderEndTag();
        }

        private void RenderExecutiveSummaryTable(HtmlTextWriter writer, SummaryAnalyzer analyzer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.H1);

            // Add a link to wiki documentation.
            writer.AddAttribute(HtmlTextWriterAttribute.Href, "https://www.1eswiki.com/wiki/Domino_execution_analyzer#HTML_Diff_Report_output");
            writer.RenderBeginTag(HtmlTextWriterTag.A);
            writer.Write("Execution log compare report");
            writer.RenderEndTag();
            writer.RenderEndTag();

            writer.AddAttribute(HtmlTextWriterAttribute.Id, "current");
            writer.RenderBeginTag(HtmlTextWriterTag.B);
            writer.Write("Current Execution Log:");
            writer.RenderEndTag();
            writer.Write(m_analyzer.ExecutionLogPath);
            writer.Write("<br>");

            writer.AddAttribute(HtmlTextWriterAttribute.Id, "previous");
            writer.RenderBeginTag(HtmlTextWriterTag.B);
            writer.Write("Previous Execution Log:");
            writer.RenderEndTag();
            writer.Write(analyzer.ExecutionLogPath);
            writer.Write("<br>");

            // Check difference in the salt flags which is a global change that impacts all fingerprints
            // Diplay this in red to show the global impact
            if (!m_analyzer.CompareSaltsEquals(analyzer))
            {
                writer.RenderBeginTag(HtmlTextWriterTag.H3);
                writer.AddAttribute(HtmlTextWriterAttribute.Type, "button");
                writer.AddAttribute(HtmlTextWriterAttribute.Onclick, @"toggleMe('Global_fingerprint_change')");
                writer.AddAttribute(HtmlTextWriterAttribute.Style, "background-color:#FF6347");
                writer.RenderBeginTag(HtmlTextWriterTag.Button);
                writer.Write("Global fingerprint change");
                writer.RenderEndTag();
                writer.RenderEndTag();

                writer.AddAttribute(HtmlTextWriterAttribute.Id, "Global_fingerprint_change");
                writer.AddAttribute(HtmlTextWriterAttribute.Style, "display: none;");
                writer.RenderBeginTag(HtmlTextWriterTag.Div);

                var saltDiffs = m_analyzer.GetSaltsDifference(analyzer);
                writer.RenderBeginTag(HtmlTextWriterTag.Ul);

                foreach (var diff in saltDiffs)
                {
                    writer.RenderBeginTag(HtmlTextWriterTag.Li);
                    writer.Write(diff);
                    writer.RenderEndTag();
                }

                writer.RenderEndTag(); // Ul
                writer.RenderEndTag(); // Closing div
            }

            RenderTableHeader(writer, string.Empty, s_summaryDifferenceHtmlTableColumns);

            // Table body
            writer.RenderBeginTag(HtmlTextWriterTag.Tbody);

            RenderSummaryTableRow(m_analyzer, writer, "Current");
            RenderSummaryTableRow(analyzer, writer, "Previous");
            writer.RenderEndTag();

            // Closing  table tag
            writer.RenderEndTag();
        }

        private const float LowHitRateLimit = 75;

        private static void RenderSummaryTableRow(SummaryAnalyzer analyzer, HtmlTextWriter writer, string fileReference)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Tr);

            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            RenderFileAcronym(writer, analyzer.ExecutionLogPath, fileReference);
            writer.RenderEndTag();

            // Process pips
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(analyzer.GetProcessPipCount());
            writer.RenderEndTag();

            // Executed pips
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(analyzer.GetExecutedProcessPipCount());
            writer.RenderEndTag();

            // Cache hit
            float hitRate = analyzer.GetProcessPipHitRate();
            var backGroundColor = "#00FF66";
            if (hitRate < LowHitRateLimit)
            {
                backGroundColor = "#FF6347";
            }

            writer.AddAttribute(HtmlTextWriterAttribute.Bgcolor, backGroundColor);
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(hitRate.ToString("F", CultureInfo.InvariantCulture));
            writer.RenderEndTag();

            // Failed Pips
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(analyzer.GetFailedProcessPipCount());
            writer.RenderEndTag();

            // Output files produced
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(analyzer.GetProducedFileCount());
            writer.RenderEndTag();

            // Output files from cache
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(analyzer.GetCachedFileCount());
            writer.RenderEndTag();

            // Output files up to date
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(analyzer.GetUpToDateFileCount());
            writer.RenderEndTag();

            // uncacheable pips
            writer.RenderBeginTag(HtmlTextWriterTag.Td);
            writer.Write(analyzer.GetUncacheableProcessPipCount());
            writer.RenderEndTag();

            writer.RenderEndTag();
        }

        private static void RenderScript(HtmlTextWriter writer)
        {
            writer.RenderBeginTag(HtmlTextWriterTag.Script);
            writer.WriteLine(@"function toggleMe(a) {
                var e = document.getElementById(a);
                if (!e)return true;
                if (e.style.display === ""none"") {
                    e.style.display = ""block"";
                } else {
                    e.style.display = ""none"";
                }
                return true;
                }");
            writer.RenderEndTag();
        }

        /// <summary>
        /// Render the stylesheet (.css)
        /// </summary>
        private static void RenderStylesheet(HtmlTextWriter writer)
        {
            writer.AddAttribute("type", "text/css");
            writer.RenderBeginTag(HtmlTextWriterTag.Style);
            writer.Write(@"    
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
            writer.RenderEndTag();
        }

        private static string ToSeconds(TimeSpan time)
        {
            return Math.Round(time.TotalSeconds, 3).ToString(CultureInfo.InvariantCulture);
        }
    }
}
#endif
