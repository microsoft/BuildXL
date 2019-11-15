// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Processes;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;

namespace VBCSCompilerLogger
{
    /// <summary>
    /// This logger catches csc and vbc MSBuild tasks and uses the command line argument passed to the compiler to mimic the file accesses that the compiler 
    /// would have produced.
    /// </summary>
    /// <remarks>
    /// This MSBuild logger is used by the MSBuild frontend of BuildXL when scheduling pips: when process breakaway is supported, VBSCompiler.exe is allowed to escape the sandbox
    /// and outlive the originating pip. This logger is used as a way to compensate for the missing accesses and the MSBuild frontend attaches it to every MSBuild invocation.
    /// </remarks>
    public class VBCSCompilerLogger : Logger
    {
        private const string CscTaskName = "Csc";
        private const string VbcTaskName = "Vbc";
        private const string CscToolName = "csc.exe";
        private const string VbcToolName = "vbc.exe";
        private AugmentedManifestReporter m_augmentedReporter;

        /// <inheritdoc/>
        public override void Initialize(IEventSource eventSource)
        {
            eventSource.MessageRaised += EventSourceOnMessageRaised;
            m_augmentedReporter = AugmentedManifestReporter.Instance;
        }

        private void EventSourceOnMessageRaised(object sender, BuildMessageEventArgs e)
        {
            if (e is TaskCommandLineEventArgs commandLine)
            {
                // We are only interested in CSharp and VisualBasic tasks
                string language;
                string extractedArguments;
                string error;
                bool success;
                
                switch (commandLine.TaskName)
                {
                    case CscTaskName:
                        language = LanguageNames.CSharp;
                        success = TryGetArgumentsFromCommandLine(CscToolName, commandLine.CommandLine, out extractedArguments, out error);
                        break;
                    case VbcTaskName:
                        language = LanguageNames.VisualBasic;
                        success = TryGetArgumentsFromCommandLine(VbcToolName, commandLine.CommandLine, out extractedArguments, out error);
                        break;
                    default:
                        return;
                }

                // We were able to split the compiler invocation from its arguments. This is the indicator
                // that something didn't go as expected. Since failing to parse the command line means we
                // are not catching all inputs/outputs properly, we have no option but to fail the corresponding pip
                if (!success)
                {
                    throw new ArgumentException(error);
                }

                var parsedCommandLine = CompilerUtilities.GetParsedCommandLineArguments(language, extractedArguments, commandLine.ProjectFile);
                RegisterAccesses(parsedCommandLine);
            }
        }

        private bool TryGetArgumentsFromCommandLine(string toolToTrim, string commandLine, out string arguments, out string error)
        {
            toolToTrim += " ";
            int index = commandLine.IndexOf(toolToTrim, StringComparison.OrdinalIgnoreCase);
            
            if (index == -1)
            {
                arguments = null;
                error = $"Unexpected tool name in command line. Expected '{CscToolName}' or '{VbcToolName}', but got: {commandLine}";
                
                return false;
            }

            arguments = commandLine.Substring(index + toolToTrim.Length);
            error = string.Empty;

            return true;
        }

        private void RegisterAccesses(CommandLineArguments args)
        {
            // All inputs
            RegisterInputs(args.AnalyzerReferences.Select(r => ResolveRelativePathIfNeeded(r.FilePath, args.BaseDirectory, args.ReferencePaths)));
            RegisterInputs(args.MetadataReferences.Select(r => ResolveRelativePathIfNeeded(r.Reference, args.BaseDirectory, args.ReferencePaths)));
            RegisterInputs(args.SourceFiles.Select(source => source.Path));
            RegisterInputs(args.EmbeddedFiles.Select(embedded => embedded.Path));
            RegisterInput(args.Win32ResourceFile);
            RegisterInput(args.Win32Icon);
            RegisterInput(args.Win32Manifest);
            RegisterInputs(args.AdditionalFiles.Select(additional => additional.Path));
            RegisterInput(args.AppConfigPath);
            RegisterInput(args.RuleSetPath);
            RegisterInput(args.SourceLink);
            
            // All outputs
            RegisterOutput(args.TouchedFilesPath?.Insert(args.TouchedFilesPath.Length - 1, ".read"));
            RegisterOutput(args.TouchedFilesPath?.Insert(args.TouchedFilesPath.Length - 1, ".write"));
            RegisterOutput(args.DocumentationPath);
            RegisterOutput(args.ErrorLogPath);
            RegisterOutput(args.OutputRefFilePath);
            var outputFileName = ComputeOutputFileName(args);
            RegisterOutput(Path.Combine(args.OutputDirectory, outputFileName));
            if (args.EmitPdb)
            {
                RegisterOutput(Path.Combine(args.OutputDirectory, args.PdbPath ?? Path.ChangeExtension(outputFileName, ".pdb")));
            }
        }

