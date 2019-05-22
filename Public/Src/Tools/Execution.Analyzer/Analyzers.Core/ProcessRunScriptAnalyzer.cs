// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Analyzer InitializeProcessRunScriptAnalyzer()
        {
            string outputFile = null;
            string outputDirectory = null;
            long? pipId = null;
            string pipInput = null;
            string pipOutput = null;
            bool echo = false;
            string saveOutputsRoot = null;
            string resultOutputsRoot = null;
            string jsonEnvironmentScript = null;
            string linkReproPath = null;
            bool redirectStreams = false;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = ParseSingletonPathOption(opt, outputFile);
                }
                else if (opt.Name.Equals("outputDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    pipId = Convert.ToInt64(ParseStringOption(opt), 16);
                }
                else if (opt.Name.Equals("pipInput", StringComparison.OrdinalIgnoreCase))
                {
                    pipInput = Path.GetFullPath(ParseStringOption(opt));
                }
                else if (opt.Name.Equals("pipOutput", StringComparison.OrdinalIgnoreCase))
                {
                    pipOutput = Path.GetFullPath(ParseStringOption(opt));
                }
                else if (opt.Name.Equals("echo", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("e", StringComparison.OrdinalIgnoreCase))
                {
                    echo = true;
                }
                else if (opt.Name.Equals("saveOutputsRoot", StringComparison.OrdinalIgnoreCase))
                {
                    saveOutputsRoot = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("resultOutputsRoot", StringComparison.OrdinalIgnoreCase))
                {
                    resultOutputsRoot = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("linkRepro", StringComparison.OrdinalIgnoreCase))
                {
                    linkReproPath = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("jsonEnvironmentScript", StringComparison.OrdinalIgnoreCase))
                {
                    jsonEnvironmentScript = ParseStringOption(opt);
                }
                else if (opt.Name.Equals("redirectStreams", StringComparison.OrdinalIgnoreCase))
                {
                    redirectStreams = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for dump process text analysis: {0}", opt.Name);
                }
            }

            if (pipId == null && pipInput == null && pipOutput == null)
            {
                throw Error("pip, pipInput, or pipOutput parameter is required");
            }

            if (pipId != null && outputFile == null && outputDirectory == null)
            {
                outputFile = $"Pip{pipId.Value:x}.bat";
            }

            if (outputFile == null && outputDirectory == null)
            {
                throw Error("outputFile or outputDirectory must be specified (outputFile cannot be used if multiple pips match)");
            }

            if (saveOutputsRoot != null)
            {
                saveOutputsRoot = Path.GetFullPath(saveOutputsRoot);
            }

            if (resultOutputsRoot != null)
            {
                resultOutputsRoot = Path.GetFullPath(resultOutputsRoot);
            }

            if (linkReproPath != null)
            {
                linkReproPath = Path.GetFullPath(linkReproPath);
            }

            if (outputFile != null)
            {
                outputFile = Path.GetFullPath(outputFile);
            }

            if (outputDirectory != null)
            {
                outputDirectory = Path.GetFullPath(outputDirectory);
            }

            return new ProcessRunScriptAnalyzer(GetAnalysisInput())
            {
                PipId = pipId,
                PipInput = pipInput,
                PipOutput = pipOutput,
                OutputFile = outputFile,
                OutputDirectory = outputDirectory,
                Echo = echo,
                SaveOutputsRoot = saveOutputsRoot,
                ResultOutputsRoot = resultOutputsRoot,
                JsonEnvironmentScript = jsonEnvironmentScript,
                LinkReproPath = linkReproPath,
                RedirectStreams = redirectStreams,
            };
        }

        private static void WriteProcessRunScriptAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("ProcessRunScript Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.ProcessRunScript), "Generates batch file which can run process with appropriate env vars set");
            writer.WriteOption("pip", "Optional. The semi-stable hash for the pip.", shortName: "p");
            writer.WriteOption("pipInput", "Optional. If pip is not specified, match all process pips that consume this file (either directly or through a sealed directory)");
            writer.WriteOption("pipOutput", "Optional. If pip is not specified, match all process pips that produce this file (either directly or through a sealed directory)");
            writer.WriteOption("outputFile", @"Optional. The path to the output script file (if using /pip it defaults to .\PipNNNNNNNNNNNNNNNN.bat).", shortName: "o");
            writer.WriteOption("outputDirectory", @"Optional. The name of directory to put a batch file per pip (batch files are named PipNNNNNNNNNNNNNNNN.bat).");
            writer.WriteOption("echo", @"Optional. Echo commands as they are run");
            writer.WriteOption("saveOutputsRoot", @"Optional. Path of a directory to backup existing outputs.");
            writer.WriteOption("resultOutputsRoot", @"Optional. Path of a directory to put the resulting outputs of running the script.");
            writer.WriteOption("linkReproPath", @"Optional. The directory to store a MSVC Link Repro (i.e. LINK_REPRO) so that the link can be performed on another machine for diagnosing linker issues.");
            writer.WriteOption("jsonEnvironmentScript", @"Optional. A batch script which takes a single json file path and sets the environment variables in it (the batch script version does not handle special characters today which can cause problems).");
            writer.WriteOption("redirectStreams", @"Optional. Redirect the stdout and stderr of the process to .bat.out and .bat.err respectively.");
        }
    }

    /// <summary>
    /// Analyzer used to generate batch script for running process with environment variables from pip graph
    /// </summary>
    internal sealed class ProcessRunScriptAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the dump file
        /// </summary>
        public string OutputFile;
        public string OutputDirectory;

        public long? PipId;
        public string PipInput;
        public string PipOutput;
        public bool Echo;
        public bool RedirectStreams;
        public string SaveOutputsRoot;
        public string ResultOutputsRoot;
        public string JsonEnvironmentScript;
        public string LinkReproPath;

        private string m_substSource;
        private string m_substTarget;

        public ProcessRunScriptAnalyzer(AnalysisInput input)
            : base(input)
        {
        }


        private void SaveOutputs(StreamWriter writer, Process pip, string directory)
        {
            writer.WriteLine(":::: Save PIP Outputs to {0}", directory);

            writer.WriteLine(":: Clearing Directory");
            writer.WriteLine("IF EXIST \"{0}\" del /F/S/Q \"{0}\"", directory);
            writer.WriteLine("IF EXIST \"{0}\" rmdir /S/Q \"{0}\"", directory);
            writer.WriteLine();

            IEnumerable<AbsolutePath> outputPaths =
                pip.FileOutputs.Select(f => f.Path)
                .Concat(pip.DirectoryOutputs.Select(d => d.Path));

            writer.WriteLine(":: Ensure Directories Exist");
            foreach (var fileOutputFolder in outputPaths.Select(p => p.GetParent(PathTable)).Distinct())
            {
                if (fileOutputFolder.IsValid)
                {
                    writer.WriteLine("IF NOT EXIST \"{0}\\{1}\" mkdir \"{0}\\{1}\"", directory, fileOutputFolder.ToString(PathTable).Replace(':', '_'));
                }
            }
            writer.WriteLine();

            writer.WriteLine(":: Save Files");
            foreach (var fileOutput in pip.FileOutputs)
            {
                writer.Write("move ");
                writer.Write("\"{0}\" ", fileOutput.Path.ToString(PathTable));
                writer.Write("\"{0}\\{1}\"", directory, fileOutput.Path.ToString(PathTable).Replace(':', '_'));
                writer.WriteLine();
            }
            writer.WriteLine();

            writer.WriteLine(":: Save Directories");
            foreach (var directoryOutput in pip.DirectoryOutputs)
            {
                writer.Write("move ");
                writer.Write("\"{0}\" ", directoryOutput.Path.ToString(PathTable));
                writer.Write("\"{0}\\{1}\"", directory, directoryOutput.Path.ToString(PathTable).Replace(':', '_'));
                writer.WriteLine();
            }
            writer.WriteLine();
        }

        private IEnumerable<AbsolutePath> GetOutputDirectories(Process pip)
        {
            foreach (var directoryOutput in pip.DirectoryOutputs.Select(d => d.Path))
                yield return directoryOutput;

            foreach (var temporaryDirectoryOutput in pip.AdditionalTempDirectories)
                yield return temporaryDirectoryOutput;

            if (pip.TempDirectory != null)
            {
                yield return pip.TempDirectory;
            }
        }

        private void DeleteOutputs(StreamWriter writer, Process pip)
        {
            writer.WriteLine(":::: Delete Outputs");

            writer.WriteLine(":: Delete Files");
            foreach (var fileOutput in pip.FileOutputs)
            {
                writer.WriteLine("IF EXIST \"{0}\" del \"{0}\"", fileOutput.Path.ToString(PathTable));
            }
            writer.WriteLine();

            writer.WriteLine(":: Delete Directories");
            foreach (var directory in GetOutputDirectories(pip))
            {
                writer.WriteLine("IF EXIST \"{0}\" del /F/S/Q \"{0}\"", directory.ToString(PathTable));
                writer.WriteLine("IF EXIST \"{0}\" rmdir /S/Q \"{0}\"", directory.ToString(PathTable));
            }
            writer.WriteLine();
        }

        private void RestoreOutputs(StreamWriter writer, Process pip, string directory)
        {
            DeleteOutputs(writer, pip);

            writer.WriteLine(":::: Restore Outputs from {0}", directory);

            writer.WriteLine(":: Restore Files");
            foreach (var fileOutput in pip.FileOutputs)
            {
                writer.Write("move ");
                writer.Write("\"{0}\\{1}\" ", directory, fileOutput.Path.ToString(PathTable).Replace(':', '_'));
                writer.Write("\"{0}\"", fileOutput.Path.ToString(PathTable));
                writer.WriteLine();
            }
            writer.WriteLine();

            writer.WriteLine(":: Restore Directories");
            foreach (var directoryOutput in pip.DirectoryOutputs)
            {
                writer.Write("move ");
                writer.Write("\"{0}\\{1}\" ", directory, directoryOutput.Path.ToString(PathTable).Replace(':', '_'));
                writer.Write("\"{0}\"", directoryOutput.Path.ToString(PathTable));
                writer.WriteLine();
            }
            writer.WriteLine();
        }

        private void SetupEnvironment(StreamWriter writer, Process pip, string outputFile)
        {
            var pipEnviornment = new PipEnvironment();
            var environment = pipEnviornment.GetEffectiveEnvironmentVariables(PathTable, pip).ToDictionary();

            writer.WriteLine(":::: Environment Variables");
            if (string.IsNullOrEmpty(JsonEnvironmentScript))
            {
                writer.WriteLine(":: Clear Existing Environment Variables");
                writer.WriteLine(@"for /f ""tokens=1* delims=="" %%a in ('set') do (");
                writer.WriteLine("    set %%a=");
                writer.WriteLine(")");
                writer.WriteLine();

                writer.WriteLine(":: Setting PIP Environment Variables");
                foreach (var environmentVariable in environment)
                {
                    writer.WriteLine("set {0}={1}", SanitizeEnvironmentVariableValue(environmentVariable.Key), SanitizeEnvironmentVariableValue(environmentVariable.Value));
                }
            }
            else
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(outputFile));
                using (var jsonFile = File.Create($"{outputFile}.env.json"))
                using (var jsonStream = new StreamWriter(jsonFile))
                using (var json = new JsonTextWriter(jsonStream))
                {
                    json.WriteStartObject();
                    foreach (var environmentVariable in environment)
                    {
                        json.WritePropertyName(environmentVariable.Key);
                        json.WriteValue(environmentVariable.Value);
                    }
                    json.WriteEndObject();
                }
                writer.WriteLine($"call {JsonEnvironmentScript} {outputFile}.env.json");
            }
            writer.WriteLine();
        }

        private static string SanitizeEnvironmentVariableValue(string value)
        {
            var replacements = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>(@"|", @"^|"),
                new Tuple<string, string>(@"(", @"^("),
                new Tuple<string, string>(@")", @"^)"),
                new Tuple<string, string>(@"&", @"^&"),
                new Tuple<string, string>(@">", @"^>"),
                new Tuple<string, string>(@"<", @"^<"),
            };

            foreach (var replacement in replacements)
            {
                value = value.Replace(replacement.Item1, replacement.Item2);
            }

            return value;
        }

        private IEnumerable<Process> GetProcessPipDependents(Pip pip)
        {
            foreach(var dependent in CachedGraph.PipGraph.RetrievePipImmediateDependents(pip))
            {
                switch (dependent.PipType)
                {
                    case PipType.Process:
                        yield return dependent as Process;
                        break;

                    case PipType.SealDirectory:
                        foreach (var directoryDependent in GetProcessPipDependents(dependent))
                            yield return directoryDependent;
                        break;
                        
                    case PipType.CopyFile:
                    case PipType.WriteFile:
                    case PipType.HashSourceFile:
                    case PipType.Ipc:
                    case PipType.Value:
                    case PipType.SpecFile:
                    case PipType.Module:
                        break;
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            var orderedPips =
                PipId.HasValue ?
                    CachedGraph.PipGraph.RetrievePipReferencesOfType(PipType.Process)
                        .Where(lazyPip => lazyPip.SemiStableHash == PipId)
                        .Select(lazyPip => (Process)lazyPip.HydratePip())
                        .ToList() :
                PipInput != null ?
                    GetProcessPipDependents(CachedGraph.PipGraph.TryFindProducer(
                        AbsolutePath.Create(CachedGraph.Context.PathTable, PipInput),
                        global::BuildXL.Scheduler.VersionDisposition.Latest,
                        null
                    )).ToList() :
                PipOutput != null ? new List<Process>() { CachedGraph.PipGraph.TryFindProducer(
                        AbsolutePath.Create(CachedGraph.Context.PathTable, PipOutput),
                        global::BuildXL.Scheduler.VersionDisposition.Latest,
                        null
                    ) as Process} :
                throw new InvalidOperationException();

            if (!(OutputDirectory != null || (OutputFile != null && orderedPips.Count == 1)))
            {
                Console.WriteLine("Too many pips match to use the outputFile option, please specify the outputDirectory argument so different batch file can be made for each pip");
                return 1;
            }

            int doneProcesses = 0;
            var t = new Timer(
                o =>
                {
                    var done = doneProcesses;
                    Console.WriteLine("Processes Done: {0} of {1}", done, orderedPips.Count);
                },
                null,
                5000,
                5000);

            try
            {
                foreach (var pip in orderedPips)
                {
                    string outputFile = OutputFile ?? ($"{OutputDirectory}\\{pip.SemiStableHash:x}.bat");

                    using (var scriptStream = File.Create(outputFile, bufferSize: 64 << 10 /* 64 KB */))
                    using (var writer = (!string.IsNullOrWhiteSpace(m_substSource) && !string.IsNullOrWhiteSpace(m_substTarget)) ? new TranslatingStreamWriter(m_substTarget, m_substSource, scriptStream) :  new StreamWriter(scriptStream))
                    {
                        if (!Echo)
                        {
                            writer.WriteLine("@echo off");
                        }

                        writer.WriteLine("setlocal");

                        if (pip.WorkingDirectory.IsValid)
                        {
                            writer.WriteLine(":::: Set Working Directory");
                            writer.WriteLine("cd /D \"{0}\"", pip.WorkingDirectory.ToString(PathTable));
                            writer.WriteLine();
                        }

                        if (SaveOutputsRoot != null)
                        {
                            SaveOutputs(writer, pip, SaveOutputsRoot);
                        }

                        DeleteOutputs(writer, pip);

                        writer.WriteLine(":::: Creating Output Directories");
                        foreach (var directory in GetOutputDirectories(pip))
                        {
                            if (directory.IsValid)
                            {
                                writer.WriteLine("mkdir \"{0}\"", directory.ToString(PathTable));
                            }
                        }
                        writer.WriteLine();

                        SetupEnvironment(writer, pip, outputFile);

                        if (LinkReproPath != null)
                        {
                            writer.WriteLine($"IF EXIST \"{LinkReproPath}\" del /f/s/q \"{LinkReproPath}\"");
                            writer.WriteLine($"IF EXIST \"{LinkReproPath}\" rmdir /s/q \"{LinkReproPath}\"");
                            writer.WriteLine($"mkdir {LinkReproPath}");
                            writer.WriteLine($"set LINK_REPRO={LinkReproPath}");
                        }

                        if (pip.StandardInputData.IsValid)
                        {
                            using (var standardInputStream = File.Create(outputFile + ".in", bufferSize: 64 << 10 /* 64 KB */))
                            using (var standardInputWriter = new StreamWriter(standardInputStream))
                            {
                                var bytes = CharUtilities.Utf8NoBomNoThrow.GetBytes(pip.StandardInputData.ToString(PathTable));
                                standardInputStream.Write(bytes, 0, bytes.Length);
                            }
                        }

                        writer.WriteLine(":::: Running Process");
                        writer.Write("\"{0}\"", pip.Executable.Path.ToString(PathTable));

                        string arguments = GetArgumentsDataFromProcess(pip).ToString(PathTable);
                        if (!string.IsNullOrEmpty(arguments))
                        {
                            writer.Write(" {0}", arguments);
                        }

                        if (pip.StandardInputData.IsValid)
                        {
                            writer.Write(" < \"{0}\"", outputFile + ".in");
                        }

                        if (pip.StandardInputFile.IsValid)
                        {
                            writer.Write(" < \"{0}\"", pip.StandardInputFile.Path.ToString(PathTable));
                        }

                        if (RedirectStreams)
                        {
                            writer.Write(" 1> \"{0}\"", outputFile + ".out");
                            writer.Write(" 2> \"{0}\"", outputFile + ".err");
                        }

                        writer.WriteLine();

                        if (SaveOutputsRoot != null)
                        {
                            SaveOutputs(writer, pip, ResultOutputsRoot);
                            RestoreOutputs(writer, pip, SaveOutputsRoot);

                            writer.WriteLine(":::: Removing saveOutputsRoot");
                            writer.WriteLine($"IF EXIST \"{SaveOutputsRoot}\" del /f/s/q \"{SaveOutputsRoot}\"");
                            writer.WriteLine($"IF EXIST \"{SaveOutputsRoot}\" rmdir /s/q \"{SaveOutputsRoot}\"");
                        }

                        doneProcesses++;
                    }
                }
            }
            finally
            {
                // kill and wait for the status timer to die...
                using (var e = new AutoResetEvent(false))
                {
                    t.Dispose(e);
                    e.WaitOne();
                }
            }

            return 0;
        }

        public override void DominoInvocation(DominoInvocationEventData data)
        {
            // Capture the DominoInvocation event for sake of switching process run scripts to their un-substed versions.
            var loggingConfig = data.Configuration.Logging;
            if (loggingConfig.SubstSource.IsValid &&
                loggingConfig.SubstTarget.IsValid)
            {
                // tostring on root of drive automatically adds trailing slash, so only add trailing slash when needed.
                m_substTarget = loggingConfig.SubstTarget.ToString(PathTable, PathFormat.HostOs);
                if (m_substTarget.LastOrDefault() != Path.DirectorySeparatorChar)
                {
                    m_substTarget += Path.DirectorySeparatorChar;
                }

                m_substSource = loggingConfig.SubstSource.ToString(PathTable, PathFormat.HostOs);
                if (m_substSource.LastOrDefault() != Path.DirectorySeparatorChar)
                {
                    m_substSource += Path.DirectorySeparatorChar;
                }
            }
        }

        /// <summary>
        /// StreamWriter that translates strings as the stream is written
        /// </summary>
        private class TranslatingStreamWriter : StreamWriter
        {
            private readonly string m_fromPath;
            private readonly string m_toPath;

            public TranslatingStreamWriter(string fromPath, string toPath, Stream stream) : base(stream)
            {
                m_fromPath = fromPath;
                m_toPath = toPath;
            }

            public override void Write(string value)
            {
                base.Write(value.Replace(m_fromPath, m_toPath));
            }

            public override void Write(string format, object arg0)
            {
                Write(string.Format(format, arg0));
            }

            public override void Write(string format, object arg0, object arg1)
            {
                Write(string.Format(format, arg0, arg1));
            }

            public override void Write(string format, object arg0, object arg1, object arg2)
            {
                Write(string.Format(format, arg0, arg1, arg2));
            }

            public override void Write(string format, params object[] arg)
            {
                Write(string.Format(format, arg));
            }

            public override void WriteLine(string value)
            {
                base.WriteLine(value.Replace(m_fromPath, m_toPath));
            }

            public override void WriteLine(string format, object arg0)
            {
                WriteLine(string.Format(format, arg0));
            }

            public override void WriteLine(string format, object arg0, object arg1)
            {
                WriteLine(string.Format(format, arg0, arg1));
            }

            public override void WriteLine(string format, object arg0, object arg1, object arg2)
            {
                WriteLine(string.Format(format, arg0, arg1, arg2));
            }

            public override void WriteLine(string format, params object[] arg)
            {
                WriteLine(string.Format(format, arg));
            }
        }
    }
}
