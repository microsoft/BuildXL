// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Engine;
using BuildXL.Interop;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using System.Threading;
using BuildXL.Scheduler;
using MacPaths = BuildXL.Interop.MacOS.IO;

namespace Test.BuildXL.Scheduler
{
    public abstract class PipTestBase : TemporaryStorageTestBase
    {
        protected const string SourceRootPrefix = "src";
        protected const string ObjectRootPrefix = "obj";
        protected const string RedirectedRootPrefix = "redirected";
        protected const string CacheRootPrefix = ".cache";

        private const string WarningRegexDescription = "WARNING";

        /// <summary>
        /// Test process tool base name name
        /// </summary>
        protected const string TestProcessToolNameWithoutExtension = "Test.BuildXL.Executables.TestProcess";

        /// <summary>
        /// Value if for created pip.
        /// </summary>
        protected const string PipValuePrefix = "MyValue_";

        /// <summary>
        /// Description for seal-directory pip.
        /// </summary>
        protected const string SealDirectoryDescription = "SEAL";

        /// <summary>
        /// Description for copy-file pip.
        /// </summary>
        protected const string CopyFileDescription = "COPY";

        /// <summary>
        /// Description for write-file pip.
        /// </summary>
        protected const string WriteFileDescription = "WRITE";

        /// <summary>
        /// FileArtifact for cmd.exe
        /// </summary>
        protected readonly FileArtifact CmdExecutable;

        /// <summary>
        /// FileArtifact for generic TestProcess.exe
        /// </summary>
        protected readonly FileArtifact TestProcessExecutable;

        protected readonly AbsolutePath[] TestProcessDependencies;

        /// <summary>
        /// Test process tool name
        /// </summary>
        protected string TestProcessToolName => OperatingSystemHelper.IsUnixOS
            ? TestProcessToolNameWithoutExtension
            : TestProcessToolNameWithoutExtension + ".exe";

        /// <summary>
        /// Context
        /// </summary>
        protected BuildXLContext Context;

        /// <summary>
        /// Fresh id for pip.
        /// </summary>
        private int m_pipFreshId;

        /// <summary>
        /// ID used to generate filenames. Incremented for each new name.
        /// </summary>
        private int m_uniqueFileId;

        /// <summary>
        /// Directory containing temporary 'source' files.
        /// </summary>
        protected string SourceRoot { get; private set; }

        /// <summary>
        /// Directory containing temporary 'output' files.
        /// </summary>
        protected string ObjectRoot { get; private set; }

        /// <summary>
        /// Directory containing redirected 'output' files.
        /// </summary>
        protected string RedirectedRoot { get; private set; }

        /// <summary>
        /// Directory containing temporary cache.
        /// </summary>
        protected string CacheRoot { get; private set; }

        protected string ReadonlyRoot;
        protected string NonHashableRoot;
        protected string NonReadableRoot;
        protected string TestBinRoot;

        /// <summary>
        /// Absolute path for ObjectRoot.
        /// </summary>
        protected AbsolutePath ObjectRootPath { get; set; }

        protected AbsolutePath SourceRootPath { get; set; }

        protected AbsolutePath TestBinRootPath { get; set; }

        protected QualifierTable QualifierTable { get; private set; }

        protected MountPathExpander Expander { get; set; }

        protected PipTable PipTable { get; private set; }

        private static long s_pipIdCounter = 1;

        protected static long GetNextPipId() { return Interlocked.Increment(ref s_pipIdCounter); }

        /// <summary>
        /// The pip graph builder.
        /// </summary>
        protected virtual PipGraph.Builder PipGraphBuilder { get; private set; }

        // Lazily created packing fields. Use the properties instead of these directly
        private PipConstructionHelper m_pipConstructionHelper;

        protected PipConstructionHelper PipConstructionHelper
        {
            get
            {
                if (m_pipConstructionHelper == null)
                {
                    var objectRoot = AbsolutePath.Create(Context.PathTable, ObjectRoot);
                    var redirectedRoot = AbsolutePath.Create(Context.PathTable, RedirectedRoot);

                    ModuleId moduleId = new ModuleId(1);
                    var specPath = objectRoot.Combine(Context.PathTable, "spec.dsc");

                    PipGraphBuilder.AddModule(ModulePip.CreateForTesting(Context.StringTable, specPath, moduleId));
                    PipGraphBuilder.AddSpecFile(new SpecFilePip(new FileArtifact(specPath), new LocationData(specPath, 0, 0), moduleId));

                    m_pipConstructionHelper = PipConstructionHelper.CreateForTesting(
                        Context,
                        objectRoot: objectRoot,
                        redirectedRoot: redirectedRoot,
                        pipGraph: PipGraphBuilder,
                        specPath: specPath);
                }

                return m_pipConstructionHelper;
            }
        }

