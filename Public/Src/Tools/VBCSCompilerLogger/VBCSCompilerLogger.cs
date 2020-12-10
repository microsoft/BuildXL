// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities.Collections;
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

        private readonly ConcurrentBigSet<Diagnostic> m_badSwitchErrors = new ConcurrentBigSet<Diagnostic>();

        /// <inheritdoc/>
        public override void Initialize(IEventSource eventSource)
        {
            eventSource.MessageRaised += EventSourceOnMessageRaised;
            eventSource.BuildFinished += EventSourceOnBuildFinished;

            m_augmentedReporter = AugmentedManifestReporter.Instance;
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

                var parsedCommandLine = CompilerUtilities.GetParsedCommandLineArguments(language, extractedArguments, commandLine.ProjectFile, out string[] args);

                // In general we don't care about errors in the command line, since any error there will eventually fail the compiler call.
                // However, we do care about new switches that may represent file accesses that are introduced to the compiler and this logger 
                // is not aware of.
                // This means that if the command line comes back with a bad switch error, but the compiler doesn't fail, we need to fail the call. 
                // Error 2007 represents a bad switch. Unfortunately there doesn't seem to be any public enumeration that defines it properly.
                var badSwitchErrors = parsedCommandLine.Errors.Where(diagnostic => diagnostic.Id.Contains("2007")).ToList();
                foreach (var badSwitch in badSwitchErrors)
                {
                    // If we find a bad switch error, delay making a decision until we know if the compiler failed or not.
                    m_badSwitchErrors.Add(badSwitch);
                }

                string[] embeddedResourceFilePaths = Array.Empty<string>();

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

                var result = new ParseResult()
                {
                    ParsedArguments = parsedCommandLine,
                    EmbeddedResourcePaths = embeddedResourceFilePaths
                };

                RegisterAccesses(result);
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

        private void RegisterAccesses(ParseResult results)
        {
            // Even thought CommandLineArguments class claims to always report back absolute paths, that's not the case.
            // Use the base directory to resolve relative paths if needed
            // The base directory is what CommandLineArgument claims to be resolving all paths against anyway
            var accessRegister = new AccessRegistrar(m_augmentedReporter, results.ParsedArguments.BaseDirectory);

            // All inputs
            accessRegister.RegisterInputs(results.ParsedArguments.AnalyzerReferences.Select(r => ResolveRelativePathIfNeeded(r.FilePath, results.ParsedArguments.BaseDirectory, results.ParsedArguments.ReferencePaths)));
            accessRegister.RegisterInputs(results.ParsedArguments.MetadataReferences.Select(r => ResolveRelativePathIfNeeded(r.Reference, results.ParsedArguments.BaseDirectory, results.ParsedArguments.ReferencePaths)));
            accessRegister.RegisterInputs(results.ParsedArguments.SourceFiles.Select(source => source.Path));
            accessRegister.RegisterInputs(results.ParsedArguments.EmbeddedFiles.Select(embedded => embedded.Path));
            accessRegister.RegisterInput(results.ParsedArguments.Win32ResourceFile);
            accessRegister.RegisterInput(results.ParsedArguments.Win32Icon);
            accessRegister.RegisterInput(results.ParsedArguments.Win32Manifest);
            accessRegister.RegisterInputs(results.ParsedArguments.AdditionalFiles.Select(additional => additional.Path));
            accessRegister.RegisterInput(results.ParsedArguments.AppConfigPath);
            accessRegister.RegisterInput(results.ParsedArguments.RuleSetPath);
            accessRegister.RegisterInput(results.ParsedArguments.SourceLink);
            accessRegister.RegisterInput(results.ParsedArguments.CompilationOptions.CryptoKeyFile);
#if !TEST
            // When building for tests we intentionally use an older version of CommandLineArguments where these fields are not available
            accessRegister.RegisterInputs(results.ParsedArguments.AnalyzerConfigPaths);
            accessRegister.RegisterOutput(results.ParsedArguments.ErrorLogOptions?.Path);

            // If there is any analyzer configured and the generated output directory is not null, that means some of the configured analyzers could be source generators, and therefore they might
            // produce files under the generated output directory. We cannot predict those outputs files, so we bail out for this case
            if (results.ParsedArguments.GeneratedFilesOutputDirectory != null && results.ParsedArguments.AnalyzerReferences.Length > 0 && !results.ParsedArguments.SkipAnalyzers)
            {
                throw new InvalidOperationException("The compilation is configured to emit generated sources which cannot be statically predicted. Static predictions are required for managed compilers when shared compilation is enabled. " +
                            "Please disable shared compilation.");
            }
#endif
            // /resource: parameters end up in ParsedArguments.ManifestResources, but the returned class drops the file path. We'll have to get them explicitly.
            // We might be able to simply use ParsedArguments.ManifestResources if this gets resolved: https://github.com/dotnet/roslyn/issues/41372
            accessRegister.RegisterInputs(results.EmbeddedResourcePaths);

            // All outputs
            accessRegister.RegisterOutput(results.ParsedArguments.TouchedFilesPath?.Insert(results.ParsedArguments.TouchedFilesPath.Length - 1, ".read"));
            accessRegister.RegisterOutput(results.ParsedArguments.TouchedFilesPath?.Insert(results.ParsedArguments.TouchedFilesPath.Length - 1, ".write"));
            accessRegister.RegisterOutput(results.ParsedArguments.DocumentationPath);
            accessRegister.RegisterOutput(results.ParsedArguments.ErrorLogPath);
            accessRegister.RegisterOutput(results.ParsedArguments.OutputRefFilePath);
            var outputFileName = ComputeOutputFileName(results.ParsedArguments);
            accessRegister.RegisterOutput(Path.Combine(results.ParsedArguments.OutputDirectory, outputFileName));
            if (results.ParsedArguments.EmitPdb)
            {
                accessRegister.RegisterOutput(Path.Combine(results.ParsedArguments.OutputDirectory, results.ParsedArguments.PdbPath ?? Path.ChangeExtension(outputFileName, ".pdb")));
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
            if (Path.IsPathRooted(path))
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
            return FileUtilities.FileExistsNoFollow(path);
        }

        /// <summary>
        /// Resolves all relative path registrations against a base path
        /// </summary>
        private sealed class AccessRegistrar
        {
            private readonly string m_basePath;
            private readonly AugmentedManifestReporter m_augmentedReporter;

            public AccessRegistrar(AugmentedManifestReporter reporter, string basePath)
            {
                Contract.Requires(!string.IsNullOrEmpty(basePath));
                Contract.Requires(reporter != null);

                m_basePath = basePath;
                m_augmentedReporter = reporter;
            }

            public void RegisterOutput(string filePath)
            {
                RegisterAccess(filePath, m_augmentedReporter.TryReportFileCreations);
            }

            public void RegisterInput(string filePath)
            {
                RegisterAccess(filePath, m_augmentedReporter.TryReportFileReads);
            }

            public void RegisterInputs(IEnumerable<string> filePaths)
            {
                Contract.Requires(filePaths != null);

                var finalPaths = filePaths.Where(path => !string.IsNullOrEmpty(path)).Select(path => MakeAbsoluteIfNeeded(path));

                if (!m_augmentedReporter.TryReportFileReads(finalPaths))
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

                filePath = MakeAbsoluteIfNeeded(filePath);

                if (!tryReport(new[] { filePath }))
                {
                    throw new InvalidOperationException($"Failed at reporting an augmented file access for ${filePath}");
                }
            }

            private string MakeAbsoluteIfNeeded(string path)
            {
                Contract.Requires(!string.IsNullOrEmpty(path));

                return Path.IsPathRooted(path)? path : Path.Combine(m_basePath, path);
            }
        }

        /// <summary>
        /// Encapsulates the results of all parsing.
        /// </summary>
        private sealed class ParseResult
        {
            public CommandLineArguments ParsedArguments { get; set; }
            public string[] EmbeddedResourcePaths { get; set; }
        }
    }
}