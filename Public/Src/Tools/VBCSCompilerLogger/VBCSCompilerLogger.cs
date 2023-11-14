// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;

#nullable enable

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

        private readonly ConcurrentBigSet<Diagnostic> m_badSwitchErrors = new();

        /// <inheritdoc/>
        public override void Initialize(IEventSource eventSource)
        {
            if (eventSource is IEventSource4 eventSource4)
            {
                // This needs to happen so binary loggers can get evaluation properties and items
                eventSource4.IncludeEvaluationPropertiesAndItems();
            }

            eventSource.MessageRaised += EventSourceOnMessageRaised;
            eventSource.BuildFinished += EventSourceOnBuildFinished;
        }

        private void EventSourceOnBuildFinished(object sender, BuildFinishedEventArgs e)
        {
            // The build succedeed, but we couldn't recognize some of the switches passed to the compiler
            // That means the Roslyn parsing classes we are using are older than the version of the compiler this
            // build is using.
            // We have to fail the build in this case, since we could be missing switches that imply file accesses
            if (e.Succeeded && m_badSwitchErrors.Count > 0)
            {
                // Should be safe to retrieve all bad switch errors, the build is done.
                var allMessages = string.Join(Environment.NewLine, m_badSwitchErrors.UnsafeGetList().Select(diagnostic => $"[{diagnostic.Id}] {diagnostic.GetMessage()}"));

                throw new InvalidOperationException("Unrecognized switch(es) passed to the compiler. Even though the compiler supports it, using shared compilation in a sandboxed process requires the build engine to understand all compiler switches." +
                    $"This probably means the version of the compiler is newer than the version the build engine is aware of. Disabling shared compilation will likely fix the problem. Details: {allMessages}");
            }
        }

        private void EventSourceOnMessageRaised(object sender, BuildMessageEventArgs e)
        {
            if (e is TaskCommandLineEventArgs commandLine)
            {
                string? extractedArguments;
                string error;
                bool success;
                string language;

                // We are only interested in CSharp and VisualBasic tasks
                switch (commandLine.TaskName)
                {
                    case CscTaskName:
                        language = LanguageNames.CSharp;
                        success = TryGetArgumentsFromCommandLine(CscTaskName, commandLine.CommandLine, out extractedArguments, out error);
                        break;
                    case VbcTaskName:
                        language = LanguageNames.VisualBasic;
                        success = TryGetArgumentsFromCommandLine(VbcTaskName, commandLine.CommandLine, out extractedArguments, out error);
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

                CommandLineArguments parsedCommandLine = CompilerUtilities.GetParsedCommandLineArguments(language, extractedArguments!, commandLine.ProjectFile, out string[] args);

                // In general we don't care about errors in the command line, since any error there will eventually fail the compiler call.
                // However, we do care about new switches that may represent file accesses that are introduced to the compiler and this logger 
                // is not aware of.
                // This means that if the command line comes back with a bad switch error, but the compiler doesn't fail, we need to fail the call. 
                // Error 2007 represents a bad switch. Unfortunately there doesn't seem to be any public enumeration that defines it properly.
                IEnumerable<Diagnostic> badSwitchErrors = parsedCommandLine.Errors.Where(diagnostic => diagnostic.Id.Contains("2007")).ToList();
                foreach (Diagnostic badSwitch in badSwitchErrors)
                {
                    // If we find a bad switch error, delay making a decision until we know if the compiler failed or not.
                    m_badSwitchErrors.Add(badSwitch);
                }

                string[] embeddedResourceFilePaths = Array.Empty<string>();
                Contract.RequiresNotNullOrEmpty(parsedCommandLine.BaseDirectory);

                // Determine the paths to the embedded resources. /resource: parameters end up in CommandLineArguments.ManifestResources,
                // but the returned class drops the file path (and is currently internal anyway).
                // We should be able to remove this if/when this gets resolved: https://github.com/dotnet/roslyn/issues/41372.
                if (parsedCommandLine.ManifestResources.Length > 0)
                {
                    IEnumerable<string> embeddedResourcesArgs = args.Where(
                        a =>
                            a.StartsWith("/resource:", StringComparison.OrdinalIgnoreCase)
                            || a.StartsWith("/res:", StringComparison.Ordinal)
                            || a.StartsWith("/linkresource:", StringComparison.Ordinal)
                            || a.StartsWith("/linkres:", StringComparison.Ordinal));

                    embeddedResourceFilePaths = CompilerUtilities.GetEmbeddedResourceFilePaths(
                        embeddedResourcesArgs,
                        parsedCommandLine.BaseDirectory);
                }

                RegisterAccesses(new ParseResult(parsedCommandLine, embeddedResourceFilePaths));
            }
        }

        // internal for unit testing.
        internal static bool TryGetArgumentsFromCommandLine(string task, string commandLine, out string? arguments, out string error)
        {
            int taskIndex = commandLine.IndexOf(task, StringComparison.OrdinalIgnoreCase);
            const int ExtensionLength = 4;
            int indexFollowingTool = taskIndex + task.Length + ExtensionLength;
            if (taskIndex == -1 || indexFollowingTool >= commandLine.Length
                || !hasExeOrDllExtension(commandLine, taskIndex + task.Length, out bool hasExeExtension)
                // A space follows csc.exe and vbc.exe, whereas a double quote and a space follow csc.dll and vbc.dll.
                || indexFollowingTool + 1 + (hasExeExtension ? 0 : 1) >= commandLine.Length)
            {
                arguments = null;
                error = $"Unexpected tool name in command line. Expected csc.exe, csc.dll, vbc.exe, or vbc.dll, but got: {commandLine}";
                return false;
            }

            // Obtain the arguments supplied to the task. Ignore the space following csc.exe and vbc.exe and double quote
            // and space following csc.dll and vbc.dll.
            arguments = commandLine.Substring(indexFollowingTool + 1 + (hasExeExtension ? 0 : 1));
            error = string.Empty;

            return true;

            static bool hasExeOrDllExtension(string commandLineRemainder, int index, out bool hasExeExtension)
            {
                string extension = commandLineRemainder.Substring(index, ExtensionLength);
                if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return hasExeExtension = true;
                }

                hasExeExtension = false;
                return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void RegisterAccesses(ParseResult results)
        {
            // Even though CommandLineArguments class claims to always report back absolute paths, that's not the case.
            // Use the base directory to resolve relative paths if needed
            // The base directory is what CommandLineArgument claims to be resolving all paths against anyway
            CommandLineArguments commandLineArguments = results.ParsedArguments;
            string? baseDirectory = commandLineArguments.BaseDirectory!;
            var accessRegistrar = new AccessRegistrar(baseDirectory);

            // All *inputs* for compiler options that are guaranteed to exist for the Microsoft.CodeAnalysis
            // library versions we support.
            accessRegistrar.RegisterInputs(commandLineArguments.MetadataReferences.Select(r => ResolveRelativePathIfNeeded(r.Reference, baseDirectory, commandLineArguments.ReferencePaths)));
            accessRegistrar.RegisterInputs(commandLineArguments.SourceFiles.Select(source => source.Path));
            accessRegistrar.RegisterInput(commandLineArguments.Win32ResourceFile);
            accessRegistrar.RegisterInput(commandLineArguments.Win32Icon);
            accessRegistrar.RegisterInput(commandLineArguments.Win32Manifest);
            accessRegistrar.RegisterInput(commandLineArguments.AppConfigPath);
            accessRegistrar.RegisterInput(commandLineArguments.CompilationOptions.CryptoKeyFile);

            // All *outputs* for compiler options that are guaranteed to exist for the Microsoft.CodeAnalysis
            // library versions we support.
            accessRegistrar.RegisterOutput(commandLineArguments.TouchedFilesPath?.Insert(commandLineArguments.TouchedFilesPath.Length - 1, ".read"));
            accessRegistrar.RegisterOutput(commandLineArguments.TouchedFilesPath?.Insert(commandLineArguments.TouchedFilesPath.Length - 1, ".write"));
            accessRegistrar.RegisterOutput(commandLineArguments.DocumentationPath);

            // /resource: parameters end up in ParsedArguments.ManifestResources, but the returned class drops the file path. We'll have to get them explicitly.
            // We might be able to simply use ParsedArguments.ManifestResources if this gets resolved: https://github.com/dotnet/roslyn/issues/41372
            accessRegistrar.RegisterInputs(results.EmbeddedResourcePaths);

            // The following registrations concern compiler options that did not exist when Roslyn was open sourced:
            // https://github.com/dotnet/roslyn/blob/3611ed35610793e814c8aa25715aa582ec08a8b6/Src/Compilers/Core/Source/NonPortable/CommandLine/CommonCommandLineArguments.cs
            // Therefore, we no-op if a compiler option doesn't exist to support old Microsoft.CodeAnalysis libraries.
            // It's important that, if a new compiler option introduces a new type, then the path(s) associated with the
            // new compiler option are passed into tryAccessCommandLineArgument. E.g.,
            // tryAccessCommandLineArgument(() => commandLineArguments.ErrorLogOptions?.Path)
            // not
            // tryAccessCommandLineArgument(() => commandLineArguments.ErrorLogOptions)?.Path
            // Otherwise, the CLR will throw a System.TypeLoadException.

            // Inputs:
            ImmutableArray<CommandLineAnalyzerReference> analyzerReferences = tryAccessCommandLineArgument(() => commandLineArguments.AnalyzerReferences);
            if (analyzerReferences != default)
            {
                accessRegistrar.RegisterInputs(analyzerReferences.Select(r => ResolveRelativePathIfNeeded(r.FilePath, baseDirectory, commandLineArguments.ReferencePaths)));
            }
            
            ImmutableArray<CommandLineSourceFile> embeddedFiles = tryAccessCommandLineArgument(() => commandLineArguments.EmbeddedFiles);
            if (embeddedFiles != default)
            {
                accessRegistrar.RegisterInputs(embeddedFiles.Select(embedded => embedded.Path));
            }

            ImmutableArray<CommandLineSourceFile> additionalFiles = tryAccessCommandLineArgument(() => commandLineArguments.AdditionalFiles);
            if (additionalFiles != default)
            {
                accessRegistrar.RegisterInputs(additionalFiles.Select(additional => additional.Path));
            }
            
            accessRegistrar.RegisterInput(tryAccessCommandLineArgument(() => commandLineArguments.RuleSetPath));
            accessRegistrar.RegisterInput(tryAccessCommandLineArgument(() => commandLineArguments.SourceLink));   
#if !TEST
            // When building for tests we intentionally use an older version of CommandLineArguments where these fields are not available
            ImmutableArray<string> analyzerConfigPaths = tryAccessCommandLineArgument(() => commandLineArguments.AnalyzerConfigPaths);
            if (analyzerConfigPaths != default)
            {
                accessRegistrar.RegisterInputs(analyzerConfigPaths);
            }

            accessRegistrar.RegisterInput(tryAccessCommandLineArgument(() => commandLineArguments.ErrorLogOptions?.Path));

            // If there is any analyzer configured and the generated output directory is not null, that means some of the configured analyzers could be source generators, and therefore they might
            // produce files under the generated output directory. We cannot predict those outputs files, so we bail out for this case
            if (tryAccessCommandLineArgument(() => commandLineArguments.GeneratedFilesOutputDirectory) != null && analyzerReferences.Length > 0 &&
                    !tryAccessCommandLineArgument(() => commandLineArguments.SkipAnalyzers))
            {
                throw new InvalidOperationException("The compilation is configured to emit generated source files, which cannot be statically predicted."
                    + " Static predictions are required for managed compilers when shared compilation is enabled."
                    + " Please disable shared compilation.");
            }
#endif
            // Outputs:
            accessRegistrar.RegisterOutput(tryAccessCommandLineArgument(() => commandLineArguments.ErrorLogPath));
            accessRegistrar.RegisterOutput(tryAccessCommandLineArgument(() => commandLineArguments.OutputRefFilePath));
            string outputFileName = ComputeOutputFileName(commandLineArguments);
            string? outputDirectory = commandLineArguments.OutputDirectory;
            accessRegistrar.RegisterOutput(outputDirectory != null ? Path.Combine(outputDirectory, outputFileName) : outputDirectory);
            if (tryAccessCommandLineArgument(() => commandLineArguments.EmitPdb))
            {
                accessRegistrar.RegisterOutput(Path.Combine(outputDirectory!, results.ParsedArguments.PdbPath ?? Path.ChangeExtension(outputFileName, ".pdb")));
            }

            // For those using old Microsoft.CodeAnalysis libraries, no-op when trying to get new compiler options discovered at runtime.
            static T? tryAccessCommandLineArgument<T>(Func<T> accessCommandLineArgument)
            {
                try
                {
                    return accessCommandLineArgument();
                }
                catch (MissingMethodException)
                {
                    // No-op.
                    return default;
                }
            }
        }

        private static string ComputeOutputFileName(CommandLineArguments args)
        {
            string? outputFileName = args.OutputFileName;

            // If the output filename is not specified, we follow the logic documented for csc.exe
            // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/out-compiler-option
            // If you do not specify the name of the output file:
            // - An.exe will take its name from the source code file that contains the Main method.
            // - A.dll or .netmodule will take its name from the first source code file.
            // Note: observe however that when invoked through the standard MSBuild csc task, the output file name is always specified
            // and it is based on the first source file (when the original task didn't specify it). So this is just about being conservative.
            if (string.IsNullOrEmpty(outputFileName))
            {
                outputFileName = args.CompilationOptions.OutputKind switch
                {
                    // For these cases, the first source file is used
                    OutputKind.DynamicallyLinkedLibrary => Path.ChangeExtension(args.SourceFiles[0].Path, ".dll"),
                    OutputKind.NetModule => Path.ChangeExtension(args.SourceFiles[0].Path, ".netmodule"),
                    OutputKind.WindowsRuntimeMetadata => Path.ChangeExtension(args.SourceFiles[0].Path, ".winmdobj"),
                    // For these cases an .exe will be generated based on the source file that contains a Main method.
                    // We cannot easily predict this statically, so we bail out for this case.
                    OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication => throw new InvalidOperationException("The output filename was not specified and it could not be statically predicted. Static predictions are required for managed compilers when shared compilation is enabled. " +
                                                "Please specify the output filename or disable shared compilation by setting 'useManagedSharedCompilation' in Bxl main configuration file."),
                    _ => throw new InvalidOperationException($"Unrecognized OutputKind: {args.CompilationOptions.OutputKind}"),
                };
            }

            return outputFileName!;
        }

        /// <summary>
        /// Returns an absolute path based on the given path and base and additional search directories. Null if the path cannot be resolved.
        /// </summary>
        /// <remarks>
        /// This mimics the behavior of the compiler, where in case path is a relative one, tries to compose an absolute path based on the provided
        /// directories (first the base directory, then the additional ones, in order) and returns the first absolute path such that the path exists.
        /// Observe this will cause potential absent file probes that will be observed by detours, which is intentional.
        /// </remarks>
        private static string? ResolveRelativePathIfNeeded(string path, string baseDirectory, IEnumerable<string> additionalSearchDirectories)
        {
            if (path.Length == 0)
            {
                return null;
            }

            // If the path is already an absolute one, just return
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            // So this should be a relative path
            // We first try resolving against the base directory
            string candidate = Path.Combine(baseDirectory, path);
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

        private static bool PathExistsAsFile(string path) => FileUtilities.FileExistsNoFollow(path);

        /// <summary>
        /// Resolves all relative path registrations against a base path
        /// </summary>
        private sealed class AccessRegistrar
        {
            private readonly string m_basePath;

            public AccessRegistrar(string basePath) => m_basePath = basePath;

            public void RegisterOutput(string? filePath) => RegisterAccess(filePath, AugmentedManifestReporter.Instance.TryReportFileCreations);

            public void RegisterInput(string? filePath) => RegisterAccess(filePath, AugmentedManifestReporter.Instance.TryReportFileReads);

            public void RegisterInputs(IEnumerable<string?> filePaths)
            {
                IEnumerable<string> finalPaths = filePaths.Where(path => !string.IsNullOrEmpty(path)).Select((path) => MakeAbsoluteIfNeeded(path!));
                if (!AugmentedManifestReporter.Instance.TryReportFileReads(finalPaths))
                {
                    throw new InvalidOperationException($"Failed at reporting augmented file accesses for the following files: [{string.Join(", ", filePaths)}]");
                }
            }

            private void RegisterAccess(string? filePath, Func<IEnumerable<string>, bool> tryReport)
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                filePath = MakeAbsoluteIfNeeded(filePath!);
                if (!tryReport(new[] { filePath }))
                {
                    throw new InvalidOperationException($"Failed at reporting an augmented file access for {filePath}");
                }
            }

            private string MakeAbsoluteIfNeeded(string path)
            {
                Contract.Requires(path.Length > 0);
                return Path.IsPathRooted(path) ? path : Path.Combine(m_basePath, path);
            }
                
        }

        /// <summary>
        /// Encapsulates the results of all parsing.
        /// </summary>
        private readonly record struct ParseResult(CommandLineArguments ParsedArguments, string[] EmbeddedResourcePaths);
    }
}