        /// <nodoc />
        public ProcessBuilder CreatePipBuilder(IEnumerable<Operation> processOperations, IEnumerable<string> tags = null, string description = null)
        {
            var builder = ProcessBuilder.CreateForTesting(Context.PathTable);
            builder.Executable = TestProcessExecutable;
            builder.AddInputFile(TestProcessExecutable);
            AddUntrackedWindowsDirectories(builder);

            // When symlinks are involved, TestProcess.exe can access C:\ProgramData\Microsoft\NetFramework\BreadcrumbStore
            builder.AddUntrackedProgramDataDirectories();

            // When symlinks are involved, TestProcess.exe can write to temp directory to test symlink creation.
            builder.EnableTempDirectory();

            CreateArguments(builder.ArgumentsBuilder, processOperations, Context.StringTable);
            var operations = InferIOFromOperations(processOperations);

            foreach (var dependency in operations.Dependencies)
            {
                builder.AddInputFile(dependency);
            }

            foreach (var output in operations.Outputs)
            {
                builder.AddOutputFile(new FileArtifact(output.Path, output.RewriteCount - 1), FileExistence.Required);
            }

            if (tags != null)
            {
                builder.AddTags(Context.StringTable, tags.ToArray());
            }

            if (description != null)
            {
                builder.ToolDescription = StringId.Create(Context.StringTable, description);
            }

            if (OperatingSystemHelper.IsUnixOS)
            {
                builder.SetEnvironmentVariable(
                    StringId.Create(Context.StringTable, "DYLD_LIBRARY_PATH"),
                    Path.GetDirectoryName(TestProcessExecutable.Path.ToString(Context.PathTable)));

                // untracking this directory as well because dynamic probes are non-deterministic which
                // can cause some of our FingerprintStore tests to fail.
                builder.AddUntrackedDirectoryScope(TestProcessExecutable.Path.GetParent(Context.PathTable));
            }

            return builder;
        }

        /// <summary>
        /// Resets the <see cref="PipGraphBuilder"/> and <see cref="PipTable"/> Structures to create a new graph for
        /// a future scheduler iteration in this test. Note that this does not reset the underlying <see cref="PathTable"/>
        /// so graph based enumerations won't be impacted by paths that are removed.
        /// </summary>
        public void ResetPipGraphBuilder(IConfiguration configuration = null)
        {
            BaseSetup(configuration);
            m_pipConstructionHelper = null;
        }

        protected PipTestBase(ITestOutputHelper output) : base(output)
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

            CacheRoot = Path.Combine(TemporaryDirectory, CacheRootPrefix);

            BaseSetup();
        }

        protected void BaseSetup(IConfiguration configuration = null, bool disablePipSerialization = false)
        {
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(ObjectRoot);

            var pathTable = Context.PathTable;
            var stringTable = Context.StringTable;

            PipTable = new PipTable(
                pathTable,
                Context.SymbolTable,
                initialBufferSize: 16,
                maxDegreeOfParallelism: disablePipSerialization ? 0 : Environment.ProcessorCount,
                debug: true);

            QualifierTable = new QualifierTable(Context.StringTable);
            Expander = new MountPathExpander(pathTable);
            configuration = configuration ?? new ConfigurationImpl();

            PipGraphBuilder = new PipGraph.Builder(
                PipTable,
                Context,
                global::BuildXL.Scheduler.Tracing.Logger.Log,
                LoggingContext,
                configuration,
                Expander,
                fingerprintSalt: configuration.Cache.CacheSalt,
                directoryMembershipFingerprinterRules: new DirectoryMembershipFingerprinterRuleSet(configuration, stringTable));

            ReadonlyRoot = Path.Combine(ObjectRoot, "readonly");
            NonHashableRoot = Path.Combine(ObjectRoot, "nonhashable");
            NonReadableRoot = Path.Combine(ObjectRoot, "nonreadable");

            Directory.CreateDirectory(ReadonlyRoot);
            Directory.CreateDirectory(NonHashableRoot);
            Directory.CreateDirectory(NonReadableRoot);

            Expander.Add(pathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(stringTable, "SourceRoot"),
                root: SourceRootPath,
                allowHashing: true,
                readable: true,
                writable: true));