        private static string ComputeOutputFileName(CommandLineArguments args)
        {
            string outputFileName = args.OutputFileName;

            // If the output filename is not specified, we follow the logic documented for csc.exe
            // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/out-compiler-option
            // If you do not specify the name of the output file:
            // - An.exe will take its name from the source code file that contains the Main method.
            // - A.dll or .netmodule will take its name from the first source code file.
            // Note: observe however that when invoked through the standard MSBuild csc task, the output file name is always specified
            // and it is based on the first source file (when the original task didn't specify it). So this is just about being conservative.
            if (string.IsNullOrEmpty(outputFileName))
            {
                switch (args.CompilationOptions.OutputKind)
                {
                    // For these cases, the first source file is used
                    case OutputKind.DynamicallyLinkedLibrary:
                        outputFileName = Path.ChangeExtension(args.SourceFiles[0].Path, ".dll");
                        break;
                    case OutputKind.NetModule:
                        outputFileName = Path.ChangeExtension(args.SourceFiles[0].Path, ".netmodule");
                        break;
                    case OutputKind.WindowsRuntimeMetadata:
                        outputFileName = Path.ChangeExtension(args.SourceFiles[0].Path, ".winmdobj");
                        break;
                    // For these cases an .exe will be generated based on the source file that contains a Main method.
                    // We cannot easily predict this statically, so we bail out for this case.
                    case OutputKind.ConsoleApplication:
                    case OutputKind.WindowsApplication:
                    case OutputKind.WindowsRuntimeApplication:
                        throw new InvalidOperationException("The output filename was not specified and it could not be statically predicted. Static predictions are required for managed compilers when shared compilation is enabled. " +
                            "Please specify the output filename or disable shared compilation by setting 'useManagedSharedCompilation' in Bxl main configuration file.");
                    default:
                        throw new InvalidOperationException($"Unrecognized OutputKind: ${args.CompilationOptions.OutputKind}");
                }
            }

            Contract.Assert(!string.IsNullOrEmpty(outputFileName));
            return outputFileName;
        }

        private void RegisterOutput(string filePath)
        {
            RegisterAccess(filePath, m_augmentedReporter.TryReportFileCreations);
        }

        private void RegisterInput(string filePath)
        {
            RegisterAccess(filePath, m_augmentedReporter.TryReportFileReads);
        }

        private void RegisterInputs(IEnumerable<string> filePaths)
        {
            Contract.Requires(filePaths != null);

            if (!m_augmentedReporter.TryReportFileReads(filePaths))
            {
                throw new InvalidOperationException($"Failed at reporting augmented file accesses [${string.Join(", ", filePaths)}]");
            }
        }

        private void RegisterAccess(string filePath, Func<IEnumerable<string>, bool> tryReport)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            if (!tryReport(new[] { filePath }))
            {
                throw new InvalidOperationException($"Failed at reporting an augmented file access for ${filePath}");
            }
        }

        /// <summary>
        /// Returns an absolute path based on the given path and base and additional search directories. Null if the path cannot be resolved.
        /// </summary>
        /// <remarks>
        /// This mimics the behavior of the compiler, where in case path is a relative one, tries to compose an absolute path based on the provided
        /// directories (first the base directory, then the additional ones, in order) and returns the first absolute path such that the path exists.
        /// Observe this will cause potential absent file probes that will be observed by detours, which is intentional.
        /// </remarks>
        private string ResolveRelativePathIfNeeded(string path, string baseDirectory, IEnumerable<string> additionalSearchDirectories)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            // If the path is already an absolute one, just return
            if (FileUtilities.FileSystem.IsPathRooted(path))
            {
                return path;
            }

            // So this should be a relative path
            // We first try resolving against the base directory
            var candidate = Path.Combine(baseDirectory, path);
            if (PathExistsAsFile(candidate))
            {
                return candidate;
            }

            // Now try against all the additional search directories
            foreach (string searchDirectory in additionalSearchDirectories)
            {
                candidate = Path.Combine(searchDirectory, path);
                if (PathExistsAsFile(candidate))
                {
                    return candidate;
                }
            }

            // The path could not be resolved
            return null;
        }

        private bool PathExistsAsFile(string path)
        {
            var result = FileUtilities.FileSystem.TryProbePathExistence(path, followSymlink: false);
            return result.Succeeded && result.Result == PathExistence.ExistsAsFile;
        }
    }
}