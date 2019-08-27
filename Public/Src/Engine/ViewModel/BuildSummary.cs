// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    /// to enable Azure DevOps optimized UI
    /// </remarks>
    public class BuildSummary
    {
        /// <summary>
        /// The path where to store the summary
        /// </summary>
        private string m_filePath;

        /// <summary>
        /// This is a model of the tree rendering of the perf regions
        /// </summary>
        public PerfTree DurationTree { get; set; }

        /// <nodoc />
        public CriticalPathSummary CriticalPathSummary { get; } = new CriticalPathSummary();

        /// <nodoc />
        public CacheSummary CacheSummary { get; } = new CacheSummary();

        /// <nodoc />
        public List<BuildSummaryPipDiagnostic> PipErrors { get; } = new List<BuildSummaryPipDiagnostic>();

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

                if (PipErrors.Count >0)
                {
                    writer.WriteHeader("Pip Errors");
                    foreach (var error in PipErrors)
                    {
                        writer.WriteLineRaw("");
                        error.RenderMarkDown(writer);
                    }
                }
            }

            return m_filePath;
        }
        
        private string ExtractDuration(TimeSpan timespan)
        {
            return timespan.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms";
        }
    }
}
