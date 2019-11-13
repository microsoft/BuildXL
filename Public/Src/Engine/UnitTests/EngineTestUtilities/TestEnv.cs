// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Pips.Builders;
using Test.BuildXL.EngineTestUtilities;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Provides a ready-made environment for use during tests.
    /// </summary>
    public sealed class TestEnv : IDisposable
    {
        /// <summary>
        /// Root path used for scheduler types that don't touch the real disk.
        /// We have a space in the path so that all imaginary paths get quoted (that's historically been the case
        /// due to previously using MsTest's test deployment directory)
        /// </summary>
        private const string FakeTestRoot = @"\\test\test root\";

        /// <summary>
        /// Engine context
        /// </summary>
        public readonly EngineContext Context;

        /// <summary>
        /// Logging context
        /// </summary>
        public readonly LoggingContext LoggingContext;

        /// <summary>
        /// A usable path table.
        /// </summary>
        public readonly PathTable PathTable;

        /// <summary>
        /// A usable identifier table.
        /// </summary>
        public SymbolTable SymbolTable => Context.SymbolTable;

        /// <summary>
        /// Source root for the LocalDiskSourceStore for the environment
        /// </summary>
        public AbsolutePath SourceRoot => Configuration.Layout.SourceDirectory;

        /// <summary>
        /// Root for written outputs.
        /// </summary>
        public AbsolutePath ObjectRoot => Configuration.Layout.ObjectDirectory;

        /// <nodoc/>
        public readonly PipConstructionHelper PipConstructionHelper;

        /// <summary>
        /// Path helpers
        /// </summary>
        public readonly Paths Paths;

        /// <summary>
        /// Pip graph
        /// </summary>
        public readonly IPipGraphBuilder PipGraph;

        /// <summary>
        /// Configuration
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <nodoc />
        public ObjectPool<PipDataBuilder> PipDataBuilderPool { get; }

        /// <nodoc />
        public PipTable PipTable { get; private set; }

        /// <summary>
        /// Creates a new test environment which schedules pips with full scheduler validation, but which cannot execute pips.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static TestEnv CreateTestEnvWithPausedScheduler(List<IMount> mounts = null, PathTable pathTable = null)
        {
            return CreateTestEnvWithPausedScheduler(GetTestNameGuess(), mounts, pathTable);
        }

        /// <summary>
        /// Creates a new test environment which schedules pips with full scheduler validation, but which cannot execute pips.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static TestEnv CreateTestEnvWithPausedScheduler(string testName, List<IMount> mounts = null, PathTable pathTable = null, bool enableLazyOutputMaterialization = false)
        {
            return new TestEnv(testName, FakeTestRoot, mounts: mounts, pathTable: pathTable, enableLazyOutputMaterialization: enableLazyOutputMaterialization);
        }

        /// <summary>
        /// Gets a test's name based on call stack.
        /// </summary>
        /// <remarks>
        /// This is consumed in various places for historical reasons. We have to prevent it from being inlined, otherwise
        /// the correct method name may not be returned
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetTestNameGuess()
        {
            return new System.Diagnostics.StackTrace().GetFrame(2).GetMethod().Name;
        }

        /// <summary>
        /// Creates a new test environment.
        /// </summary>
        /// <param name="name">Name of the test environment</param>
        /// <param name="rootPath">
        /// Path under which files will be written hypothetical files otherwise.
        /// </param>
        /// <param name="enableLazyOutputMaterialization">Enable lazy outputs materialization</param>
        /// <param name="maxRelativeOutputDirectoryLength">
        /// The maximum length of output directories created under
        /// <code>ObjectDirectoryPath</code>; long names will be shortened by hashing.
        /// </param>
        /// <param name="mounts">Optional list of mounts to include in the configuration</param>
        /// <param name="pathTable">Optional path table to use. If not defined, a default one will be used.</param>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "TestValue")]
        [SuppressMessage("Microsoft.Globalization", "CA1308:Normalize strings to uppercase")]
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public TestEnv(
            string name,
            string rootPath,
            bool enableLazyOutputMaterialization = false,
            int maxRelativeOutputDirectoryLength = 260,
            List<IMount> mounts = null,
            PathTable pathTable = null)
        {
            Contract.Requires(name != null);
            Contract.Requires(!string.IsNullOrEmpty(rootPath));

            LoggingContext = new LoggingContext("TestLogger." + name);
            PathTable = pathTable ?? new PathTable();

            PipDataBuilderPool = new ObjectPool<PipDataBuilder>(() => new PipDataBuilder(PathTable.StringTable), _ => { });

            // The tests that use TestEnv need to be modernized to take a filesystem
            var fileSystem = new PassThroughFileSystem(PathTable);

            Context = EngineContext.CreateNew(CancellationToken.None, PathTable, fileSystem);

            // Add some well-known paths with fixed casing to the Context.PathTable
            AbsolutePath.Create(Context.PathTable, rootPath.ToLowerInvariant());
            var root = AbsolutePath.Create(Context.PathTable, rootPath);

            var configuration = ConfigHelpers.CreateDefaultForXml(Context.PathTable, root);
            configuration.Layout.SourceDirectory = root.Combine(PathTable, PathAtom.Create(PathTable.StringTable, "src")); // These tests have non-standard src folder
            configuration.Engine.MaxRelativeOutputDirectoryLength = maxRelativeOutputDirectoryLength;
            configuration.Schedule.EnableLazyOutputMaterialization = enableLazyOutputMaterialization;
            configuration.Schedule.UnsafeDisableGraphPostValidation = false;
            configuration.Schedule.ComputePipStaticFingerprints = true;
            configuration.Sandbox.FileAccessIgnoreCodeCoverage = true;

            BuildXLEngine.PopulateFileSystemCapabilities(configuration, configuration, Context.PathTable, LoggingContext);
            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(configuration, Context.PathTable, bxlExeLocation: null, inTestMode: true);
            BuildXLEngine.PopulateAndValidateConfiguration(configuration, configuration, Context.PathTable, LoggingContext);

            Configuration = configuration;

            var mountsTable = MountsTable.CreateAndRegister(LoggingContext, Context, Configuration, null);

            if (mounts != null)
            {
                foreach (var mount in mounts)
                {
                    mountsTable.AddResolvedMount(mount);
                }
            }
            
            AbsolutePath specFile = SourceRoot.CreateRelative(Context.PathTable, "TestSpecFile.dsc");

            var graph = TestSchedulerFactory.CreateEmptyPipGraph(Context, configuration, mountsTable.MountPathExpander);
            PipTable = graph.PipTable;
            PipGraph = graph;

            var locationData = new LocationData(specFile, 0, 0);
            var modulePip = ModulePip.CreateForTesting(Context.StringTable, specFile);
            PipGraph.AddModule(modulePip);
            PipGraph.AddSpecFile(new SpecFilePip(FileArtifact.CreateSourceFile(specFile), locationData, modulePip.Module));

            PipConstructionHelper = PipConstructionHelper.CreateForTesting(
                Context,
                ObjectRoot,
                redirectedRoot: Configuration.Layout.RedirectedDirectory,
                pipGraph: PipGraph,
                moduleName: modulePip.Identity.ToString(Context.StringTable),
                symbol: name,
                specPath: specFile);

            Paths = new Paths(PathTable);

            mountsTable.CompleteInitialization();
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed")]
        public void Dispose()
        {
        }

        /// <summary>
        /// A fake pip graph for testing.
        /// </summary>
        public sealed class TestPipGraph : IPipGraphBuilder
        {
            private readonly ConcurrentQueue<Pip> m_pips = new ConcurrentQueue<Pip>();
            private readonly Lazy<IIpcMoniker> m_lazyApiServerMoniker = Lazy.Create(() => IpcFactory.GetProvider().CreateNewMoniker());
            private int m_reservedSealIds = 0;

            /// <inheritdoc />
            public bool AddProcess(Process process, PipId valuePip)
            {
                Contract.Requires(process != null, "Argument process cannot be null");
                process.PipId = GetPipId();
                m_pips.Enqueue(process);
                return true;
            }

            /// <inheritdoc />
            public bool AddIpcPip(IpcPip ipcPip, PipId valuePip)
            {
                Contract.Requires(ipcPip != null, "Argument pip cannot be null");
                ipcPip.PipId = GetPipId();
                m_pips.Enqueue(ipcPip);
                return true;
            }

            /// <inheritdoc />
            public bool AddCopyFile(CopyFile copyFile, PipId valuePip)
            {
                Contract.Requires(copyFile != null, "Argument copyFile cannot be null");
                copyFile.PipId = GetPipId();
                m_pips.Enqueue(copyFile);
                return true;
            }

            /// <inheritdoc />
            public bool AddWriteFile(WriteFile writeFile, PipId valuePip)
            {
                Contract.Requires(writeFile != null, "Argument writeFile cannot be null");
                writeFile.PipId = GetPipId();
                m_pips.Enqueue(writeFile);
                return true;
            }

            /// <inheritdoc />
            public DirectoryArtifact AddSealDirectory(SealDirectory sealDirectory, PipId valuePip)
            {
                Contract.Requires(sealDirectory != null);

                sealDirectory.PipId = GetPipId();
                sealDirectory.SetDirectoryArtifact(DirectoryArtifact.CreateWithZeroPartialSealId(sealDirectory.DirectoryRoot));
                Contract.Assume(sealDirectory.IsInitialized);
                m_pips.Enqueue(sealDirectory);
                return sealDirectory.Directory;
            }

            /// <inheritdoc />
            public bool AddOutputValue(ValuePip value)
            {
                Contract.Requires(value != null, "Argument outputValue cannot be null");
                value.PipId = GetPipId();
                m_pips.Enqueue(value);
                return true;
            }

            /// <inheritdoc />
            public bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency)
            {
                Contract.Requires(valueDependency.ParentIdentifier.IsValid);
                Contract.Requires(valueDependency.ChildIdentifier.IsValid);
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public bool AddSpecFile(SpecFilePip specFile)
            {
                Contract.Requires(specFile != null, "Argument specFile cannot be null");
                specFile.PipId = GetPipId();
                m_pips.Enqueue(specFile);
                return true;
            }

            /// <inheritdoc />
            public bool AddModule(ModulePip module)
            {
                Contract.Requires(module != null, "Argument module cannot be null");
                module.PipId = GetPipId();
                m_pips.Enqueue(module);
                return true;
            }

            /// <inheritdoc />
            public bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency)
            {
                return true;
            }

            /// <inheritdoc />
            public IEnumerable<Pip> RetrieveScheduledPips()
            {
                return m_pips.ToList();
            }

            /// <inheritdoc />
            public IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip)
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip)
            {
                throw new NotImplementedException();
            }

            private PipId GetPipId()
            {
                lock (m_pips)
                {
                    return new PipId((uint)m_pips.Count + 1);
                }
            }

            /// <nodoc />
            public int PipCount => m_pips.Count;

            /// <inheritdoc />
            public bool IsImmutable
            {
                get
                {
                    throw new NotImplementedException();
                }
            }

            /// <nodoc />
            public IIpcMoniker GetApiServerMoniker()
            {
                return m_lazyApiServerMoniker.Value;
            }

            /// <inheritdoc />
            public GraphPatchingStatistics PartiallyReloadGraph(HashSet<AbsolutePath> affectedSpecs)
            {
                Contract.Requires(affectedSpecs != null);
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public void SetSpecsToIgnore([CanBeNull] IEnumerable<AbsolutePath> specsToIgnore)
            {
                throw new NotImplementedException();
            }
            
            /// <inheritdoc />
            public DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot)
            {
                Interlocked.Increment(ref m_reservedSealIds);
                return new DirectoryArtifact(directoryArtifactRoot, (uint) m_reservedSealIds, isSharedOpaque: true);
            }

            /// <inheritdoc />
            public PipGraph Build()
            {
                throw new NotImplementedException();
            }

            /// <inheritdoc />
            public bool ApplyCurrentOsDefaults(ProcessBuilder processBuilder)
            {
                return true;
            }
        }

        /// <summary>
        /// Creates a valid pipProvenance with appropriate backing nodes in the graph.
        /// </summary>
        public PipProvenance CreatePipProvenance(StringId usage, string valueName = "TestValue")
        {
            AbsolutePath specFile = SourceRoot.CreateRelative(Context.PathTable, "TestSpecFile.dsc");

            var valueId = FullSymbol.Create(Context.SymbolTable, valueName);
            var locationData = new LocationData(specFile, 0, 0);

            var modulePip = ModulePip.CreateForTesting(Context.StringTable, specFile);
            PipGraph.AddModule(ModulePip.CreateForTesting(Context.StringTable, specFile));
            PipGraph.AddSpecFile(new SpecFilePip(FileArtifact.CreateSourceFile(specFile), locationData, modulePip.Module));
            PipGraph.AddOutputValue(new ValuePip(valueId, QualifierId.Unqualified, locationData));

            return new PipProvenance(
                0,
                modulePip.Module,
                modulePip.Identity,
                valueId,
                LocationData.Invalid,
                QualifierId.Unqualified,
                usage.IsValid ? PipDataBuilder.CreatePipData(Context.StringTable, string.Empty, PipDataFragmentEscaping.NoEscaping, usage) : PipData.Invalid);
        }
    }
}