            Expander.Add(pathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(stringTable, "ObjectRoot"),
                root: ObjectRootPath,
                allowHashing: true,
                readable: true,
                writable: true));

            Expander.Add(pathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(stringTable, "NonReadableRoot"),
                root: AbsolutePath.Create(pathTable, NonReadableRoot),
                allowHashing: false,
                readable: false,
                writable: false));

            Expander.Add(pathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(stringTable, "ReadOnlyRoot"),
                root: AbsolutePath.Create(pathTable, ReadonlyRoot),
                allowHashing: true,
                readable: true,
                writable: false));

            Expander.Add(pathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(stringTable, "NonHashableRoot"),
                root: AbsolutePath.Create(pathTable, NonHashableRoot),
                allowHashing: false,
                readable: true,
                writable: true));

            Expander.Add(pathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(stringTable, "CmdRoot"),
                root: CmdExecutable.Path.GetParent(pathTable),
                allowHashing: false,
                readable: true,
                writable: true));

            Expander.Add(pathTable, new SemanticPathInfo(
                rootName: PathAtom.Create(stringTable, nameof(TestBinRoot)),
                root: TestBinRootPath,
                allowHashing: true,
                readable: true,
                writable: false));

            if (OperatingSystemHelper.IsUnixOS)
            {
                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "Applications"),
                    root: AbsolutePath.Create(pathTable, MacPaths.Applications),
                    allowHashing: true,
                    readable: true,
                    writable: false));

                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "UsrBin"),
                    root: AbsolutePath.Create(pathTable, MacPaths.UsrBin),
                    allowHashing: true,
                    readable: true,
                    writable: false));

                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "UsrInclude"),
                    root: AbsolutePath.Create(pathTable, MacPaths.UsrInclude),
                    allowHashing: true,
                    readable: true,
                    writable: false));

                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "UsrLib"),
                    root: AbsolutePath.Create(pathTable, MacPaths.UsrLib),
                    allowHashing: true,
                    readable: true,
                    writable: false));

                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "Library"),
                    root: AbsolutePath.Create(pathTable, MacPaths.Library),
                    allowHashing: true,
                    readable: true,
                    writable: false));

                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "UserProvisioning"),
                    root: AbsolutePath.Create(pathTable, MacPaths.UserProvisioning),
                    allowHashing: true,
                    readable: true,
                    writable: false));
            }
        }

        /// <summary>
        /// Creates a unique environment variable given a value.
        /// </summary>
        protected EnvironmentVariable CreateUniqueEnvironmentVariable(string value)
        {
            Contract.Requires(value != null);
            return new EnvironmentVariable(
                StringId.Create(Context.PathTable.StringTable, "ENV" + Guid.NewGuid().ToString().Replace("-", string.Empty)),
                PipDataBuilder.CreatePipData(Context.PathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, value));
        }

        /// <summary>
        /// Creates a seal directory pip.
        /// </summary>
        protected SealDirectory CreateSealDirectory(AbsolutePath directoryPath, IEnumerable<FileArtifact> fileArtifacts, bool partial = false, IEnumerable<string> tags = null)
        {
            var sortedFileArtifacts = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(
                fileArtifacts,
                OrdinalFileArtifactComparer.Instance);
            return new SealDirectory(
                directoryPath,
                sortedFileArtifacts,
                partial ? SealDirectoryKind.Partial : SealDirectoryKind.Full,
                CreateProvenance(StringId.Create(Context.PathTable.StringTable, SealDirectoryDescription)),
                ConvertToStringIdArray(tags),
                ReadOnlyArray<StringId>.Empty);
        }

        /// <summary>
        /// Creates a copy file pip.
        /// </summary>
        protected CopyFile CreateCopyFile(FileArtifact sourceFile, FileArtifact targetFile, string descriptionQualifier = "", string[] tags = null)
        {
            Contract.Requires(sourceFile.IsValid, "Argument sourceFile must be valid");
            Contract.Requires(targetFile.IsValid, "Argument targetFile must be valid");

            string description = string.Join(" ", CopyFileDescription + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture), descriptionQualifier);
            return new CopyFile(
                sourceFile,
                targetFile,
                ConvertToStringIdArray(tags),
                CreateProvenance(StringId.Create(Context.PathTable.StringTable, description)));
        }

        /// <summary>
        /// Creates and schedules a copy file pip.
        /// </summary>
        protected CopyFile CreateAndScheduleCopyFile(FileArtifact sourceFile, FileArtifact targetFile, string descriptionQualifier = "", string[] tags = null)
        {
            Contract.Requires(sourceFile.IsValid, "Argument sourceFile must be valid");
            Contract.Requires(targetFile.IsValid, "Argument targetFile must be valid");

            var copyFile = CreateCopyFile(sourceFile, targetFile, descriptionQualifier, tags);
            var added = PipGraphBuilder.AddCopyFile(copyFile);
            XAssert.IsTrue(added);

            return copyFile;
        }

        /// <summary>
        /// Creates a write pip.
        /// </summary>
        protected WriteFile CreateWriteFile(
            FileArtifact targetFile,
            string separator,
            IEnumerable<string> content,
            string[] tags = null)
        {
            Contract.Requires(targetFile.IsValid, "Argument targetFile must be valid");
            Contract.Requires(content != null, "Argument content cannot be null");

            PipData pipData = PipDataBuilder.CreatePipData(Context.PathTable.StringTable, separator, PipDataFragmentEscaping.NoEscaping, content.Select(c => (PipDataAtom)c).ToArray());
            string description = WriteFileDescription + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture);

            return new WriteFile(
                targetFile,
                pipData,
                WriteFileEncoding.Utf8,
                tags != null
                    ? ReadOnlyArray<StringId>.From(tags.Select(tag => StringId.Create(Context.PathTable.StringTable, tag)))
                    : ReadOnlyArray<StringId>.Empty,
                CreateProvenance(StringId.Create(Context.PathTable.StringTable, description)));
        }

        /// <summary>
        /// Creates and schedules a write file pip.
        /// </summary>
        protected WriteFile CreateAndScheduleWriteFile(
            FileArtifact targetFile,
            string separator,
            IEnumerable<string> content,
            string[] tags = null)
        {
            var writeFile = CreateWriteFile(targetFile, separator, content, tags);
            var added = PipGraphBuilder.AddWriteFile(writeFile);
            XAssert.IsTrue(added);

            return writeFile;
        }

        protected FileArtifact GetCmdExecutable()
        {
            return CmdExecutable;
        }

        protected AbsolutePath GetWorkingDirectory()
        {
            return ObjectRootPath;
        }

        protected AbsolutePath GetStandardDirectory()
        {
            return ObjectRootPath;
        }

        protected AbsolutePath Combine(AbsolutePath root, params PathAtom[] atoms)
            => root.Combine(Context.PathTable, atoms);

        protected AbsolutePath Combine(AbsolutePath root, params string[] atoms)
            => Combine(root, atoms.Select(a => PathAtom.Create(Context.StringTable, a)).ToArray());

        /// <see cref="CreateSourceFileWithPrefix(string, string)"/>
        protected FileArtifact CreateSourceFile(AbsolutePath root, string prefix = null)
            => CreateSourceFileWithPrefix(root: root.ToString(Context.PathTable), prefix: prefix);

        /// <see cref="CreateSourceFileWithPrefix(string, string)"/>
        protected FileArtifact CreateSourceFile(string root = null)
            => CreateSourceFileWithPrefix(root: root, prefix: SourceRootPrefix);

        /// <summary>
        /// Creates a source artifact and populates it with <see cref="WriteSourceFile"/>.
        /// Creating a backing file is necessary since source artifacts must exist at the beginning of a build.
        ///
        /// If <paramref name="root"/> is not specified, <see cref="SourceRoot"/> is used as the parent directory.
        ///
        /// If <paramref name="prefix"/> is not specified, <see cref="SourceRootPrefix"/> is used as the file name prefix.
        /// </summary>
        protected FileArtifact CreateSourceFileWithPrefix(string root = null, string prefix = null)
        {
            FileArtifact sourceFile = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(prefix ?? SourceRootPrefix, root));
            WriteSourceFile(sourceFile);
            return sourceFile;
        }

        /// <summary>
        /// Creates a file for use as source (it can exist before the scheduler starts).
        /// Intermediate directories are created as needed.
        /// </summary>
        protected void WriteSourceFile(FileArtifact artifact)
        {
            Contract.Requires(artifact.IsValid);

            string fullPath = artifact.Path.ToString(Context.PathTable);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, Guid.NewGuid().ToString());
        }

        /// <summary>
        /// Creates a unique directory.
        /// </summary>
        protected AbsolutePath CreateUniqueDirectory(AbsolutePath root, string prefix = null) => CreateUniqueDirectory(root.ToString(Context.PathTable), prefix);

        protected AbsolutePath CreateUniqueDirectory(string root = null, string prefix = null)
        {
            AbsolutePath path = CreateUniqueSourcePath(prefix ?? SourceRootPrefix, root);
            Directory.CreateDirectory(path.ToString(Context.PathTable));
            return path;
        }

        /// <summary>
        /// Creates a unique directory and wraps it in a <see cref="DirectoryArtifact"/>.
        /// </summary>
        protected DirectoryArtifact CreateUniqueDirectoryArtifact(string root = null, string prefix = null) 
            => DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(root, prefix));

        /// <summary>
        /// Creates a file artifact with the given name under the given root
        /// </summary>
        public FileArtifact CreateFileArtifactWithName(string name, string root)
        {
            AbsolutePath filePath;
            AbsolutePath.TryCreate(Context.PathTable, Path.Combine(root, name), out filePath);
            return new FileArtifact(filePath);
        }

        /// <summary>
        /// Creates an output artifact without creating the backing file. Output artifacts may be created by scheduled pips.
        /// </summary>
        protected FileArtifact CreateOutputFileArtifact(string root = null, string prefix = null)
        {
            return FileArtifact.CreateSourceFile(CreateUniqueObjPath(prefix ?? "obj", root)).CreateNextWrittenVersion();
        }

        /// <summary>
        /// Creates an output artifact without creating the backing file. Output artifacts may be created by scheduled pips.
        /// </summary>
        protected FileArtifact CreateOutputFileArtifact(AbsolutePath root, string prefix = null)
        {
            return CreateOutputFileArtifact(root.ToString(Context.PathTable), prefix);
        }

        /// <summary>
        /// Creates an output directory artifact without creating the backing directory. Output artifacts may be created by scheduled pips.
        /// </summary>
        protected DirectoryArtifact CreateOutputDirectoryArtifact(string root = null)
        {
            return OutputDirectory.Create(CreateUniqueObjPath("obj", root));
        }

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
        {
            Contract.Requires(prefix != null);
            Contract.Requires(root != null);

            string uniqueFilePath = Path.Combine(root, string.Format(CultureInfo.InvariantCulture, "{0}_{1}", prefix, m_uniqueFileId++));
            return AbsolutePath.Create(Context.PathTable, uniqueFilePath);
        }

        protected PipProvenance CreateProvenance(AbsolutePath? specPath = null)
        {
            return CreateProvenance(StringId.Invalid, specPath: specPath);
        }

        protected PipProvenance CreateProvenance(StringId usage, AbsolutePath? specPath = null)
        {
            return CreateProvenance(
                Context,
                PipGraphBuilder,
                usage,
                specPath ?? CreateUniquePath("spec", SourceRoot),
                PipValuePrefix + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture));
        }

        protected PipProvenance CreateProvenance(IPipGraph pipGraph, string value = null, string usage = null)
        {
            value = value ?? PipValuePrefix + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture);

            return PipTestBase.CreateProvenance(Context, pipGraph, usage != null ? StringId.Create(Context.StringTable, usage) : StringId.Invalid, CreateUniqueSourcePath(SourceRoot), value);
        }

        /// <summary>
        /// Creates provenance.
        /// </summary>
        public static PipProvenance CreateProvenance(
            BuildXLContext context,
            IPipGraph pipGraph,
            StringId usage,
            AbsolutePath specFile,
            string valueName)
        {
            var provenance = new PipProvenance(
                0,
                ModuleId.Invalid,
                StringId.Invalid,
                FullSymbol.Create(context.SymbolTable, valueName),
                new LocationData(specFile, 1, 1),
                QualifierId.Unqualified,
                usage.IsValid ? PipDataBuilder.CreatePipData(context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, usage) : PipData.Invalid);

            if (pipGraph != null)
            {
                AddMetaPips(context, provenance, pipGraph);
            }

            return provenance;
        }

        public static void AddMetaPips(PipExecutionContext context, PipProvenance provenance, IPipGraph pipGraph)
        {
            var moduleId = new ModuleId(1, "TestModuleId");
            var locationData = new LocationData(provenance.Token.Path, 0, 0);

            pipGraph.AddModule(ModulePip.CreateForTesting(context.StringTable, provenance.Token.Path, moduleId));
            pipGraph.AddSpecFile(new SpecFilePip(new FileArtifact(provenance.Token.Path), locationData, moduleId));
            pipGraph.AddOutputValue(new ValuePip(provenance.OutputValueSymbol, QualifierId.Unqualified, locationData));
        }

        private ReadOnlyArray<StringId> ConvertToStringIdArray(IEnumerable<string> list)
        {
            return list != null
                ? ReadOnlyArray<StringId>.From(list.Select(tag => StringId.Create(Context.PathTable.StringTable, tag)))
                : ReadOnlyArray<StringId>.Empty;
        }

        protected SealDirectory CreateSealDirectory(AbsolutePath root, SealDirectoryKind sealDirectoryKind, params FileArtifact[] files)
        {
            return new SealDirectory(
                directoryRoot: root,
                contents: SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(files, OrdinalFileArtifactComparer.Instance),
                kind: sealDirectoryKind,
                provenance: CreateProvenance(),
                tags: ReadOnlyArray<StringId>.Empty,
                patterns: ReadOnlyArray<StringId>.Empty);
        }

        protected SealDirectory CreateAndScheduleSealDirectory(AbsolutePath root, SealDirectoryKind sealDirectoryKind, params FileArtifact[] files)
        {
            var seal = CreateSealDirectory(root, sealDirectoryKind, files);
            PipGraphBuilder.AddSealDirectory(seal);
            return seal;
        }

        protected SealDirectory CreateSealDirectory(AbsolutePath root, SealDirectoryKind sealDirectoryKind, string tag, params FileArtifact[] files)
        {
            return new SealDirectory(
                directoryRoot: root,
                contents: SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(files, OrdinalFileArtifactComparer.Instance),
                kind: sealDirectoryKind,
                provenance: CreateProvenance(),
                tags: ConvertToStringIdArray(new[] { tag }),
                patterns: ReadOnlyArray<StringId>.Empty);
        }

        protected SealDirectory CreateSourceSealDirectory(AbsolutePath root, SealDirectoryKind sealDirectoryKind, params string[] patterns)
        {
            Contract.Requires(sealDirectoryKind.IsSourceSeal());
            return new SealDirectory(
                directoryRoot: root,
                contents: SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(
                    CollectionUtilities.EmptyArray<FileArtifact>(),
                    OrdinalFileArtifactComparer.Instance),
                kind: sealDirectoryKind,
                provenance: CreateProvenance(),
                tags: ReadOnlyArray<StringId>.Empty,
                patterns: ConvertToStringIdArray(patterns));
        }

        protected DirectoryArtifact CreateAndScheduleSealDirectoryArtifact(AbsolutePath root, SealDirectoryKind sealDirectoryKind, params FileArtifact[] files)
        {
            SealDirectory sealedDirectory = CreateSealDirectory(root, sealDirectoryKind, files);
            DirectoryArtifact dir = PipGraphBuilder.AddSealDirectory(sealedDirectory);
            XAssert.IsTrue(dir.IsValid);
            return dir;
        }

        /// <summary>
        /// Creates arguments.
        /// </summary>
        internal static PipData CreateCmdArguments(
            StringTable stringTable,
            IEnumerable<FileArtifact> dependencies,
            IEnumerable<FileArtifact> outputs,
            IEnumerable<StaticDirectory> directoryDependencies = null,
            bool includeWarning = false)
        {
            Contract.Requires(dependencies != null, "Argument dependencies cannot be null");
            Contract.Requires(outputs != null, "Argument outputs cannot be null");

            var pipDataBuilder = new PipDataBuilder(stringTable);
            int i = 0;

            pipDataBuilder.Add("/d");
            pipDataBuilder.Add("/c");

            foreach (FileArtifact output in outputs)
            {
                if (i > 0)
                {
                    pipDataBuilder.Add("&");
                }

                using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    var allDependencies = dependencies.ToList();

                    if (directoryDependencies != null)
                    {
                        foreach (var directoryDependency in directoryDependencies)
                        {
                            allDependencies.AddRange(directoryDependency.Contents);
                        }
                    }

                    pipDataBuilder.Add("type");
                    foreach (FileArtifact dependency in allDependencies)
                    {
                        pipDataBuilder.Add(dependency);
                    }

                    pipDataBuilder.Add(">");
                    pipDataBuilder.Add(output);
                }

                pipDataBuilder.Add("&");

                using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    pipDataBuilder.Add("echo");
                    pipDataBuilder.Add("BuildXL");
                    pipDataBuilder.Add(">>");
                    pipDataBuilder.Add(output);
                }

                i++;
            }

            if (includeWarning)
            {
                if (outputs.Any())
                {
                    pipDataBuilder.Add("&&");
                }

                using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                {
                    pipDataBuilder.Add("echo");
                    pipDataBuilder.Add(WarningRegexDescription);
                }
            }

            return pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
        }

        /// <summary>
        /// Creates a process pip.
        /// </summary>
        protected Process CreateCmdProcess(
            IEnumerable<FileArtifact> dependencies,
            IEnumerable<FileArtifact> outputs,
            PipData? arguments = null,
            IEnumerable<string> tags = null,
            PipProvenance provenance = null,
            IEnumerable<DirectoryArtifact> directoryDependencies = null,
            IEnumerable<PipId> orderDependencies = null,
            IEnumerable<EnvironmentVariable> environmentVariables = null,
            FileArtifact? stdOut = null,
            FileArtifact? stdError = null,
            bool withWarning = false,
            string value = null,
            IPipGraph pipGraph = null,
            IEnumerable<FileArtifact> untrackedFiles = null,
            IEnumerable<DirectoryArtifact> directoryOutputs = null)
        {
            Contract.Requires(dependencies != null);
            Contract.Requires(outputs != null);

            FileArtifact executable = CmdExecutable;

            IEnumerable<AbsolutePath> untrackedDependencies = CmdHelper.GetCmdDependencies(Context.PathTable);
            if (untrackedFiles != null)
            {
                List<AbsolutePath> files = new List<AbsolutePath>(untrackedDependencies);
                foreach (FileArtifact oneFile in untrackedFiles)
                {
                    files.Add(oneFile.Path);
                }
                untrackedDependencies = files;
            }

            untrackedFiles = untrackedFiles ?? Enumerable.Empty<FileArtifact>();

            var process =
                new Process(
                    executable: executable,
                    workingDirectory: GetWorkingDirectory(),
                    arguments: arguments ?? CreateCmdArguments(Context.StringTable, dependencies.Concat(untrackedFiles), outputs, includeWarning: withWarning),
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

                    // TODO:1759: Fix response file handling. Should be able to appear in the dependencies list, but should appear in the graph as a WriteFile pip.
                    dependencies: ReadOnlyArray<FileArtifact>.From((new[] { executable /*, responseFile*/}).Concat(dependencies ?? CollectionUtilities.EmptyArray<FileArtifact>())),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.From(outputs.Select(o => o.WithAttributes()).ToArray()),
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.From(directoryDependencies ?? new DirectoryArtifact[0]),
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.From(directoryOutputs ?? new DirectoryArtifact[0]),
                    orderDependencies: orderDependencies != null ? ReadOnlyArray<PipId>.From(orderDependencies) : ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(untrackedDependencies),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(Context.PathTable)),
                    tags: ConvertToStringIdArray(tags),
                    successExitCodes: ReadOnlyArray<int>.Empty,
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: provenance ?? CreateProvenance(pipGraph: pipGraph, value: value, usage: null),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    warningRegex:
                        withWarning
                            ? new RegexDescriptor(StringId.Create(Context.StringTable, WarningRegexDescription), RegexOptions.IgnoreCase)
                            : default(RegexDescriptor));

            return process;
        }

        /// <summary>
        /// Creates arguments.
        /// </summary>
        protected PipData CreateArguments(
            IEnumerable<Operation> processOperations,
            StringTable stringTable)
        {
            var pipDataBuilder = new PipDataBuilder(stringTable);
            CreateArguments(pipDataBuilder, processOperations, stringTable);
            return pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
        }

        /// <summary>
        /// Creates arguments.
        /// </summary>
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
        /// Creates a process pip.
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
            IEnumerable<FileArtifact> tempFiles = null /* converted to FileArtifactWithAttributes with FileExistence.Temporary, then concatenated to outputs */)
        {
            FileArtifact executable = TestProcessExecutable;

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
        /// Creates a process pip without inferring any accesses from processOperations. This allows the consumer to specify
        /// all file acceses
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
            IEnumerable<FileArtifact> tempFiles = null /* converted to FileArtifactWithAttributes with FileExistence.Temporary, then concatenated to outputs */)
        {
            Contract.Requires(dependencies != null);
            Contract.Requires(outputs != null);

            FileArtifact executable = TestProcessExecutable;

            untrackedScopes = untrackedScopes ?? new AbsolutePath[0];
            if (tempDirectory != null)

            // Make temp directories untracked scopes to mimic pips built through PipBuilder
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

            var process =
                new Process(
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
                    tempDirectory: tempDirectory ?? default(AbsolutePath)
                    );

            return process;
        }

        /// <summary>
        /// Naively infers the dependencies and outputs of the given <see cref="Operation"/>s in processOperations
        /// where <see cref="Operation.DoNotInfer"/> is false. Assumes that test processes will not consume their own outputs.
        /// </summary>
        /// <returns>Struct containing inferred dependencies and outputs that can be adjusted if incorrect</returns>
        protected DependenciesAndOutputs InferIOFromOperations(IEnumerable<Operation> processOperations, bool force = false)
        {
            DependenciesAndOutputs dao = new DependenciesAndOutputs();

            if (processOperations != null)
            {
                foreach (var op in processOperations)
                {
                    if (force || !op.DoNotInfer)
                    {
                        // Assumes that a test process will not execute any actions dependent
                        // on its own output within its own output (i.e. WriteFile(a) then ReadFile(a))
                        switch (op.OpType)
                        {
                            case Operation.Type.WriteFile:
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

                            default:
                                break;
                        }
                    }
                }
            }

            return dao;
        }

        // Allows inferred dependencies and outputs to be returned and adjusted
        public class DependenciesAndOutputs
        {
            public HashSet<FileArtifact> Dependencies;
            public HashSet<FileArtifact> Outputs;

            public DependenciesAndOutputs()
            {
                Dependencies = new HashSet<FileArtifact>();
                Outputs = new HashSet<FileArtifact>();
            }
        }

        /// <summary>
        /// Validates the count of pips that fall into various buckets in the newest status line logged. Note this is
        /// only valid to call if the scheduler has run and has logged a final status update
        /// </summary>
        public void AssertLatestProcessPipCounts(int succeeded = 0, int failed = 0, int skipped = 0, int hit = 0)
        {
            string[] logLines = EventListener.GetLog().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            bool performedValidation = false;
            for (int i = logLines.Length - 1; i > -1; i--)
            {
                string line = logLines[i];
                if (line.Contains("Procs:"))
                {
                    performedValidation = true;
                    Regex regex = new Regex(@"Procs: (?<succeeded>\d*) succeeded \((?<hit>\d*)\) hit, (?<failed>\d*) failed, (?<skipped>\d*) skipped, (?<executiong>\d*) executing, (?<pending>\d*) pending, (?<waiting>\d*) waiting");
                    Match match = regex.Match(line);
                    XAssert.AreEqual(succeeded.ToString(), match.Groups["succeeded"].Value);
                    XAssert.AreEqual(failed.ToString(), match.Groups["failed"].Value);
                    XAssert.AreEqual(skipped.ToString(), match.Groups["skipped"].Value);
                    XAssert.AreEqual(hit.ToString(), match.Groups["hit"].Value);

                    performedValidation = true;
                    break;
                }
            }

            XAssert.IsTrue(performedValidation, "Did not find log line matching pattern to perform validation");
        }

        protected Process ToProcess(params Operation[] operations)
        {
            var processBuilder = CreatePipBuilder(operations);
            AddUntrackedWindowsDirectories(processBuilder);
            var ok = processBuilder.TryFinish(PipConstructionHelper, out var process, out var _);
            XAssert.IsTrue(ok, "Could not finish creating process builder");
            return process;
        }

        protected void AddUntrackedWindowsDirectories(ProcessBuilder processBuilder)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                processBuilder.EnableTempDirectory();
            }

            processBuilder.AddCurrentHostOSDirectories();

            PipGraphBuilder.ApplyCurrentOsDefaults(processBuilder);

        }

        #region IO Helpers

        public bool Exists(AbsolutePath path)
        {
            return File.Exists(path.ToString(Context.PathTable));
        }

        public void Delete(AbsolutePath path)
        {
            File.Delete(path.ToString(Context.PathTable));
        }

#endregion
    }
}
