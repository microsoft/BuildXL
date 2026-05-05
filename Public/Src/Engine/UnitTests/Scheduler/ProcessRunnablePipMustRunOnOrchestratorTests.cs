// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for <see cref="ProcessRunnablePip.MustRunOnOrchestrator"/> combining
    /// the static process pip option and the dynamic runtime override.
    /// </summary>
    public class ProcessRunnablePipMustRunOnOrchestratorTests : TemporaryStorageTestBase
    {
        private readonly BuildXLContext m_context;
        private readonly LoggingContext m_loggingContext;
        private readonly DummyPipExecutionEnvironment m_executionEnvironment;

        public ProcessRunnablePipMustRunOnOrchestratorTests(ITestOutputHelper output) : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
            m_loggingContext = CreateLoggingContextForTest();
            var configuration = ConfigurationHelpers.GetDefaultForTesting(m_context.PathTable, AbsolutePath.Create(m_context.PathTable, Path.Combine(TemporaryDirectory, "config.ds")));

            var pipTable = new PipTable(
                m_context.PathTable,
                m_context.SymbolTable,
                initialBufferSize: 1024,
                maxDegreeOfParallelism: (Environment.ProcessorCount + 2) / 3,
                debug: false);

            m_executionEnvironment = new DummyPipExecutionEnvironment(m_loggingContext, m_context, configuration, pipTable: pipTable);
        }

        [Fact]
        public void StaticOptionSetsPropertyTrue()
        {
            // Create a process pip with MustRunOnOrchestrator option set
            var pip = CreateProcessPip(Process.Options.MustRunOnOrchestrator);
            var pipId = m_executionEnvironment.PipTable.Add((uint)1, pip);
            var runnablePip = (ProcessRunnablePip)RunnablePip.Create(m_loggingContext, m_executionEnvironment, pipId, PipType.Process, 0, (Func<RunnablePip, Task>)null, 0);

            // The property should be true because the static option is set on the process pip
            Assert.True(runnablePip.MustRunOnOrchestrator);
        }

        [Fact]
        public void DynamicOverrideSetsPropertyTrue()
        {
            // Create a process pip without the static option
            var pip = CreateProcessPip(Process.Options.None);
            var pipId = m_executionEnvironment.PipTable.Add((uint)1, pip);
            var runnablePip = (ProcessRunnablePip)RunnablePip.Create(m_loggingContext, m_executionEnvironment, pipId, PipType.Process, 0, (Func<RunnablePip, Task>)null, 0);

            // Initially false
            Assert.False(runnablePip.MustRunOnOrchestrator);

            // Simulate the dynamic retry scenario (e.g., distribution failure forces retry on orchestrator)
            runnablePip.MustRunOnOrchestrator = true;

            // Now it should be true
            Assert.True(runnablePip.MustRunOnOrchestrator);
        }

        [Fact]
        public void StaticOptionCannotBeOverriddenByDynamicFalse()
        {
            // Create a process pip with the static option set
            var pip = CreateProcessPip(Process.Options.MustRunOnOrchestrator);
            var pipId = m_executionEnvironment.PipTable.Add((uint)1, pip);
            var runnablePip = (ProcessRunnablePip)RunnablePip.Create(m_loggingContext, m_executionEnvironment, pipId, PipType.Process, 0, (Func<RunnablePip, Task>)null, 0);

            // Even if the dynamic field is not set (defaults to false), the static option prevails
            runnablePip.MustRunOnOrchestrator = false;
            Assert.True(runnablePip.MustRunOnOrchestrator);
        }

        private Process CreateProcessPip(Process.Options options)
        {
            var pathTable = m_context.PathTable;
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));
            var dependencies = new HashSet<FileArtifact> { executable };

            var processBuilder = new ProcessBuilder()
                .WithExecutable(executable)
                .WithWorkingDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working")))
                .WithArguments(PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "-nosucharg"))
                .WithStandardDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working.std")))
                .WithDependencies(dependencies)
                .WithContext(m_context)
                .WithOptions(options);

            var dataBuilder = new PipDataBuilder(m_context.PathTable.StringTable);
            return processBuilder.WithArguments(dataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping)).Build();
        }
    }
}
