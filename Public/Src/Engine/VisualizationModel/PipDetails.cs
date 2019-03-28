// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Engine.Visualization;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Pip Details
    /// </summary>
    public sealed class PipDetails : PipReference
    {
        /// <summary>
        /// The qualifier as a string.
        /// </summary>
        public string QualifierAsString { get; set; }

        /// <summary>
        /// List of pip tags
        /// </summary>
        public IEnumerable<string> Tags { get; set; }

        /// <summary>
        /// The process' executable
        /// </summary>
        public ToolReference Executable { get; set; }

        /// <summary>
        /// The process' working directory
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// The process' command line fragments
        /// </summary>
        public IEnumerable<string> CommandLineFragments { get; set; }

        /// <summary>
        /// The process' command line
        /// </summary>
        public string CommandLine { get; set; }

        /// <summary>
        /// List of Environment variables set for this pip
        /// </summary>
        public IEnumerable<Tuple<string, string>> EnvironmentVariables { get; set; }

        /// <summary>
        /// Full path to response file
        /// </summary>
        public FileDetails ResponseFile { get; set; }

        /// <summary>
        /// Warning timeout duration
        /// </summary>
        public TimeSpan? WarningTimeout { get; set; }

        /// <summary>
        /// Timeout duration
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Standard input
        /// </summary>
        public StandardInput StandardInput { get; set; }

        /// <summary>
        /// Standard output file
        /// </summary>
        public FileReference StandardOutput { get; set; }

        /// <summary>
        /// Standard error file
        /// </summary>
        public FileReference StandardError { get; set; }

        /// <summary>
        /// Standard directory path where standard output / input may be written to
        /// </summary>
        public string StandardDirectory { get; set; }

        /// <summary>
        /// List of untracked files
        /// </summary>
        public IEnumerable<string> UntrackedPaths { get; set; }

        /// <summary>
        /// List of untracked directories
        /// </summary>
        public IEnumerable<string> UntrackedScopes { get; set; }

        /// <summary>
        /// List of files that the pip depends on
        /// </summary>
        public IEnumerable<FileDetails> Dependencies { get; set; }

        /// <summary>
        /// List of directories that the pip depends on
        /// </summary>
        public IEnumerable<string> DirectoryDependencies { get; set; }

        /// <summary>
        /// List of additional temporary directories
        /// </summary>
        public IEnumerable<string> AdditionalTempDirectories { get; set; }

        /// <summary>
        /// List of files that this pip produces
        /// </summary>
        public IEnumerable<FileDetails> Outputs { get; set; }

        /// <summary>
        /// Destination file path to be written
        /// </summary>
        public FileDetails Destination { get; set; }

        /// <summary>
        /// Source file path
        /// </summary>
        public FileDetails Source { get; set; }

        /// <summary>
        /// Directory of SealDirectory pip
        /// </summary>
        public string Directory { get; set; }

        /// <summary>
        /// Contents of SealDirectory pip
        /// </summary>
        public IEnumerable<FileDetails> Contents { get; set; }

        /// <summary>
        /// Indicates the kind of sealed directory.
        /// </summary>
        public string SealKind { get; set; }

        /// <summary>
        /// List of Parent Pips
        /// </summary>
        public IEnumerable<PipReference> DependantOf { get; set; }

        /// <summary>
        /// List of child pips
        /// </summary>
        public IEnumerable<PipReference> DependsOn { get; set; }

        /// <summary>
        /// Values whose evaluation created this pip as a side effect. Should be empty or one element for non-meta pips.
        /// </summary>
        public IEnumerable<ValueReference> Values { get; set; }

        /// <summary>
        /// Corresponds to the rendered value of <see cref="IpcPip.MessageBody"/>.
        /// </summary>
        public string Payload { get; set; }

        /// <summary>
        /// Corresponds to individual fragments of <see cref="IpcPip.MessageBody"/>.
        /// </summary>
        public IEnumerable<string> PayloadFragments { get; set; }

        /// <summary>
        /// Corresponds to <see cref="IpcPip.IsServiceFinalization"/>
        /// </summary>
        public bool IsServiceFinalizer { get; set; }

        /// <summary>
        /// Textual value of the IPC client configuration of an Ipc pip.
        /// </summary>
        public string IpcConfig { get; set; }

        /// <summary>
        /// Textual value of the IPC moniker of an Ipc pip.
        /// </summary>
        public string IpcMoniker { get; set; }

        /// <summary>
        /// Default constructor needed for DataContract serialization
        /// </summary>
        public PipDetails()
        {
        }

        /// <summary>
        /// Creates a new PipDetails from a pip
        /// </summary>
        public static PipDetails DetailsFromPip(Pip pip, PathTable pathTable, PipEnvironment pipEnvironment)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pathTable != null);

            var resultingPipDetails = new PipDetails();

            SetCommonPipDetails(pip, resultingPipDetails);

            // Details specific to pip type
            switch (pip.PipType)
            {
                case PipType.WriteFile:
                    SetWriteFilePipDetails((WriteFile)pip, resultingPipDetails, pathTable);
                    break;
                case PipType.CopyFile:
                    SetCopyFilePipDetails((CopyFile)pip, resultingPipDetails, pathTable);
                    break;
                case PipType.Process:
                    SetProcessPipDetails((Process)pip, resultingPipDetails, pathTable, pipEnvironment);
                    break;
                case PipType.HashSourceFile:
                    // Not an interesting pip
                    break;
                case PipType.SealDirectory:
                    SetSealDirectoryPipDetails((SealDirectory)pip, resultingPipDetails, pathTable);
                    break;
                case PipType.Value:
                    SetValuePipDetails((ValuePip)pip, resultingPipDetails, pathTable);
                    break;
                case PipType.SpecFile:
                    SetSpecFilePipDetails((SpecFilePip)pip, resultingPipDetails, pathTable);
                    break;
                case PipType.Module:
                    SetModulePipDetails((ModulePip)pip, resultingPipDetails, pathTable);
                    break;
                case PipType.Ipc:
                    SetIpcPipDetails((IpcPip)pip, resultingPipDetails, pathTable);
                    break;
                default:
                    Contract.Assume(false);
                    break;
            }

            return resultingPipDetails;
        }

        private static void SetCommonPipDetails(Pip p, PipDetails result)
        {
            IVisualizationInformation visualizationInformation = EngineModel.VisualizationInformation;
            PipGraph pipGraph = visualizationInformation.PipGraph.Value;
            var scheduler = visualizationInformation.Scheduler.Value;
            var context = visualizationInformation.Context.Value;
            var pathTable = context.PathTable;
            var symbolTable = context.SymbolTable;

            result.Id = p.PipId.Value;
            result.Hash = p.SemiStableHash.ToString("X16", CultureInfo.InvariantCulture);
            result.QualifierAsString = p.Provenance != null ? context.QualifierTable.GetCanonicalDisplayString(p.Provenance.QualifierId) : "";
            result.Description = p.GetDescription(context);
            result.State = scheduler.GetPipState(p.PipId);

            result.Tags = p.Tags.IsValid ? p.Tags.Select(tag => pathTable.StringTable.GetString(tag)).ToList() : new List<string>();

            IEnumerable<Pip> dependents = pipGraph.RetrievePipImmediateDependents(p);
            result.DependantOf = dependents
                .Where(d => d.PipType != PipType.HashSourceFile)
                .Select(f => FromPip(f))
                .ToList();

            IEnumerable<Pip> dependencies = pipGraph.RetrievePipImmediateDependencies(p);
            result.DependsOn = dependencies
                .Where(d => d.PipType != PipType.HashSourceFile)
                .Select(f => FromPip(f))
                .ToList();

            IEnumerable<ValuePip> valuePips = dependents.Where(pipId => pipId.PipType == PipType.Value).Cast<ValuePip>();
            result.Values = valuePips.Select(valuePip => ValueReference.Create(symbolTable, pathTable, valuePip));
        }

        private static void SetWriteFilePipDetails(WriteFile pip, PipDetails result, PathTable pathTable)
        {
            result.Destination = new FileDetails() { Id = pip.Destination.Path.Value.Value, Path = pip.Destination.Path.ToString(pathTable) };
        }

        private static void SetCopyFilePipDetails(CopyFile pip, PipDetails result, PathTable pathTable)
        {
            result.Source = new FileDetails() { Id = pip.Source.Path.Value.Value, Path = pip.Source.Path.ToString(pathTable) };
            result.Destination = new FileDetails() { Id = pip.Destination.Path.Value.Value, Path = pip.Destination.Path.ToString(pathTable) };
        }

        private static void SetIpcPipDetails(IpcPip pip, PipDetails result, PathTable pathTable)
        {
            result.Payload = pip.MessageBody.ToString(pathTable);
            result.PayloadFragments = ConvertArgumentsToStringArray(pip.MessageBody, pathTable);
            result.IsServiceFinalizer = pip.IsServiceFinalization;
            result.IpcConfig = JsonConvert.SerializeObject(pip.IpcInfo.IpcClientConfig);
            result.IpcMoniker = pip.IpcInfo.IpcMonikerId.ToString(pathTable.StringTable);
            result.Dependencies = pip.FileDependencies.Select(d => FileArtifactToFileDetails(d, pathTable)).OrderBy(d => d).ToList();
            result.StandardOutput = new FileReference { Id = -1, Path = pip.OutputFile.Path.ToString(pathTable) };
        }

        private static FileDetails FileArtifactToFileDetails(FileArtifact file, PathTable pathTable)
        {
            return new FileDetails()
            {
                Id = file.Path.Value.Value,
                Path = file.Path.ToString(pathTable),
            };
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        private static void SetProcessPipDetails(Process pip, PipDetails result, PathTable pathTable, PipEnvironment pipEnvironment)
        {
            var args = new List<string>();
            args.Add(pip.Executable.Path.ToString(pathTable));
            args.AddRange(ConvertArgumentsToStringArray(pip.Arguments, pathTable));

            result.Executable = new ToolReference() { Id = pip.Executable.Path.Value.Value, Path = pip.Executable.Path.ToString(pathTable) };
            result.CommandLineFragments = args;
            result.CommandLine = GetPipCommandLine(pip, pathTable);
            result.WarningTimeout = pip.WarningTimeout;
            result.Timeout = pip.Timeout;

            bool includeDefaultOutputs = result.State == PipState.Failed;

            result.StandardInput = pip.StandardInput.IsFile
                ? StandardInput.CreateFromFile(FileReference.FromAbsolutePath(pathTable, pip.StandardInput.File.Path))
                : pip.StandardInput.IsData
                    ? StandardInput.CreateFromData(
                        pip.StandardInput.Data.ToString(pathTable).Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                    : StandardInput.Invalid;

            result.StandardOutput = GetStdFilePath(pip, SandboxedProcessFile.StandardOutput, pathTable, includeDefaultOutputs);
            result.StandardError = GetStdFilePath(pip, SandboxedProcessFile.StandardError, pathTable, includeDefaultOutputs);
            result.StandardDirectory = pip.StandardDirectory.IsValid ? pip.StandardDirectory.ToString(pathTable) : null;
            result.Dependencies = pip.Dependencies.Select(d => FileArtifactToFileDetails(d, pathTable)).OrderBy(d => d).ToList();
            result.DirectoryDependencies = pip.DirectoryDependencies.Select(d => d.Path.ToString(pathTable)).OrderBy(d => d).ToList();
            result.AdditionalTempDirectories = pip.AdditionalTempDirectories.Select(d => d.ToString(pathTable)).OrderBy(d => d).ToList();
            result.UntrackedPaths = pip.UntrackedPaths.Select(f => f.ToString(pathTable)).OrderBy(f => f).ToList();
            result.UntrackedScopes = pip.UntrackedScopes.Select(f => f.ToString(pathTable)).OrderBy(f => f).ToList();
            result.Outputs = pip.FileOutputs.Select(f => FileArtifactToFileDetails(f.ToFileArtifact(), pathTable)).OrderBy(f => f).ToList();

            result.WorkingDirectory = pip.WorkingDirectory.ToString(pathTable);
            if (!result.WorkingDirectory.EndsWith(@"\", StringComparison.Ordinal))
            {
                result.WorkingDirectory += @"\";
            }

            if (pip.ResponseFile.IsValid)
            {
                result.ResponseFile = FileArtifactToFileDetails(pip.ResponseFile, pathTable);
            }

            var vars = new List<Tuple<string, string>>();

            foreach (var env in pipEnvironment.GetEffectiveEnvironmentVariables(pathTable, pip).ToDictionary().OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                vars.Add(new Tuple<string, string>(env.Key, env.Value));
            }

            vars.Sort((first, second) => string.Compare(first.Item1, second.Item1, StringComparison.OrdinalIgnoreCase));
            result.EnvironmentVariables = vars;
        }

        /// <summary>
        /// Gets the full command line for a given pip
        /// </summary>
        /// <param name="pip">Pip</param>
        /// <param name="pathTable">PathTable</param>
        /// <returns>Command line</returns>
        public static string GetPipCommandLine(Pip pip, PathTable pathTable)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pathTable != null);

            if (pip.PipType == PipType.Process)
            {
                var processPip = (Process)pip;
                return processPip.Executable.Path.ToString(pathTable) + " " + processPip.Arguments.ToString(pathTable);
            }

            return string.Empty;
        }

        private static IEnumerable<string> ConvertArgumentsToStringArray(PipData pipData, PathTable pathTable)
        {
            var result = new List<string>();
            foreach (PipFragment fragment in pipData)
            {
                Contract.Assume(fragment.FragmentType != PipFragmentType.Invalid);
                string s = string.Empty;
                switch (fragment.FragmentType)
                {
                    case PipFragmentType.StringLiteral:
                        s = pathTable.StringTable.GetString(fragment.GetStringIdValue());
                        break;
                    case PipFragmentType.AbsolutePath:
                        s = fragment.GetPathValue().ToString(pathTable);
                        break;
                    case PipFragmentType.NestedFragment:
                        s = fragment.GetNestedFragmentValue().ToString(pathTable);
                        break;
                    default:
                        Contract.Assert(false, "Unhandled fragment type");
                        break;
                }

                if (pipData.FragmentEscaping == PipDataFragmentEscaping.CRuntimeArgumentRules)
                {
                    s = CommandLineEscaping.EscapeAsCommandLineWord(s);
                }

                s += pathTable.StringTable.GetString(pipData.FragmentSeparator);
                result.Add(s);
            }

            return result;
        }

        private static FileReference GetStdFilePath(Process pip, SandboxedProcessFile file, PathTable pathTable, bool includeDefault)
        {
            FileArtifact fileArtifact = file.PipFileArtifact(pip);
            if (fileArtifact.IsValid)
            {
                return FileReference.FromAbsolutePath(pathTable, fileArtifact.Path);
            }

            // includeDefault is true only if the pip has failed.
            // It is important to include the defaults only in the case of a failure;
            // otherwise we might end up showing the error/out from a previous failed run of the pip.
            if (!includeDefault)
            {
                return null;
            }

            string path = Path.Combine(pip.StandardDirectory.ToString(pathTable), file.DefaultFileName());
            if (!File.Exists(path))
            {
                return null;
            }

            return new FileReference
            {
                Id = -1,
                Path = path,
            };
        }

        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private static void SetValuePipDetails(ValuePip pip, PipDetails result, PathTable pathTable)
        {
        }

        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private static void SetSpecFilePipDetails(SpecFilePip pip, PipDetails result, PathTable pathTable)
        {
        }

        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private static void SetModulePipDetails(ModulePip pip, PipDetails result, PathTable pathTable)
        {
        }

        private static void SetSealDirectoryPipDetails(SealDirectory pip, PipDetails result, PathTable pathTable)
        {
            result.Directory = pip.Directory.Path.ToString(pathTable);
            result.SealKind = pip.Kind.ToString();
            result.Contents = pip.Contents.Select(f => FileArtifactToFileDetails(f, pathTable)).OrderBy(f => f).ToList();
        }
    }
}
