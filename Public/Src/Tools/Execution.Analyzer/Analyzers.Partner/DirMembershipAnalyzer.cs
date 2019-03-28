// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        private const string OutputFileArgName = "outputfile";

        public Analyzer InitializeDirMembershipAnalyzer()
        {
            string outputFilePath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals(OutputFileArgName, StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw Error("Output file must be specified with /{0}", OutputFileArgName);
            }

            return new DirMembershipAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteDirMembershipHelp(HelpWriter writer)
        {
            writer.WriteBanner("Directory Membership Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.DirMembership), "Generates a text file containing directory memberships as discovered at the time its hash is calculated");
            writer.WriteOption(OutputFileArgName, "Required. The file where to write the results", shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to generate fingerprint text file
    /// </summary>
    internal sealed class DirMembershipAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the fingerprint file
        /// </summary>
        public string OutputFilePath;

        private sealed class DirData
        {
            public readonly string Path;
            public readonly bool IsStatic;
            public readonly bool IsSearchPath;
            public List<string> Files = new List<string>();
            public readonly PipId PipId;
            public readonly string EnumeratePatternRegex;

            public DirData(string path, DirectoryMembershipHashedEventData eventData)
            {
                Path = path;
                IsStatic = eventData.IsStatic;
                PipId = eventData.PipId;
                IsSearchPath = eventData.IsSearchPath;
                EnumeratePatternRegex = eventData.EnumeratePatternRegex;
            }

            public IEnumerable<string> GetFlags()
            {
                yield return IsStatic ? "static" : "dynamic";

                if (IsSearchPath)
                {
                    yield return "search path";
                }
            }
        }

        private readonly List<DirData> m_dirData = new List<DirData>();

        public DirMembershipAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            m_dirData.Sort((x, y) => string.Compare(x.Path, y.Path, StringComparison.OrdinalIgnoreCase));

            using (var outputStream = File.Create(OutputFilePath, bufferSize: 64 << 10 /* 64 KB */))
            {
                using (var writer = new StreamWriter(outputStream))
                {
                    foreach (var dirData in m_dirData.OrderBy(item => item.Path + (item.PipId.IsValid ? CachedGraph.PipGraph.GetPipFromPipId(item.PipId).SemiStableHash.ToString(CultureInfo.InvariantCulture) : string.Empty)))
                    {
                        writer.WriteLine(
                            "Directory: {0} ({1}{2})",
                            dirData.Path,
                            string.Join(" | ", dirData.GetFlags()),
                            dirData.PipId.IsValid ? " " + CachedGraph.PipGraph.GetPipFromPipId(dirData.PipId).GetDescription(PipGraph.Context) : string.Empty);

                        writer.WriteLine("    EnumeratePatternRegex: {0}", dirData.EnumeratePatternRegex);
                        dirData.Files.Sort(StringComparer.OrdinalIgnoreCase);

                        foreach (var file in dirData.Files)
                        {
                            writer.WriteLine("    Member: {0}", file);
                        }

                        writer.WriteLine();
                    }
                }
            }

            return 0;
        }

        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            var dirData = new DirData(data.Directory.ToString(CachedGraph.Context.PathTable), data);
            dirData.Files.Capacity = data.Members.Count;
            foreach (var file in data.Members)
            {
                dirData.Files.Add(file.GetName(CachedGraph.Context.PathTable).ToString(CachedGraph.Context.StringTable));
            }

            m_dirData.Add(dirData);
        }
    }
}
