// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;
using Process = BuildXL.Pips.Operations.Process;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeWinIdeDependencyAnalyzer()
        {
            string outputFile = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
            }

            if (string.IsNullOrEmpty(outputFile))
            {
                throw Error("Missing required argument 'outputFile'");
            }

            return new WinIdeDependencyAnalyzer(GetAnalysisInput(), outputFile);
        }

        private static void WriteWinIdeDependencyAnalyzeHelp(HelpWriter writer)
        {
            writer.WriteBanner("WinIde Dependency Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.WinIdeDependency), "Generates a dependency report for WinIde that contains project dependencies");
            writer.WriteOption(
                "outputFile",
                "Required. The file to produce the report to. If the extension ends with '.json' a json format will be emitted, else a custom text format.",
                shortName: "o");
        }
    }

    /// <summary>
    /// Analyzer used to compare two execution logs and report on high impact differences that produce cache invalidation.
    /// The normal usage is to compare Build-i to Build-(i-1)
    /// </summary>
    internal sealed class WinIdeDependencyAnalyzer : Analyzer
    {
        private readonly string m_outputFilePath;

        public WinIdeDependencyAnalyzer(AnalysisInput input, string outputFilePath)
            : base(input)
        {
            m_outputFilePath = outputFilePath;
        }

        public override int Analyze()
        {
            var processes = HydrateAllProcesses();
            var sealDirectoryToProjectMap = GetSealDirectoryToProjectMap(processes);
            var projects = GetProjects(processes, sealDirectoryToProjectMap);

            if (Path.GetExtension(m_outputFilePath) == ".json")
            {
                if (!TryWriteToJsonFormat(projects, m_outputFilePath))
                {
                    return 1;
                }
            }
            else
            {
                if (!TryWriteToCustomTextFormat(projects, m_outputFilePath))
                {
                    return 1;
                }
            }

            return 0;
        }

        private Process[] HydrateAllProcesses()
        {
            return Measure(
                "Loading all processes",
                () =>
                {
                    var pipReferences = PipGraph.RetrievePipReferencesOfType(PipType.Process).ToArray();
                    var processes = new Process[pipReferences.Length];
                    Parallel.For(
                        0,
                        pipReferences.Length,
                        i =>
                        {
                            processes[i] = (Process)pipReferences[i].HydratePip();
                        });
                    return processes;
                });
        }

        private Dictionary<PipId, AbsolutePath> GetSealDirectoryToProjectMap(IEnumerable<Process> allProcesses)
        {
            return Measure(
                "Linking processes to sealed directories",
                () =>
                {
                    var sealDirectoryToProjectMap = new Dictionary<PipId, AbsolutePath>();
                    foreach (var process in allProcesses)
                    {
                        var processFolder = GetProjectFolder(process);
                        foreach (var dependent in PipGraph.RetrievePipReferenceImmediateDependents(process.PipId, PipType.SealDirectory))
                        {
                            if (processFolder == GetProjectFolder(dependent.HydratePip()))
                            {
                                if (!sealDirectoryToProjectMap.ContainsKey(dependent.PipId))
                                {
                                    sealDirectoryToProjectMap.Add(dependent.PipId, processFolder);
                                }
                            }
                        }
                    }

                    return sealDirectoryToProjectMap;
                });
        }

        private IReadOnlyList<ProjectData> GetProjects(
            IEnumerable<Process> allProcesses,
            Dictionary<PipId, AbsolutePath> sealedDirectoriesFromWrappers)
        {
            return Measure(
                "Constructing Project Data",
                () =>
                {
                    var groups = allProcesses.GroupBy(p => p.Provenance.Token.Path).ToArray();
                    var projects = new ProjectData[groups.Length];
                    Parallel.For(
                        0,
                        groups.Length,
                        i =>
                        {
                            var specFileGroupedProcesses = groups[i];
                            var projectFolder = specFileGroupedProcesses.Key.GetParent(PathTable);
                            var project = new ProjectData(projectFolder);

                            foreach (var process in specFileGroupedProcesses)
                            {
                                foreach (var dependency in PipGraph.RetrievePipReferenceImmediateDependencies(process.PipId, PipType.Process))
                                {
                                    project.AddProjectDependency(GetProjectFolder(dependency.HydratePip()));
                                }

                                foreach (var dependency in PipGraph.RetrievePipReferenceImmediateDependencies(process.PipId, PipType.SealDirectory))
                                {
                                    AbsolutePath projectDependency;
                                    if (sealedDirectoriesFromWrappers.TryGetValue(dependency.PipId, out projectDependency))
                                    {
                                        project.AddProjectDependency(projectDependency);
                                    }
                                    else
                                    {
                                        project.AddSourceFolderDependency(GetProjectFolder(dependency.HydratePip()));
                                    }
                                }
                            }

                            projects[i] = project;
                        });

                    return projects;
                });
        }

        private static bool WriteFileMeasured(string title, string outputFile, Action<TextWriter> writeContents)
        {
            return Measure(
                title,
                () =>
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
                        using (var textWriter = new StreamWriter(outputFile, append: false, encoding: Encoding.UTF8, bufferSize: 64 << 10 /* 64 KB */))
                        {
                            writeContents(textWriter);
                            return true;
                        }
                    }
                    catch (IOException e)
                    {
                        Console.Error.WriteLine("Error writing to file: {0}" + e.Message);
                        return false;
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Console.Error.WriteLine("Error writing to file: {0}" + e.Message);
                        return false;
                    }
                });
        }

        private bool TryWriteToCustomTextFormat(IReadOnlyList<ProjectData> projects, string outputFile)
        {
            return WriteFileMeasured(
                "Writing result file (txt)",
                outputFile,
                writer =>
                {
                    foreach (var project in projects)
                    {
                        writer.WriteLine(project.ProjectFolder.ToString(PathTable));
                        foreach (var projectDependency in project.ProjectDependencies)
                        {
                            writer.WriteLine("#" + projectDependency.ToString(PathTable));
                        }
                        foreach (var sourceDependency in project.SourceDependencies)
                        {
                            writer.WriteLine("@" + sourceDependency.ToString(PathTable));
                        }
                    }
                });
        }

        private bool TryWriteToJsonFormat(IReadOnlyList<ProjectData> projects, string outputFile)
        {
            return WriteFileMeasured(
                "Writing outputProcess Data (json)",
                outputFile,
                textWriter =>
                {
                    using (var writer = new JsonTextWriter(textWriter))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.IndentChar = '\t';
                        writer.Indentation = 1;

                        writer.WriteStartObject();
                        writer.WritePropertyName("projects");
                        writer.WriteStartArray();
                        foreach (var project in projects)
                        {
                            writer.WriteStartObject();
                            writer.WritePropertyName("folder");
                            writer.WriteValue(project.ProjectFolder.ToString(PathTable));

                            writer.WritePropertyName("projectDependencies");
                            writer.WriteStartArray();
                            writer.Formatting = Formatting.None;
                            foreach (var projectDependency in project.ProjectDependencies)
                            {
                                writer.WriteValue(projectDependency.ToString(PathTable));
                            }
                            writer.WriteEndArray();
                            writer.Formatting = Formatting.Indented;

                            writer.WritePropertyName("sourceDependencies");
                            writer.WriteStartArray();
                            writer.Formatting = Formatting.None;
                            foreach (var sourceDependency in project.SourceDependencies)
                            {
                                writer.WriteValue(sourceDependency.ToString(PathTable));
                            }
                            writer.WriteEndArray();
                            writer.Formatting = Formatting.Indented;

                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                });
        }

        private AbsolutePath GetProjectFolder(Pip process)
        {
            return process.Provenance.Token.Path.GetParent(PathTable);
        }

        private static T Measure<T>(string title, Func<T> doWork)
        {
            var stopWatch = Stopwatch.StartNew();
            Console.Write(title);
            Console.Write(" ...");
            try
            {
                return doWork();
            }
            finally
            {
                Console.WriteLine(" [{0}:{1:000}]", Math.Floor(stopWatch.Elapsed.TotalSeconds), stopWatch.Elapsed.Milliseconds);
            }
        }

        /// <summary>
        /// Represents an intermediate structure for WinIdeDependency analysis
        /// This represents a set of pips together in a folder.
        /// </summary>
        internal sealed class ProjectData
        {
            /// <nodoc />
            public ProjectData(AbsolutePath projectFolder)
            {
                ProjectFolder = projectFolder;
            }

            /// <summary>
            /// The folder of the project
            /// </summary>
            public AbsolutePath ProjectFolder { get; }

            private readonly HashSet<AbsolutePath> m_projectDependencies = new HashSet<AbsolutePath>();

            /// <summary>
            /// The folders of the projects this project depends on.
            /// </summary>
            public ReadOnlyHashSet<AbsolutePath> ProjectDependencies => new ReadOnlyHashSet<AbsolutePath>(m_projectDependencies);

            private readonly HashSet<AbsolutePath> m_sourceDependencies = new HashSet<AbsolutePath>();

            /// <summary>
            /// The folders where dependencies of sources (sealed directories) this project depends on.
            /// </summary>
            public ReadOnlyHashSet<AbsolutePath> SourceDependencies => new ReadOnlyHashSet<AbsolutePath>(m_sourceDependencies);

            /// <summary>
            /// Adds a project
            /// </summary>
            public void AddProjectDependency(AbsolutePath directory)
            {
                if (directory == ProjectFolder)
                {
                    return; // Skip adding dependencies to itself.
                }

                m_projectDependencies.Add(directory);
            }

            /// <summary>
            /// Adds a source folder
            /// </summary>
            public void AddSourceFolderDependency(AbsolutePath directory)
            {
                if (directory == ProjectFolder)
                {
                    return; // Skip adding dependencies to itself.
                }

                m_sourceDependencies.Add(directory);
            }
        }
    }
}
