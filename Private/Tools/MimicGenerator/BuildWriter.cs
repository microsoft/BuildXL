// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Writes out inputs and build files for a mimic'ed build
    /// </summary>
    public sealed class BuildWriter
    {
        private const string ResponseFileName = "args.rsp";

        private readonly string m_outputDirectory;
        private readonly bool m_writeInputFiles;
        private readonly BuildGraph m_buildGraph;
        private ConcurrentBigSet<int> m_sourceFilesWritten = new ConcurrentBigSet<int>();
        private readonly ILookup<string, Pip> m_specFileToPipsLookup;

        // Counter to give some progress while running
        private int m_processesEncountered;
        private int m_inputsWritten;
        private int m_inputsWithDefaultSize;

        private double m_inputScaleFactor;
        private bool m_ignoreResponseFiles;

        private readonly Language m_language;

        private ConcurrentBag<Tuple<string, string, int>> m_inputsToWrite = new ConcurrentBag<Tuple<string, string, int>>();

        public BuildWriter(string outputDirectory, bool writeInputFiles, double inputScaleFactor, BuildGraph buildGraph, bool ignoreResponseFiles, Language language)
        {
            m_outputDirectory = outputDirectory;
            m_writeInputFiles = writeInputFiles;
            m_inputScaleFactor = inputScaleFactor;
            m_buildGraph = buildGraph;
            m_specFileToPipsLookup = buildGraph.Pips.Values.ToLookup(pip => pip.Spec, StringComparer.Ordinal);
            m_ignoreResponseFiles = ignoreResponseFiles;
            m_language = language;
        }

        /// <summary>
        /// Writes out build files
        /// </summary>
        public bool WriteBuildFiles()
        {
            bool success = true;
            Console.WriteLine("Writing build files and inputs");

            Directory.CreateDirectory(m_outputDirectory);

            using (var textWriter = new StreamWriter(Path.Combine(m_outputDirectory, "stats.txt")))
            {
                m_buildGraph.OutputFileStats.Write(textWriter);
                m_buildGraph.SourceFileStats.Write(textWriter);
                m_buildGraph.PipDurationStats.Write(textWriter);
                m_buildGraph.BuildInterval.Write(textWriter);
                m_buildGraph.ProcessPipStats.Write(textWriter);
            }

            // Write out the cache config file
            System.IO.File.WriteAllText(
                Path.Combine(m_outputDirectory, "cacheConfig.json"), 
                GetEmbeddedResourceFile("Tool.MimicGenerator.Content.CacheConfig.json"));
            WriteBuildScript();

            var provider = new LanguageProvider(m_language);

            using (var configWriter = provider.CreateConfigWriter(Path.Combine(m_outputDirectory, "mimic")))
            {
                using (var moduleWriter = provider.CreateModuleWriter(m_outputDirectory, "MimicModule", new string[] { "{EngineLayout.DefaultMounts.DeploymentRootPath.Path.Combine('BuildXL.Transformers.Runners.dll')}" }))
                {
                    configWriter.AddModule(moduleWriter);

                    // Write each pip into its spec file
                    using (Timer updateTimer = new Timer(ReportProgress, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)))
                    {
                        Parallel.ForEach(m_specFileToPipsLookup, specPips =>
                        {
                            using (var writer = provider.CreateSpecWriter(RemapPath(specPips.Key)))
                            {
                                List<Process> allProcesses = new List<Process>();
                                foreach (var p in specPips)
                                {
                                    Interlocked.Increment(ref m_processesEncountered);

                                    lock (moduleWriter)
                                    {
                                        int lengthToStrip = m_outputDirectory.Length;
                                        if (!(m_outputDirectory.EndsWith(@"/", StringComparison.Ordinal) || m_outputDirectory.EndsWith(@"\", StringComparison.Ordinal)))
                                        {
                                            lengthToStrip++;
                                        }

                                        string specPath = writer.AbsolutePath.Remove(0, lengthToStrip);
                                        moduleWriter.AddSpec(specPath, writer);
                                    }

                                    Process process = p as Process;
                                    if (process != null)
                                    {
                                        allProcesses.Add(process);
                                    }

                                    CopyFile copyFile = p as CopyFile;
                                    if (copyFile != null)
                                    {
                                        bool isDirectory;
                                        bool isResponseFile;
                                        writer.AddCopyFile(
                                            GetProcessOutputName(p.PipId),
                                            GetInputExpression(copyFile.Source, writer, configWriter, out isDirectory, out isResponseFile),
                                            GetOutputExpression(copyFile.Destination, configWriter));
                                        if (isDirectory)
                                        {
                                            throw new MimicGeneratorException("CopyFile shouldn't produce a directory");
                                        }
                                    }

                                    WriteFile writeFile = p as WriteFile;
                                    if (writeFile != null)
                                    {
                                        bool ignore = false;
                                        if (m_ignoreResponseFiles)
                                        {
                                            File f;
                                            if (m_buildGraph.Files.TryGetValue(writeFile.Destination, out f))
                                            {
                                                if (f.Location.EndsWith(ResponseFileName, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    ignore = true;
                                                }
                                            }
                                        }

                                        if (!ignore)
                                        {
                                            writer.AddWriteFile(GetProcessOutputName(p.PipId), GetOutputExpression(writeFile.Destination, configWriter));
                                        }
                                    }

                                    SealDirectory sealDirectory = p as SealDirectory;
                                    if (sealDirectory != null)
                                    {
                                        HandleSealDirectory(sealDirectory, writer, configWriter);
                                    }
                                }

                                HandleProcesses(writer, configWriter, allProcesses);
                            }
                        });

                        success &= WriteQueuedInputFiles();
                    }
                }
            }

            Console.WriteLine("Write {0} input files", m_inputsWritten);
            Console.WriteLine("{0} Input files were using the default size", m_inputsWithDefaultSize);

            return success;
        }

        public void WriteBuildScript()
        {
            using (StreamWriter writer = new StreamWriter(Path.Combine(m_outputDirectory, "MimicBuild.bat")))
            {
                writer.WriteLine("REM This file sets up the environment to be able to perform mimic builds");
                writer.WriteLine(string.Empty);
                writer.WriteLine("echo Update the '[UPDATE]' areas with the root of a fully built BuildXL repo and uncomment them");
                writer.WriteLine(string.Empty);
                writer.WriteLine("SET BUILDXL_DISABLE_DECLARE_BEFORE_USE_CHECK=1");
                writer.WriteLine(@"REM SET ScriptSdk=[UPDATE]\Public\Script\Sdk");
                writer.WriteLine($@"REM [UPDATE]\Out\Bin\release\net461\{Branding.ProductExecutableName} /c:config.dsc /cacheconfigfilepath:cacheConfig.json /p:deploymentroot=[UPDATE]\out\bin\release /logstats /viewer:show");
            }
        }

        public void HandleSealDirectory(SealDirectory sealDirectory, SpecWriter writer, ConfigWriter configWriter)
        {
            List<string> remappedContents = new List<string>();
            Dir dir = m_buildGraph.Directories[sealDirectory.Directory];
            foreach (int content in dir.Contents)
            {
                int producingProcess;
                if (m_buildGraph.OutputArtifactToProducingPip.TryGetValue(content, out producingProcess))
                {
                    var p = m_buildGraph.Pips[producingProcess];
                    remappedContents.Add(writer.GetProcessInputName(GetProcessOutputName(producingProcess), p.Spec,
                        content));
                }
                else
                {
                    remappedContents.Add(configWriter.ToRelativePathExpression(m_buildGraph.Files[content].Location));
                    QueueWritingInputFileIfNecessary(content);
                }
            }

            writer.AddSealDirectory(GetSealOutputName(sealDirectory.Directory), configWriter.ToRelativePathExpression(dir.Location), remappedContents);
        }

        private void HandleProcesses(SpecWriter writer, ConfigWriter configWriter, List<Process> processes)
        {
            Tuple<ProcessStats, Process>[] stats = new Tuple<ProcessStats, Process>[processes.Count];

            for (int i = 0; i < processes.Count; i++)
            {
                stats[i] = new Tuple<ProcessStats, Process>(GetProcessStats(processes[i]), processes[i]);
            }

            var sortedStats = stats.OrderByDescending(s => s.Item1.ExecutionTimeMs);

            // Mark the longest process in the spec so it may be used for scaling in the transformer if applicable
            bool isLongest = true;
            foreach (var item in sortedStats)
            {
                WriteMimicInvocation(item.Item2, writer, configWriter, isLongest);
                isLongest = false;
            }
        }

        private void WriteMimicInvocation(Process process, SpecWriter writer, ConfigWriter configWriter, bool isLongestProcess)
        {
            // Declared inputs & seal directory dependencies
            List<string> directoryDependencies = new List<string>();
            List<string> fileDependencies = new List<string>();
            foreach (int consumes in process.Consumes)
            {
                bool wasDirectory;
                bool isResponseFile;
                string expression = GetInputExpression(consumes, writer, configWriter, out wasDirectory, out isResponseFile);

                if (wasDirectory)
                {
                    directoryDependencies.Add(expression);
                }
                else if (!m_ignoreResponseFiles || !isResponseFile)
                {
                    fileDependencies.Add(expression);
                }
            }

            // Declared outputs
            List<SpecWriter.MimicFileOutput> mimicOutputs = new List<SpecWriter.MimicFileOutput>();
            foreach (var produces in process.Produces)
            {
                File file = m_buildGraph.Files[produces];
                SpecWriter.MimicFileOutput mimicOutput = new SpecWriter.MimicFileOutput()
                {
                    Path = GetOutputExpression(produces, configWriter),
                    LengthInBytes = file.GetScaledLengthInBytes(m_inputScaleFactor),
                    RepeatingContent = file.Hash,
                    FileId = produces,
                };

                mimicOutputs.Add(mimicOutput);
            }

            // Observed accesses (if applicable)
            ObservedAccess[] accesses = null;
            int lookupPip = -1;
            if (m_buildGraph.ObservedAccesses.Count > 0)
            {
                lookupPip = process.OriginalPipId ?? process.PipId;
                if (!m_buildGraph.ObservedAccesses.TryGetValue(lookupPip, out accesses))
                {
                    Console.WriteLine("Warning: No observed accesses recorded for Pip Id: {0}", lookupPip);
                }
            }

            string relativeObservedAccessPath = null;
            if (accesses != null)
            {
                string observedAccessesPath = writer.WriteObservedAccessesFile(accesses, lookupPip);
                relativeObservedAccessPath = observedAccessesPath.Replace(writer.AbsolutePath, string.Empty).TrimStart('\\');
            }

            writer.AddMimicInvocation(
                GetProcessOutputName(process.PipId),
                directoryDependencies,
                fileDependencies,
                mimicOutputs,
                relativeObservedAccessPath,
                process.Semaphores,
                Math.Max(1, process.ProcessWallTimeMs),
                isLongestProcess);
        }

        private struct ProcessStats
        {
            public int ExecutionTimeMs;
            public int CppFiles;
            public int CsFiles;
        }

        private ProcessStats GetProcessStats(Process p)
        {
            ProcessStats stats = new ProcessStats()
            {
                ExecutionTimeMs = p.ProcessWallTimeMs,
            };

            foreach (int file in p.Consumes)
            {
                File f;
                if (m_buildGraph.Files.TryGetValue(file, out f))
                {
                    if (f.Location.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.CsFiles++;
                    }
                    else if (f.Location.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.CppFiles++;
                    }
                }
            }

            return stats;
        }

        /// <summary>
        /// Gets the expression for an input
        /// </summary>
        /// <param name="depId">Id of the input artifact</param>
        /// <param name="specWriter">
        /// SpecWriter to receive expression string according to the language and add ImportStatement to
        /// the Spec if the language is DScript.
        /// </param>
        /// <param name="configWriter">ConfigWriter to add a new mount if necessary</param>
        /// <param name="isDirectory">returns whether the input was a directory</param>
        /// <param name="isResponseFile">returns whether the input was a response file</param>
        /// <returns>expression for the input dependency</returns>
        private string GetInputExpression(int depId, SpecWriter specWriter, ConfigWriter configWriter,
            out bool isDirectory, out bool isResponseFile)
        {
            Dir dir;
            int producingProcess;
            File f;
            isResponseFile = false;
            if (m_buildGraph.Directories.TryGetValue(depId, out dir))
            {
                isDirectory = true;
                var producingSpecPath = m_buildGraph.Pips[dir.ProducerId].Spec;
                return specWriter.GetSealCopyWriteInputName(GetSealOutputName(depId), producingSpecPath);
            }

            if (m_buildGraph.OutputArtifactToProducingPip.TryGetValue(depId, out producingProcess))
            {
                isDirectory = false;
                Pip p = m_buildGraph.Pips[producingProcess];

                if (p is Process)
                {
                    return specWriter.GetProcessInputName(GetProcessOutputName(producingProcess), p.Spec, depId);
                }

                if (p is CopyFile || p is WriteFile)
                {
                    if (m_buildGraph.Files.TryGetValue(depId, out f))
                    {
                        isResponseFile = f.Location.EndsWith(ResponseFileName, StringComparison.OrdinalIgnoreCase);
                    }

                    return specWriter.GetSealCopyWriteInputName(GetProcessOutputName(producingProcess), p.Spec);
                }

                throw new MimicGeneratorException("Error. Pip isn't of known type");
            }

            if (m_buildGraph.Files.TryGetValue(depId, out f))
            {
                isDirectory = false;
                QueueWritingInputFileIfNecessary(depId);
                return configWriter.ToRelativePathExpression(f.Location);
            }

            throw new MimicGeneratorException("Could not find a producer for dependency:{0}", depId);
        }

        /// <summary>
        /// Gets an expression for an output file
        /// </summary>
        private string GetOutputExpression(int fileId, ConfigWriter configWriter)
        {
            return configWriter.ToRelativePathExpression(m_buildGraph.Files[fileId].Location);
        }

        /// <summary>
        /// Writes an input file if necessary
        /// </summary>
        private void QueueWritingInputFileIfNecessary(int fileId)
        {
            if (!m_writeInputFiles || !m_sourceFilesWritten.Add(fileId))
            {
                return;
            }

            File file = m_buildGraph.Files[fileId];
            string originalLocation = file.Location;
            string remappedPath = RemapPath(originalLocation);

            // Avoid overwriting spec files when they are registered as inputs
            if (!m_specFileToPipsLookup.Contains(originalLocation))
            {
                try
                {
                    if (!file.WasLengthSet)
                    {
                        Interlocked.Increment(ref m_inputsWithDefaultSize);
                    }

                    m_inputsToWrite.Add(new Tuple<string, string, int>(remappedPath, file.Hash, file.GetScaledLengthInBytes(m_inputScaleFactor)));
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                    // Some of the registered inputs are directories instead of actual files. As luck has it the directories
                    // are generally created before the the file is attempted to be created. So we can just skip it and move along.
                    Console.WriteLine("Warning: Could not write input file: " + remappedPath);
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }
        }

        /// <summary>
        /// Writes out the queued input files
        /// </summary>
        private bool WriteQueuedInputFiles()
        {
            // First we need to strip out any "files" that are actually directories. Otherwise if we first create a path
            // as a file and later try to create a directory at the same path, the directory creation will fail

            // We need to be able to skip creating any inputs that are actually directories, otherwise if we create a file
            // at a path and later realize it is a directory, the directory creation will fail.
            // To do this we create a set with all paths that must be directories.
            HashSet<string> directoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            StringBuilder builder = new StringBuilder();
            foreach (var tuple in m_inputsToWrite)
            {
                builder.Clear();

                string[] split = tuple.Item1.Split(Path.DirectorySeparatorChar);

                for (int i = 0; i < split.Length - 1; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(Path.DirectorySeparatorChar);
                    }

                    builder.Append(split[i]);
                    directoryPaths.Add(builder.ToString());
                }
            }

            bool success = true;
            Parallel.ForEach(m_inputsToWrite, (tuple) =>
            {
                // Now skip any "file" that is actually a directory
                if (directoryPaths.Contains(tuple.Item1))
                {
                    Console.WriteLine("Warning: skipping declared input '{0}' because it is actually a directory", tuple.Item1);
                    return;
                }

                try
                {
                    using (SourceWriter writer = new SourceWriter(tuple.Item1, tuple.Item2, tuple.Item3))
                    {
                        Interlocked.Increment(ref m_inputsWritten);
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    Console.WriteLine("Error: Could not write input file: {0}. Message:{1} ", tuple.Item1, ex.GetLogEventMessage());
                }
            });

            return success;
        }

        internal static string GetProcessOutputName(int pipId)
        {
            return "Mimic" + pipId;
        }

        private static string GetSealOutputName(int directoryNumber)
        {
            return "SealDirectory" + directoryNumber;
        }

        private string RemapPath(string path)
        {
            return Path.Combine(m_outputDirectory, path[0].ToString(), path.Remove(0, 3));
        }

        private void ReportProgress(object state)
        {
            Console.WriteLine("Progress: {0}/{1} pips. {2}/{3} inputs written", Volatile.Read(ref m_processesEncountered), m_buildGraph.Pips.Count,
                Volatile.Read(ref m_inputsWritten), m_inputsToWrite.Count);
        }

        /// <summary>
        /// Helper to get the string content of a resource file from the current assembly.
        /// </summary>
        /// <remarks>This unfortunately cannot be in a shared location like 'AssemblyHelpers' because on .Net Core it ignores the assembly and always tries to extract the resources from the running assembly. Even though GetManifestResourceNames() does respect it.</remarks>
        private static string GetEmbeddedResourceFile(string resourceKey)
        {
            var callingAssembly = typeof(BuildWriter).GetTypeInfo().Assembly;
            var stream = callingAssembly.GetManifestResourceStream(resourceKey);
            if (stream == null)
            {
                Contract.Assert(false, $"Expected embedded resource key '{resourceKey}' not found in assembly {callingAssembly.FullName}. Valid resource names are: {string.Join(",", callingAssembly.GetManifestResourceNames())}");
                return null;
            }

            using (var sr = new StreamReader(stream))
            {
                return sr.ReadToEnd();
            }
        }
    }
}
