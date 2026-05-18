// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BuildXL.Engine;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Interop;
using BuildXL.Interop.Unix;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core.Qualifier;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

using Process = BuildXL.Pips.Operations.Process;
using BuildXL.Utilities;
using BuildXL.Scheduler.Fingerprints;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Base class for Scheduler-level tests. Extends ProcessesTestBase with Scheduler-specific
    /// infrastructure: PipGraph, PipTable, MountPathExpander, FrontEndContext, PipConstructionHelper,
    /// and higher-level pip creation/scheduling methods.
    /// </summary>
    public abstract class PipTestBase : ProcessesTestBase
    {
        protected const string CacheRootPrefix = ".cache";

        private const string WarningRegexDescription = "WARNING";

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
        /// Directory containing temporary cache.
        /// </summary>
        protected string CacheRoot { get; private set; }

        protected string ReadonlyRoot;
        protected string NonHashableRoot;
        protected string NonReadableRoot;

        protected QualifierTable QualifierTable { get; private set; }

        protected MountPathExpander Expander { get; set; }

        protected PipTable PipTable { get; private set; }

        /// <summary>
        /// The pip graph builder.
        /// </summary>
        protected virtual PipGraph.Builder PipGraphBuilder { get; private set; }

        protected FrontEndContext FrontEndContext { get; private set; }

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

                    var specPath = objectRoot.Combine(Context.PathTable, "spec.dsc");

                    var modulePip = ModulePip.CreateForTesting(Context.StringTable, specPath);
                    PipGraphBuilder.AddModule(modulePip);
                    PipGraphBuilder.AddSpecFile(new SpecFilePip(new FileArtifact(specPath), new LocationData(specPath, 0, 0), modulePip.Module));

                    m_pipConstructionHelper = PipConstructionHelper.CreateForTesting(
                        Context,
                        objectRoot: objectRoot,
                        redirectedRoot: redirectedRoot,
                        pipGraph: PipGraphBuilder,
                        specPath: specPath,
                        moduleName: modulePip.Identity.ToString(Context.StringTable));
                }

                return m_pipConstructionHelper;
            }
        }

        /// <nodoc />
        public ProcessBuilder CreatePipBuilder(
            IEnumerable<Operation> processOperations,
            IEnumerable<string> tags = null,
            string description = null,
            IDictionary<string, string> environmentVariables = null,
            IEnumerable<int> succeedFastExitCodes = null,
            ProcessBuilder builder = null,
            FileArtifact testProcessExecutable = default)
        {
            var envVars = environmentVariables?.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value, false));
            return CreatePipBuilderWithEnvironment(processOperations, tags, description, envVars, succeedFastExitCodes, builder, testProcessExecutable);
        }

        /// <nodoc />
        public ProcessBuilder CreatePipBuilderWithEnvironment(
            IEnumerable<Operation> processOperations,
            IEnumerable<string> tags = null,
            string description = null,
            IDictionary<string, (string, bool)> environmentVariables = null,
            IEnumerable<int> succeedFastExitCodes = null,
            ProcessBuilder builder = null,
            FileArtifact testProcessExecutable = default)
        {
            testProcessExecutable = testProcessExecutable == default ? TestProcessExecutable : testProcessExecutable;
            builder ??= ProcessBuilder.CreateForTesting(Context.PathTable, FrontEndContext.CredentialScanner, LoggingContext);
            builder.Executable = testProcessExecutable;
            if (succeedFastExitCodes != null)
            {
                builder.SucceedFastExitCodes = ReadOnlyArray<int>.From(succeedFastExitCodes);
                builder.SuccessExitCodes = ReadOnlyArray<int>.From(succeedFastExitCodes);
            }

            builder.AddInputFile(testProcessExecutable);
            AddDefaultOsUntrackedScopes(builder);

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

            if (environmentVariables != null)
            {
                foreach (var envVar in environmentVariables)
                {
                    if (envVar.Value.Item1 is null)
                    {
                        builder.SetEnvironmentVariable(
                            StringId.Create(Context.StringTable, envVar.Key),
                            PipData.Invalid,
                            isPassThrough: envVar.Value.Item2);
                    }
                    else
                    {
                        builder.SetEnvironmentVariable(
                            StringId.Create(Context.StringTable, envVar.Key),
                            StringId.Create(Context.StringTable, envVar.Value.Item1),
                            isPassThrough: envVar.Value.Item2);
                    }
                }
            }

            if (OperatingSystemHelper.IsUnixOS)
            {
                builder.SetEnvironmentVariable(
                    StringId.Create(Context.StringTable, "DYLD_LIBRARY_PATH"),
                    Path.GetDirectoryName(TestProcessExecutable.Path.ToString(Context.PathTable)),
                    isPassThrough: false);

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
            s_semistableHashCounter = 0;
        }

        protected PipTestBase(ITestOutputHelper output) : base(output)
        {
            CacheRoot = Path.Combine(TemporaryDirectory, CacheRootPrefix);

            BaseSetup();
        }

        public class PipTestBaseSetupData
        {
            private readonly PipTable m_pipTable;
            private readonly QualifierTable m_qualifierTable;
            private readonly MountPathExpander m_mountPathExpander;
            private readonly PipTestBase m_pipTestBase;

            protected PipTestBaseSetupData(PipTestBase pipTestBase)
            {
                m_pipTable = pipTestBase.PipTable;
                m_qualifierTable = pipTestBase.QualifierTable;
                m_mountPathExpander = pipTestBase.Expander;
                m_pipTestBase = pipTestBase;
            }

            public static PipTestBaseSetupData Save(PipTestBase pipTestBase) => new PipTestBaseSetupData(pipTestBase);

            public virtual void Restore()
            {
                m_pipTestBase.PipTable = m_pipTable;
                m_pipTestBase.QualifierTable = m_qualifierTable;
                m_pipTestBase.Expander = m_mountPathExpander;
            }
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
            configuration ??= new ConfigurationImpl();

            // For tests, allow writes outside of mounts unles defined otherwise
            ((EngineConfiguration)configuration.Engine).UnsafeAllowOutOfMountWrites ??= true;
            var searchPathToolsHash = new DirectoryMembershipFingerprinterRuleSet(configuration, stringTable).ComputeSearchPathToolsHash();
            FrontEndContext = FrontEndContext.CreateInstanceForTesting(pathTable: Context.PathTable, symbolTable: Context.SymbolTable, qualifierTable: Context.QualifierTable, frontEndConfig: configuration.FrontEnd, loggingContext: LoggingContext);
            var pipSpecificPropertiesConfig = new PipSpecificPropertiesConfig(configuration.Engine.PipSpecificPropertyAndValues);

            PipGraphBuilder = new PipGraph.Builder(
                PipTable,
                Context,
                global::BuildXL.Pips.Tracing.Logger.Log,
                LoggingContext,
                configuration,
                Expander,
                fingerprintSalt: configuration.Cache.CacheSalt,
                searchPathToolsHash: searchPathToolsHash,
                observationReclassificationRulesHash: ObservationReclassifier.ComputeObservationReclassificationRulesHash(configuration),
                pipSpecificPropertiesConfig);

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
                writable: false));

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
                    root: AbsolutePath.Create(pathTable, UnixPaths.UsrBin),
                    allowHashing: true,
                    readable: true,
                    writable: false));

                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "UsrInclude"),
                    root: AbsolutePath.Create(pathTable, UnixPaths.UsrInclude),
                    allowHashing: true,
                    readable: true,
                    writable: false));

                Expander.Add(pathTable, new SemanticPathInfo(
                    rootName: PathAtom.Create(stringTable, "UsrLib"),
                    root: AbsolutePath.Create(pathTable, UnixPaths.UsrLib),
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
                CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
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

        /// <summary>
        /// Creates a unique directory and wraps it in a <see cref="DirectoryArtifact"/>.
        /// </summary>
        protected DirectoryArtifact CreateUniqueDirectoryArtifact(string root = null, string prefix = null)
            => DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(root, prefix));

        /// <summary>
        /// Creates an output without creating the backing file.
        /// </summary>
        protected string CreateOutputFileAsString(string root = null, string prefix = null)
        {
            return CreateUniqueObjPathAsString(prefix ?? "obj", root);
        }

        /// <summary>
        /// Creates an output directory artifact without creating the backing directory. Output artifacts may be created by scheduled pips.
        /// </summary>
        protected DirectoryArtifact CreateOutputDirectoryArtifact(string root = null)
        {
            return OutputDirectory.Create(CreateUniqueObjPath("obj", root));
        }

        protected string CreateUniqueObjPathAsString(string prefix, string root = null)
        {
            Contract.Requires(prefix != null);
            return CreateUniquePathAsString(prefix, root ?? ObjectRoot);
        }

        protected PipProvenance CreateProvenance(AbsolutePath? specPath)
        {
            return CreateProvenanceWithSpec(StringId.Invalid, specPath: specPath);
        }

        protected PipProvenance CreateProvenance(string moduleName)
        {
            return CreateProvenance(
                Context,
                PipGraphBuilder,
                StringId.Invalid,
                CreateUniquePath("spec", SourceRoot),
                PipValuePrefix + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture),
                moduleName: moduleName);
        }

        protected override PipProvenance CreateProvenance(StringId usage = default)
        {
            return CreateProvenanceWithSpec(usage, specPath: null);
        }

        protected PipProvenance CreateProvenanceWithSpec(StringId usage, AbsolutePath? specPath)
        {
            return CreateProvenance(
                Context,
                PipGraphBuilder,
                usage,
                specPath ?? CreateUniquePath("spec", SourceRoot),
                PipValuePrefix + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture));
        }

        protected PipProvenance CreateProvenance(IMutablePipGraph pipGraph, string value = null, string usage = null)
        {
            value = value ?? PipValuePrefix + (m_pipFreshId++).ToString(CultureInfo.InvariantCulture);

            return PipTestBase.CreateProvenance(Context, pipGraph, usage != null ? StringId.Create(Context.StringTable, usage) : StringId.Invalid, CreateUniqueSourcePath(SourceRoot), value);
        }

        /// <summary>
        /// Creates provenance.
        /// </summary>
        public static PipProvenance CreateProvenance(
            BuildXLContext context,
            IMutablePipGraph pipGraph,
            StringId usage,
            AbsolutePath specFile,
            string valueName,
            string moduleName = "module")
        {
            var moduleNameId = StringId.Create(context.StringTable, moduleName);

            var provenance = new PipProvenance(
                Interlocked.Increment(ref s_semistableHashCounter),
                ModuleId.Create(moduleNameId),
                moduleNameId,
                FullSymbol.Create(context.SymbolTable, valueName),
                new LocationData(specFile, 1, 1),
                QualifierId.Unqualified,
                usage.IsValid ? PipDataBuilder.CreatePipData(context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, usage) : PipData.Invalid,
                false);

            if (pipGraph != null)
            {
                AddMetaPips(context, provenance, pipGraph);
            }

            return provenance;
        }

        public static void AddMetaPips(PipExecutionContext context, PipProvenance provenance, IMutablePipGraph pipGraph)
        {
            var modulePip = ModulePip.CreateForTesting(context.StringTable, provenance.Token.Path, provenance.ModuleId, provenance.ModuleName);
            var locationData = new LocationData(provenance.Token.Path, 0, 0);

            pipGraph.AddModule(modulePip);
            pipGraph.AddSpecFile(new SpecFilePip(new FileArtifact(provenance.Token.Path), locationData, modulePip.Module));
            pipGraph.AddOutputValue(new ValuePip(provenance.OutputValueSymbol, QualifierId.Unqualified, locationData));
        }

        protected SealDirectory CreateSealDirectory(AbsolutePath root, SealDirectoryKind sealDirectoryKind, params FileArtifact[] files)
        {
            return new SealDirectory(
                directoryRoot: root,
                contents: SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(files, OrdinalFileArtifactComparer.Instance),
                outputDirectoryContents: CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
                kind: sealDirectoryKind,
                provenance: CreateProvenance(),
                tags: ReadOnlyArray<StringId>.Empty,
                patterns: ReadOnlyArray<StringId>.Empty);
        }

        protected SealDirectory CreateSealDirectory(AbsolutePath root, SealDirectoryKind sealDirectoryKind, bool scrub, params FileArtifact[] files)
        {
            return new SealDirectory(
                directoryRoot: root,
                contents: SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.CloneAndSort(files, OrdinalFileArtifactComparer.Instance),
                outputDirectoryContents: CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
                kind: sealDirectoryKind,
                provenance: CreateProvenance(),
                tags: ReadOnlyArray<StringId>.Empty,
                patterns: ReadOnlyArray<StringId>.Empty,
                scrub: scrub);
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
                outputDirectoryContents: CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
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
                outputDirectoryContents: CollectionUtilities.EmptySortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>(OrdinalDirectoryArtifactComparer.Instance),
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
            IMutablePipGraph pipGraph = null,
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

        protected override Process ToProcess(params Operation[] operations)
        {
            return ToProcess(testProcessExecutable: default, operations);
        }

        protected override Process ToProcess(FileArtifact testProcessExecutable, params Operation[] operations)
        {
            var processBuilder = CreatePipBuilder(operations, testProcessExecutable: testProcessExecutable);
            AddDefaultOsUntrackedScopes(processBuilder);
            var ok = processBuilder.TryFinish(PipConstructionHelper, out var process, out var _);
            XAssert.IsTrue(ok, "Could not finish creating process builder");
            return process;
        }

        protected TestPipGraphFragment CreatePipGraphFragment(string moduleName, bool useTopSort = false, string salt = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(moduleName));
            return new TestPipGraphFragment(LoggingContext, SourceRoot, ObjectRoot, RedirectedRoot, moduleName, useTopSort, salt);
        }

    }
}
