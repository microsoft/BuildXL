// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Pip graph fragment for tests.
    /// </summary>
    public sealed class TestPipGraphFragment
    {
        private readonly AbsolutePath m_sourceRoot;
        private readonly AbsolutePath m_objectRoot;
        private readonly LoggingContext m_loggingContext;
        private readonly IPipGraph m_pipGraph;
        private readonly ModuleId m_moduleId;
        private readonly AbsolutePath m_specPath;
        private readonly PipConstructionHelper m_defaultConstructionHelper;

        /// <summary>
        /// Module name.
        /// </summary>
        public string ModuleName { get; }

        /// <summary>
        /// Context.
        /// </summary>
        public BuildXLContext Context { get; }

        /// <summary>
        /// Creates an instance of <see cref="TestPipGraphFragment"/>.
        /// </summary>
        public TestPipGraphFragment(LoggingContext loggingContext, string sourceRoot, string objectRoot, string redirectedRoot, string moduleName)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(!string.IsNullOrEmpty(sourceRoot));
            Contract.Requires(!string.IsNullOrEmpty(objectRoot));
            Contract.Requires(!string.IsNullOrEmpty(moduleName));

            Context = BuildXLContext.CreateInstanceForTesting();
            m_loggingContext = loggingContext;
            m_sourceRoot = AbsolutePath.Create(Context.PathTable, sourceRoot);
            m_objectRoot = AbsolutePath.Create(Context.PathTable, objectRoot);

            m_pipGraph = new GraphFragmentBuilder(
                m_loggingContext,
                Context,
                new ConfigurationImpl()
                {
                    Schedule =
                    {
                        UseFixedApiServerMoniker = true
                    }
                });

            ModuleName = moduleName;
            var specFileName = moduleName + ".dsc";
            m_specPath = m_sourceRoot.Combine(Context.PathTable, specFileName);
            m_moduleId = ModuleId.Create(StringId.Create(Context.StringTable, moduleName));
            var modulePip = ModulePip.CreateForTesting(
                Context.StringTable, 
                m_specPath,
                m_moduleId);
            m_pipGraph.AddModule(modulePip);
            m_pipGraph.AddSpecFile(new SpecFilePip(new FileArtifact(m_specPath), new LocationData(m_specPath, 0, 0), modulePip.Module));

            m_defaultConstructionHelper = PipConstructionHelper.CreateForTesting(
                Context,
                objectRoot: m_objectRoot,
                redirectedRoot: AbsolutePath.Create(Context.PathTable, redirectedRoot),
                pipGraph: m_pipGraph,
                moduleName: moduleName,
                specRelativePath: Path.Combine(m_sourceRoot.GetName(Context.PathTable).ToString(Context.StringTable), specFileName),
                specPath: m_specPath,
                symbol: moduleName + "_defaultValue");
        }

        /// <summary>
        /// Serializes this instance of pip graph fragment to a stream.
        /// </summary>
        public void Serialize(Stream stream) =>
            new PipGraphFragmentSerializer(
                Context, 
                new PipGraphFragmentContext())
                .SerializeSerially(stream, m_pipGraph.RetrieveScheduledPips().ToList(), m_moduleId.Value.ToString(Context.StringTable));

        /// <summary>
        /// Creates a source file artifact.
        /// </summary>
        public FileArtifact CreateSourceFile(string relative) =>
            FileArtifact.CreateSourceFile(m_sourceRoot.Combine(
                Context.PathTable, RelativePath.Create(Context.StringTable, relative)));
        
        /// <summary>
        /// Creates an output file artifact.
        /// </summary>
        public FileArtifact CreateOutputFile(string relative) =>
            FileArtifact.CreateOutputFile(m_objectRoot.Combine(
                Context.PathTable, RelativePath.Create(Context.StringTable, relative)));

        /// <summary>
        /// Gets a process builder.
        /// </summary>
        public ProcessBuilder GetProcessBuilder()
        {
            var builder = ProcessBuilder.CreateForTesting(Context.PathTable);
            builder.Executable = FileArtifact.CreateSourceFile(m_sourceRoot.Combine(Context.PathTable, "test.exe"));
            builder.AddInputFile(builder.Executable);

            return builder;
        }

        /// <summary>
        /// Schedules a process builder.
        /// </summary>
        public (Process process, ProcessOutputs outputs) ScheduleProcessBuilder(ProcessBuilder builder, PipConstructionHelper pipConstructionHelper = null)
        {
            var helper = pipConstructionHelper ?? m_defaultConstructionHelper;

            if (!helper.TryAddProcess(builder, out ProcessOutputs outputs, out Process process))
            {
                throw new BuildXLTestException("Failed to add process pip");
            }

            return (process, outputs);
        }

        /// <summary>
        /// Gets API server moniker.
        /// </summary>
        public IIpcMoniker GetApiServerMoniker() => m_pipGraph.GetApiServerMoniker();

        /// <summary>
        /// Gets an IPC moniker.
        /// </summary>
        public IIpcMoniker GetIpcMoniker(PipConstructionHelper helper = null)
        {
            var semiStableHash = (helper ?? m_defaultConstructionHelper).GetNextSemiStableHash();
            return IpcFactory.GetProvider().LoadOrCreateMoniker(string.Format(CultureInfo.InvariantCulture, "{0:X16}", semiStableHash));
        }

        /// <summary>
        /// Gets IPC process builder.
        /// </summary>
        /// <returns></returns>
        public ProcessBuilder GetIpcProcessBuilder() => ProcessBuilder.CreateForTesting(Context.PathTable);

        /// <summary>
        /// Schedules an IPC pip.
        /// </summary>
        public PipId ScheduleIpcPip(
            IIpcMoniker moniker,
            PipId? servicePipId,
            ProcessBuilder ipcProcessBuilder,
            FileArtifact outputFile,
            bool isServiceFinalization,
            PipConstructionHelper helper = null)
        {
            var ipcClientInfo = new IpcClientInfo(StringId.Create(Context.StringTable, moniker.Id), new ClientConfig(0, 0));
            PipData arguments = ipcProcessBuilder.ArgumentsBuilder.ToPipData(" ", PipDataFragmentEscaping.CRuntimeArgumentRules);
            ReadOnlyArray<FileArtifact> fileDependencies = ipcProcessBuilder.GetInputFilesSoFar();

            (helper ?? m_defaultConstructionHelper).TryAddIpc(
                ipcClientInfo,
                arguments,
                outputFile,
                servicePipDependencies: servicePipId != null ? ReadOnlyArray<PipId>.From(new[] { servicePipId.Value }) : ReadOnlyArray<PipId>.Empty,
                fileDependencies: fileDependencies,
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                skipMaterializationFor: ReadOnlyArray<FileOrDirectoryArtifact>.Empty,
                isServiceFinalization: isServiceFinalization,
                mustRunOnMaster: false,
                tags: new string[0],
                out PipId pipId);

            return pipId;
        }
    }
}
