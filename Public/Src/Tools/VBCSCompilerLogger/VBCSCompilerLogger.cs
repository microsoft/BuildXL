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
    public class CompilerFileAccessLogger : Logger
    {
        private const string CscTaskName = "Csc";
        private const string VbcTaskName = "Vbc";
        private const string CscToolName = "csc.exe";
        private const string VbcToolName = "vbc.exe";
        private AugmentedManifestReporter m_augmentedReported;

        /// <inheritdoc/>
        public override void Initialize(IEventSource eventSource)
        {
            eventSource.MessageRaised += EventSourceOnMessageRaised;
            m_augmentedReported = AugmentedManifestReporter.Instance;
        }

        private void EventSourceOnMessageRaised(object sender, BuildMessageEventArgs e)
        {
            if (e is TaskCommandLineEventArgs commandLine)
            {
                // We are only interested in CSharp and VisualBasic tasks
                string language;
                string arguments;
                switch (commandLine.TaskName)
                {
                    case CscTaskName:
                        language = LanguageNames.CSharp;
                        arguments = GetArgumentsFromCommandLine(CscToolName, commandLine.CommandLine);
                        break;
                    case VbcTaskName:
                        language = LanguageNames.VisualBasic;
                        arguments = GetArgumentsFromCommandLine(VbcToolName, commandLine.CommandLine);
                        break;
                    default:
                        return;
                }

                // We were able to split the compiler invocation from its arguments. This is the indicator
                // that something didn't go as expected. Since failing to parse the command line means we
                // are not catching all inputs/outputs properly, we have no option but to fail the corresponding pip
                if (arguments == null)
                {
                    throw new ArgumentException($"Unexpected tool name in command line. Expected '{CscToolName}' or '{VbcToolName}', but got: {commandLine.CommandLine}");
                }

                var parsedCommandLine = CompilerUtilities.GetParsedCommandLineArguments(language, arguments, commandLine.ProjectFile);
                RegisterAccesses(parsedCommandLine);
            }
        }

        private string GetArgumentsFromCommandLine(string toolToTrim, string commandLine)
        {
            toolToTrim += " ";
            int index = commandLine.IndexOf(toolToTrim, StringComparison.OrdinalIgnoreCase);
            
            if (index == -1)
            {
                return null;
            }

            return commandLine.Substring(index + toolToTrim.Length);
        }

        private void RegisterAccesses(CommandLineArguments args)
        {
            // All inputs
            RegisterInputs(args.SourceFiles.Select(source => source.Path));
            RegisterInputs(args.AnalyzerReferences.Select(reference => reference.FilePath));
            RegisterInputs(args.EmbeddedFiles.Select(embedded => embedded.Path));
            RegisterInput(args.Win32ResourceFile);
            RegisterInput(args.Win32Icon);
            RegisterInput(args.Win32Manifest);
            RegisterInputs(args.AdditionalFiles.Select(additional => additional.Path));
            RegisterInputs(args.MetadataReferences.Select(metadata => metadata.Reference).Where(pathOrAssemblyName => FileUtilities.FileSystem.IsPathRooted(pathOrAssemblyName)));
            RegisterInput(args.AppConfigPath);
            RegisterInput(args.RuleSetPath);
            RegisterInput(args.SourceLink);
            RegisterInputs(args.AnalyzerReferences.Select(analyzerRef => analyzerRef.FilePath));

            // All outputs
            RegisterOutput(args.TouchedFilesPath?.Insert(args.TouchedFilesPath.Length - 1, ".read"));
            RegisterOutput(args.TouchedFilesPath?.Insert(args.TouchedFilesPath.Length - 1, ".write"));
            RegisterOutput(args.DocumentationPath);
            RegisterOutput(args.ErrorLogPath);
            RegisterOutput(args.OutputRefFilePath);

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

            RegisterOutput(Path.Combine(args.OutputDirectory, outputFileName));
            if (args.EmitPdb)
            {
                RegisterOutput(Path.Combine(args.OutputDirectory, args.PdbPath ?? Path.ChangeExtension(outputFileName, ".pdb")));
            }
        }

        private void RegisterOutput(string filePath)
        {
            RegisterAccess(filePath, m_augmentedReported.TryReportFileCreations);
        }

        private void RegisterInput(string filePath)
        {
            RegisterAccess(filePath, m_augmentedReported.TryReportFileReads);
        }

        private void RegisterInputs(IEnumerable<string> filePaths)
        {
            Contract.Requires(filePaths != null);

            if (!m_augmentedReported.TryReportFileReads(filePaths))
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
    }
}