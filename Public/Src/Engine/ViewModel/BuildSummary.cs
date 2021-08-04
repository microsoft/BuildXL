// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Text;

namespace BuildXL.ViewModel
{
    /// <summary>
    /// This class collects build information during the build to be displayed in Azure DevOps extensions page as a MarkDown
    /// </summary>
    /// <remarks>
    /// This class is only expected to be instantiated when the users passes /ado on the commandline
    /// to enable Azure DevOps optimized UI.
    /// </remarks>
    public class BuildSummary
    {
        /// <summary>
        /// The path where to store the summary
        /// </summary>
        private readonly string m_filePath;
        private readonly List<BuildSummaryPipDiagnostic> m_pipErrors = new();
        private bool m_isPipErrorsTruncated;

        /// <summary>
        /// This is a model of the tree rendering of the perf regions
        /// </summary>
        public PerfTree DurationTree { get; set; }

        /// <nodoc />
        public CriticalPathSummary CriticalPathSummary { get; } = new CriticalPathSummary();

        /// <nodoc />
        public CacheSummary CacheSummary { get; } = new CacheSummary();

        /// <nodoc />
        public IReadOnlyCollection<BuildSummaryPipDiagnostic> PipErrors => m_pipErrors;

        /// <nodoc />
        public BuildSummary(string filePath)
        {
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            m_filePath = filePath;
        }

        /// <summary>
        /// This will render the markdown to the predetermined file.
        /// </summary>
        /// <remarks>
        /// Caller is responsible for exception handling
        /// </remarks>
        public string RenderMarkdown()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_filePath));
            using (var writer = new MarkDownWriter(m_filePath))
            {
                writer.WriteHeader("Stats");

                writer.StartTable();

                if (DurationTree != null)
                {
                    writer.StartDetailedTableSummary("Build Duration", DurationTree.Duration.MakeFriendly());
                    var builder = new StringBuilder();
                    DurationTree.Write(builder, 0, DurationTree.Duration);
                    writer.WritePre(builder.ToString());
                    writer.EndDetailedTableSummary();
                }

                CacheSummary.RenderMarkdown(writer);

                CriticalPathSummary.RenderMarkdown(writer);

                writer.EndTable();

                if (PipErrors.Count > 0)
                {
                    writer.WriteHeader("Pip Errors");

                    if (m_isPipErrorsTruncated)
                    {
                        writer.StartDetails($"Note: The list is collapsed for UI performance reasons. Displaying the first {m_pipErrors.Count} errors.");
                    }
                    else
                    {
                        writer.StartDetails("Note: The list is collapsed for UI performance reasons.");
                    }

                    foreach (var error in PipErrors)
                    {
                        writer.WriteLineRaw("");
                        error.RenderMarkDown(writer);
                    }

                    writer.EndDetails();
                }
            }

            return m_filePath;
        }

        /// <summary>
        /// Adds a pip error to the build summary if it contains fewer than specified number of errors.
        /// </summary>
        /// <remarks>
        /// The method is not thread-safe.
        /// </remarks>
        public void AddPipError(BuildSummaryPipDiagnostic pipError, int maxErrorsToInclude)
        {
            if (m_pipErrors.Count <= maxErrorsToInclude)
            {
                m_pipErrors.Add(pipError);
            }
            else
            {
                m_isPipErrorsTruncated = true;
            }
        }
    }
}
