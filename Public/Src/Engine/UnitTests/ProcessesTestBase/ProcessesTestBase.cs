// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Interop;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

using Process = BuildXL.Pips.Operations.Process;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Lightweight test base for Processes tests. Provides BuildXLContext, path helpers,
    /// and process-creation utilities at the Pips level only, without depending on
    /// Scheduler, Engine, or FrontEnd assemblies.
    ///
    /// PipTestBase (in Test.BuildXL.Scheduler) extends this class and adds Scheduler-level
    /// infrastructure (PipGraph, PipTable, MountPathExpander, etc.).
    /// </summary>
    public abstract class ProcessesTestBase : TemporaryStorageTestBase
    {
        protected const string SourceRootPrefix = "src";
        protected const string ObjectRootPrefix = "obj";
        protected const string RedirectedRootPrefix = "redirected";

        protected const string TestProcessToolNameWithoutExtension = "Test.BuildXL.Executables.TestProcess";
        protected const string InfiniteWaiterWithoutExtension = "Test.BuildXL.Executables.InfiniteWaiter";

        /// <summary>
        /// FileArtifact for cmd.exe / /bin/sh
        /// </summary>
        protected readonly FileArtifact CmdExecutable;

        /// <summary>
        /// FileArtifact for the generic TestProcess executable.
        /// </summary>
        protected FileArtifact TestProcessExecutable { get; set; }

        protected readonly AbsolutePath[] TestProcessDependencies;

        protected string TestProcessToolName => OperatingSystemHelper.IsUnixOS
            ? TestProcessToolNameWithoutExtension
            : TestProcessToolNameWithoutExtension + ".exe";

        protected string TestProcessToolNameWithCapabilties => OperatingSystemHelper.IsUnixOS
            ? $"{TestProcessToolName}WithCapabilities"
            : $"{TestProcessToolName}WithCapabilities.exe";

        protected static string InfiniteWaiterToolName => OperatingSystemHelper.IsUnixOS
            ? InfiniteWaiterWithoutExtension
            : InfiniteWaiterWithoutExtension + ".exe";

        /// <summary>
        /// BuildXL context (PathTable, StringTable, etc.)
        /// </summary>
        protected BuildXLContext Context;

        private static long s_pipIdCounter = 1;

        protected static long GetNextPipId() => Interlocked.Increment(ref s_pipIdCounter);

        protected static long s_semistableHashCounter = 0;

        /// <summary>
        /// ID used to generate filenames.
        /// </summary>
        protected int m_uniqueFileId;

        protected int m_pipFreshId;

        /// <summary>
        /// Directory containing temporary 'source' files.
        /// </summary>
        protected string SourceRoot { get; private set; }

        /// <summary>
        /// Directory containing temporary 'output' files.
        /// </summary>
        protected string ObjectRoot { get; private set; }

        protected string RedirectedRoot { get; private set; }

        protected AbsolutePath ObjectRootPath { get; set; }

        protected AbsolutePath SourceRootPath { get; set; }

        protected AbsolutePath TestBinRootPath { get; set; }

        protected string TestBinRoot { get; private set; }

        protected ProcessesTestBase(ITestOutputHelper output) : base(output)
        {
            Context = BuildXLContext.CreateInstanceForTesting();
            PathTable.DebugPathTable = Context.PathTable;
            m_pipFreshId = 0;

            TestBinRoot = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(System.Reflection.Assembly.GetExecutingAssembly()));
            TestBinRootPath = AbsolutePath.Create(Context.PathTable, TestBinRoot);
            SourceRoot = Path.Combine(TemporaryDirectory, SourceRootPrefix);
            SourceRootPath = AbsolutePath.Create(Context.PathTable, SourceRoot);

            CmdExecutable = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, CmdHelper.OsShellExe));

            string testProcessFolder = Path.Combine(TestBinRoot, "TestProcess");
            string platformDir = Dispatch.CurrentOS().ToString();
            string exe = Path.Combine(testProcessFolder, platformDir, TestProcessToolName);
            TestProcessExecutable = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, exe));

            // Test process depends on C://Windows (when running on Windows)
            TestProcessDependencies = OperatingSystemHelper.IsUnixOS
                ? new AbsolutePath[0]
                : new AbsolutePath[] { AbsolutePath.Create(Context.PathTable, Environment.GetFolderPath(Environment.SpecialFolder.Windows)) };

            ObjectRoot = Path.Combine(TemporaryDirectory, ObjectRootPrefix);
            RedirectedRoot = Path.Combine(TemporaryDirectory, RedirectedRootPrefix);
            ObjectRootPath = AbsolutePath.Create(Context.PathTable, ObjectRoot);

            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(ObjectRoot);
        }

        /// <summary>
        /// Returns the default OS-level untracked scopes that allow sandboxed processes to run successfully.
        /// Uses PipGraph.UnixDefaults/WindowsOsDefaults directly to avoid duplicating the path lists.
        /// </summary>
        protected IEnumerable<AbsolutePath> GetDefaultUntrackedScopes()
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                // Create UnixDefaults to get the canonical untracked directory list.
                // Passing null for pipGraph is safe — it's only used for source sealing which we don't invoke.
                var unixDefaults = new PipGraph.UnixDefaults(Context.PathTable, pipGraph: null);
                foreach (var dir in unixDefaults.UntrackedDirectories)
                {
                    yield return dir.Path;
                }

                // Untrack TestProcessExecutable's parent directory — dynamic probes are non-deterministic
                // and can cause test failures without this.
                yield return TestProcessExecutable.Path.GetParent(Context.PathTable);
            }
            else
            {
                var windowsDefaults = new PipGraph.WindowsOsDefaults(Context.PathTable);
                foreach (var dir in windowsDefaults.UntrackedDirectories)
                {
                    if (dir.IsValid)
                    {
                        yield return dir.Path;
                    }
                }
            }
        }

        #region Path helpers

        protected AbsolutePath Combine(AbsolutePath root, params PathAtom[] atoms)
            => root.Combine(Context.PathTable, atoms);

        protected AbsolutePath Combine(AbsolutePath root, params string[] atoms)
            => Combine(root, atoms.Select(a => PathAtom.Create(Context.StringTable, a)).ToArray());

        protected FileArtifact CreateSourceFile(AbsolutePath root, string prefix = null)
            => CreateSourceFileWithPrefix(root: root.ToString(Context.PathTable), prefix: prefix);

        protected FileArtifact CreateSourceFile(string root = null)
            => CreateSourceFileWithPrefix(root: root, prefix: SourceRootPrefix);

        protected FileArtifact CreateSourceFileWithPrefix(string root = null, string prefix = null)
        {
            FileArtifact sourceFile = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(prefix ?? SourceRootPrefix, root));
            WriteSourceFile(sourceFile);
            return sourceFile;
        }

        protected void WriteSourceFile(FileArtifact artifact, string content = null)
        {
            Contract.Requires(artifact.IsValid);
            string fullPath = artifact.Path.ToString(Context.PathTable);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content ?? Guid.NewGuid().ToString());
        }

        protected AbsolutePath CreateUniqueDirectory(AbsolutePath root, string prefix = null)
            => CreateUniqueDirectory(root.ToString(Context.PathTable), prefix);

        protected AbsolutePath CreateUniqueDirectory(string root = null, string prefix = null)
        {
            AbsolutePath path = CreateUniqueSourcePath(prefix ?? SourceRootPrefix, root);
            Directory.CreateDirectory(path.ToString(Context.PathTable));
            return path;
        }

        public FileArtifact CreateFileArtifactWithName(string name, string root)
        {
            AbsolutePath.TryCreate(Context.PathTable, Path.Combine(root, name), out AbsolutePath filePath);
            return new FileArtifact(filePath);
        }

        protected FileArtifact CreateOutputFileArtifact(string root = null, string prefix = null)
            => FileArtifact.CreateSourceFile(CreateUniqueObjPath(prefix ?? "obj", root)).CreateNextWrittenVersion();

        protected FileArtifact CreateOutputFileArtifact(AbsolutePath root, string prefix = null)
            => CreateOutputFileArtifact(root.ToString(Context.PathTable), prefix);

        protected AbsolutePath CreateUniqueSourcePath(string prefix, string root = null)
        {
            Contract.Requires(prefix != null);
            return CreateUniquePath(prefix, root ?? SourceRoot);
        }

        protected AbsolutePath CreateUniqueSourcePath() => CreateUniqueSourcePath(SourceRootPrefix);

        protected AbsolutePath CreateUniqueObjPath(string prefix, string root = null)
        {
            Contract.Requires(prefix != null);
            return CreateUniquePath(prefix, root ?? ObjectRoot);
        }

        protected AbsolutePath CreateUniquePath(string prefix, string root)
            => AbsolutePath.Create(Context.PathTable, CreateUniquePathAsString(prefix, root));

        protected string CreateUniquePathAsString(string prefix, string root)
        {
            Contract.Requires(prefix != null);
            Contract.Requires(root != null);
            return Path.Combine(root, string.Format(CultureInfo.InvariantCulture, "{0}_{1}", prefix, m_uniqueFileId++));
        }

        #endregion

        #region Process creation helpers

        /// <summary>
        /// Creates a provenance without requiring a PipGraph. Override in subclasses to register
        /// with PipGraphBuilder.
        /// </summary>
        protected virtual PipProvenance CreateProvenance(StringId usage = default)
        {
            var specFile = CreateUniquePath("spec", SourceRoot);
            var valueName = "MyValue_" + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture);
            var moduleNameId = StringId.Create(Context.StringTable, "module");

            return new PipProvenance(
                Interlocked.Increment(ref s_semistableHashCounter),
                ModuleId.Create(moduleNameId),
                moduleNameId,
                FullSymbol.Create(Context.SymbolTable, valueName),
                new LocationData(specFile, 1, 1),
                QualifierId.Unqualified,
                usage.IsValid ? PipDataBuilder.CreatePipData(Context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, usage) : PipData.Invalid,
                false);
        }

        /// <summary>
        /// Creates arguments from operations.
        /// </summary>
        protected PipData CreateArguments(
            IEnumerable<Operation> processOperations,
            StringTable stringTable)
        {
            var pipDataBuilder = new PipDataBuilder(stringTable);
            CreateArguments(pipDataBuilder, processOperations, stringTable);
            return pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
        }

        protected void CreateArguments(
            PipDataBuilder pipDataBuilder,
            IEnumerable<Operation> processOperations,
            StringTable stringTable)
        {
            foreach (var op in processOperations)
            {
                pipDataBuilder.Add(op.ToCommandLine(Context.PathTable));
            }
        }

        /// <summary>
        /// Naively infers the dependencies and outputs of the given operations.
        /// </summary>
        protected DependenciesAndOutputs InferIOFromOperations(IEnumerable<Operation> processOperations, bool force = false)
        {
            DependenciesAndOutputs dao = new DependenciesAndOutputs();

            if (processOperations != null)
            {
                foreach (var op in processOperations)
                {
                    if (force || !op.DoNotInfer)
                    {
                        switch (op.OpType)
                        {
                            case Operation.Type.WriteFile:
                            case Operation.Type.WriteEnvVariableToFile:
                            case Operation.Type.WriteFileWithRetries:
                                dao.Outputs.Add(op.Path.FileArtifact);
                                break;

                            case Operation.Type.ReadAndWriteFile:
                                dao.Outputs.Add(op.LinkPath.FileArtifact);
                                dao.Dependencies.Add(op.Path.FileArtifact);
                                break;

                            case Operation.Type.CreateHardlink:
                                dao.Dependencies.Add(op.LinkPath.FileArtifact);
                                dao.Outputs.Add(op.Path.FileArtifact);
                                break;

                            case Operation.Type.ReadFile:
                            case Operation.Type.ReadFileFromOtherFile:
                            case Operation.Type.ReadFileIfInputEqual:
                            case Operation.Type.WaitUntilFileExists:
                                dao.Dependencies.Add(op.Path.FileArtifact);
                                break;

                            case Operation.Type.CopyFile:
                                dao.Dependencies.Add(op.Path.FileArtifact);
                                dao.Outputs.Add(op.LinkPath.FileArtifact);
                                break;

                            case Operation.Type.Probe:
                                if (op.Path.IsFile)
                                {
                                    dao.Dependencies.Add(op.Path.FileArtifact);
                                }
                                break;

                            case Operation.Type.CreateSymlink:
                                if (op.LinkPath.IsFile)
                                {
                                    dao.Outputs.Add(op.LinkPath.FileArtifact);
                                }
                                break;

                            case Operation.Type.CreateJunction:
                                if (op.LinkPath.IsFile)
                                {
                                    dao.Outputs.Add(op.LinkPath.FileArtifact);
                                }
                                break;

                            case Operation.Type.Spawn:
                            case Operation.Type.SpawnWithVFork:
                                if (op.Path.IsValid && op.Path.IsFile)
                                {
                                    dao.Outputs.Add(op.Path.FileArtifact);
                                }
                                break;

                            default:
                                break;
                        }
                    }
                }
            }

            return dao;
        }

        public class DependenciesAndOutputs
        {
            public HashSet<FileArtifact> Dependencies = new HashSet<FileArtifact>();
            public HashSet<FileArtifact> Outputs = new HashSet<FileArtifact>();
        }

        /// <summary>
        /// Creates a Process pip from operations. Infers file dependencies/outputs from the operations,
        /// then delegates to <see cref="CreateProcessWithoutInferringAccesses"/>.
        /// </summary>
        protected Process CreateProcess(
            IEnumerable<Operation> processOperations,
            IEnumerable<string> tags = null,
            IEnumerable<EnvironmentVariable> environmentVariables = null,
            IEnumerable<DirectoryArtifact> directoryDependencies = null,
            IEnumerable<DirectoryArtifact> directoryOutputs = null,
            IEnumerable<PipId> orderDependencies = null,
            IEnumerable<AbsolutePath> untrackedPaths = null,
            IEnumerable<AbsolutePath> untrackedScopes = null,
            FileArtifact? stdOut = null,
            FileArtifact? stdError = null,
            PipProvenance provenance = null,
            IEnumerable<AbsolutePath> additionalTempDirectories = null,
            AbsolutePath? tempDirectory = null,
            IEnumerable<FileArtifact> tempFiles = null)
        {
            var dao = InferIOFromOperations(processOperations);

            return CreateProcessWithoutInferringAccesses(
                dependencies: dao.Dependencies,
                outputs: dao.Outputs,
                processOperations: processOperations,
                tags: tags,
                environmentVariables: environmentVariables,
                directoryDependencies: directoryDependencies,
                directoryOutputs: directoryOutputs,
                orderDependencies: orderDependencies,
                untrackedPaths: untrackedPaths,
                untrackedScopes: untrackedScopes,
                stdOut: stdOut,
                stdError: stdError,
                provenance: provenance,
                additionalTempDirectories: additionalTempDirectories,
                tempDirectory: tempDirectory,
                tempFiles: tempFiles);
        }

        /// <summary>
        /// Creates a Process pip without inferring any accesses from processOperations.
        /// This allows the consumer to specify all file accesses explicitly.
        /// </summary>
        protected Process CreateProcessWithoutInferringAccesses(
            IEnumerable<FileArtifact> dependencies,
            IEnumerable<FileArtifact> outputs,
            IEnumerable<Operation> processOperations,
            IEnumerable<string> tags = null,
            IEnumerable<EnvironmentVariable> environmentVariables = null,
            IEnumerable<DirectoryArtifact> directoryDependencies = null,
            IEnumerable<DirectoryArtifact> directoryOutputs = null,
            IEnumerable<PipId> orderDependencies = null,
            IEnumerable<AbsolutePath> untrackedPaths = null,
            IEnumerable<AbsolutePath> untrackedScopes = null,
            FileArtifact? stdOut = null,
            FileArtifact? stdError = null,
            PipProvenance provenance = null,
            IEnumerable<AbsolutePath> additionalTempDirectories = null,
            AbsolutePath? tempDirectory = null,
            IEnumerable<FileArtifact> tempFiles = null)
        {
            Contract.Requires(dependencies != null);
            Contract.Requires(outputs != null);

            FileArtifact executable = TestProcessExecutable;

            untrackedScopes = untrackedScopes ?? new AbsolutePath[0];
            if (tempDirectory != null)
            {
                untrackedScopes = untrackedScopes.Concat(new AbsolutePath[] { (AbsolutePath)tempDirectory });
            }
            untrackedScopes = untrackedScopes.Concat(additionalTempDirectories ?? new AbsolutePath[0]);

            // Add C://Windows dependency for test executable
            untrackedScopes = untrackedScopes.Concat(TestProcessDependencies);

            IEnumerable<FileArtifactWithAttributes> outputsWithAttributes = outputs.Select(o => o.WithAttributes());
            if (tempFiles != null)
            {
                outputsWithAttributes = outputsWithAttributes.Concat(tempFiles.Select(tf => FileArtifactWithAttributes.FromFileArtifact(tf, FileExistence.Temporary)));
            }

            return new Process(
                executable: executable,
                workingDirectory: GetWorkingDirectory(),
                arguments: CreateArguments(processOperations, Context.StringTable),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: environmentVariables != null
                    ? ReadOnlyArray<EnvironmentVariable>.From(environmentVariables)
                    : ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: stdOut ?? FileArtifact.Invalid,
                standardError: stdError ?? FileArtifact.Invalid,
                standardDirectory: GetStandardDirectory(),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.From((new[] { executable }).Concat(dependencies ?? CollectionUtilities.EmptyArray<FileArtifact>())),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.From(outputsWithAttributes),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.From(directoryDependencies ?? new DirectoryArtifact[0]),
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.From(directoryOutputs ?? new DirectoryArtifact[0]),
                orderDependencies: orderDependencies != null ? ReadOnlyArray<PipId>.From(orderDependencies) : ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedPaths ?? ReadOnlyArray<AbsolutePath>.Empty),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                tags: ConvertToStringIdArray(tags),
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: provenance ?? CreateProvenance(),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.From(additionalTempDirectories ?? ReadOnlyArray<AbsolutePath>.Empty),
                tempDirectory: tempDirectory ?? default(AbsolutePath));
        }

        /// <summary>
        /// Returns the working directory for process pips. Override in subclasses for custom behavior.
        /// </summary>
        protected AbsolutePath GetWorkingDirectory() => ObjectRootPath;

        /// <summary>
        /// Returns the standard output/error directory. Override in subclasses for custom behavior.
        /// </summary>
        protected AbsolutePath GetStandardDirectory() => ObjectRootPath;

        /// <summary>
        /// Converts a string enumerable to a ReadOnlyArray of StringIds.
        /// </summary>
        protected ReadOnlyArray<StringId> ConvertToStringIdArray(IEnumerable<string> list)
        {
            return list != null
                ? ReadOnlyArray<StringId>.From(list.Select(tag => StringId.Create(Context.PathTable.StringTable, tag)))
                : ReadOnlyArray<StringId>.Empty;
        }

        /// <summary>
        /// Creates a Process pip from operations using the TestProcess executable.
        /// Includes default OS untracked scopes needed for sandboxed execution.
        /// Override in subclasses to use PipGraphBuilder infrastructure instead.
        /// </summary>
        protected virtual Process ToProcess(params Operation[] operations)
            => ToProcess(testProcessExecutable: default, operations);

        protected virtual Process ToProcess(FileArtifact testProcessExecutable, params Operation[] operations)
        {
            var executable = testProcessExecutable == default ? TestProcessExecutable : testProcessExecutable;
            var dao = InferIOFromOperations(operations);

            var untrackedScopes = new HashSet<AbsolutePath>(TestProcessDependencies);
            foreach (var scope in GetDefaultUntrackedScopes())
            {
                untrackedScopes.Add(scope);
            }
            if (!OperatingSystemHelper.IsUnixOS)
            {
                foreach (var scope in CmdHelper.GetCmdDependencyScopes(Context.PathTable))
                {
                    untrackedScopes.Add(scope);
                }
            }

            return new Process(
                executable: executable,
                workingDirectory: ObjectRootPath,
                arguments: CreateArguments(operations, Context.StringTable),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: ObjectRootPath,
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.From(new[] { executable }.Concat(dao.Dependencies)),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.From(dao.Outputs.Select(o => o.WithAttributes())),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(untrackedScopes),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: CreateProvenance(),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);
        }

        protected FileArtifact CreateTestProcessWithCapabilities()
        {
            var testProcessWithCapabilities = TestProcessExecutable.Path.GetParent(Context.PathTable).Combine(Context.PathTable, TestProcessToolNameWithCapabilties);
            var testProcessWithCapabilitiesString = testProcessWithCapabilities.ToString(Context.PathTable);

            if (File.Exists(testProcessWithCapabilitiesString))
            {
                return FileArtifact.CreateSourceFile(testProcessWithCapabilities);
            }

            File.Copy(TestProcessExecutable.Path.ToString(Context.PathTable), testProcessWithCapabilitiesString, overwrite: false);

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = $"setcap cap_dac_override=eip \"{testProcessWithCapabilitiesString}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                ErrorDialog = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            Assert.True(process != null, "Cannot start process 'sudo'");
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);

            return FileArtifact.CreateSourceFile(testProcessWithCapabilities);
        }

        /// <summary>
        /// Adds common untracked OS directories to a ProcessBuilder.
        /// CODESYNC: PipGraph.Builder.ApplyCurrentOsDefaultsInternal — this does the same thing
        /// (calls UnixDefaults/WindowsOsDefaults.ProcessDefaults) but without requiring a PipGraphBuilder instance.
        /// We avoid pulling in the Scheduler/PipGraphBuilder dependency to keep the module closure small
        /// and prevent net472 from creeping into more assemblies.
        /// </summary>
        protected void AddDefaultOsUntrackedScopes(ProcessBuilder processBuilder)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                processBuilder.EnableTempDirectory();
            }

            processBuilder.AddCurrentHostOSDirectories();

            // Apply OS defaults — equivalent to PipGraphBuilder.ApplyCurrentOsDefaultsInternal(processBuilder, untrackInsteadSourceSeal: true)
            if (OperatingSystemHelper.IsUnixOS)
            {
                new PipGraph.UnixDefaults(Context.PathTable, pipGraph: null)
                    .ProcessDefaults(processBuilder, untrackInsteadSourceSeal: true);
            }
            else
            {
                new PipGraph.WindowsOsDefaults(Context.PathTable)
                    .ProcessDefaults(processBuilder);
            }

            // Code coverage runs cause some side effect accesses under the QTest toolchain.
            string qbitsPath = Environment.GetEnvironmentVariable("QBITSPATH");
            if (!string.IsNullOrWhiteSpace(qbitsPath))
            {
                processBuilder.AddUntrackedDirectoryScope(DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(Context.PathTable, qbitsPath)));
            }
        }

        #endregion

        #region IO Helpers

        public bool Exists(AbsolutePath path)
            => File.Exists(path.ToString(Context.PathTable));

        public void Delete(AbsolutePath path)
            => File.Delete(path.ToString(Context.PathTable));

        #endregion
    }
}
