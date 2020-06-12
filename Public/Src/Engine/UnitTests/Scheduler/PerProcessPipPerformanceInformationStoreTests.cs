// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using Newtonsoft.Json;
using Test.BuildXL.Processes;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class PerProcessPipPerformanceInformationStoreTests : TemporaryStorageTestBase
    {
        private PerProcessPipPerformanceInformationStore m_perPipPerformanceInformationStore;
        private readonly BuildXLContext m_context;
        private readonly CommandLineConfiguration m_configuration;
        private readonly LoggingContext m_loggingContext;
        private readonly int m_individualPipInfoStringLength;
        private readonly IPipExecutionEnvironment m_executionEnvironment;
        private readonly Hashtable m_runnablePips;

        public PerProcessPipPerformanceInformationStoreTests(ITestOutputHelper output)
            : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
            m_loggingContext = CreateLoggingContextForTest();
            m_configuration = ConfigurationHelpers.GetDefaultForTesting(m_context.PathTable, AbsolutePath.Create(m_context.PathTable, Path.Combine(TemporaryDirectory, "config.ds")));
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            m_runnablePips = new Hashtable();

            var pipTable = new PipTable(
                    m_context.PathTable,
                    m_context.SymbolTable,
                    initialBufferSize: 1024,
                    maxDegreeOfParallelism: (Environment.ProcessorCount + 2) / 3,
                    debug: false);
            m_executionEnvironment = new DummyPipExecutionEnvironment(m_loggingContext, m_context, m_configuration, pipTable: pipTable);

            m_individualPipInfoStringLength = PerProcessPipPerformanceInformationStore.SerializePipPerfInfo(CreateSamplePip(0)).Length;
        }

        [Fact]
        public void TestEmptyPerPipPerformanceInformation()
        {
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(0, resp.Length);
            var perfArr = AssertMessageSizesAndParseJsonArray(resp);
            Assert.Equal(0, perfArr.Length);
        }

        [Fact]
        public void TestSinglePerPipPerformanceInformation()
        {
            m_perPipPerformanceInformationStore.AddPip(CreateSamplePip(1));
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(1, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp));
        }

        [Fact]
        public void TestMultipleBatchOfPipPerformanceInformation()
        {
            m_configuration.Logging.MaxNumPipTelemetryBatches = 4;
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            AddMultiplePips(200000);
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(4, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp));
        }

        [Fact]
        public void TestSmallerBatchsOfPipPerformanceInformation()
        {
            m_configuration.Logging.AriaIndividualMessageSizeLimitBytes = m_individualPipInfoStringLength * 4;
            m_configuration.Logging.MaxNumPipTelemetryBatches = 10;
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            AddMultiplePips(100000);
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(10, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp));
        }

        [Fact]
        public void TestLargeDescPipPerformanceInformation()
        {
            m_configuration.Logging.AriaIndividualMessageSizeLimitBytes = m_individualPipInfoStringLength - 1;
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            AddMultiplePips(100);
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(0, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp));
        }

        private PerProcessPipPerformanceInformation CreateSamplePip(int index)
        {
            Func<RunnablePip, Task<PipResult>> taskFactory = async (runnablePip) =>
            {
                PipResult result;
                var operationTracker = new OperationTracker(runnablePip.LoggingContext);
                var pip = runnablePip.Pip;
                using (var operationContext = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, pip.PipId, pip.PipType, runnablePip.LoggingContext))
                {
                    result = await TestPipExecutor.ExecuteAsync(operationContext, m_executionEnvironment, pip);
                }

                return result;
            };

            var pathTable = m_context.PathTable;

            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, X("/x/pkgs/tool.exe")));
            var dependencies = new HashSet<FileArtifact> { executable };

            var processBuilder = new ProcessBuilder()
                .WithExecutable(executable)
                .WithWorkingDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working")))
                .WithArguments(PipDataBuilder.CreatePipData(pathTable.StringTable, " ", PipDataFragmentEscaping.CRuntimeArgumentRules, "-loadargs"))
                .WithStandardDirectory(AbsolutePath.Create(pathTable, X("/x/obj/working.std")))
                .WithDependencies(dependencies)
                .WithContext(m_context);

            var dataBuilder = new PipDataBuilder(m_context.PathTable.StringTable);
            var pipData = dataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping);
            var pip = processBuilder.WithArguments(pipData).Build();
            var pipId = m_executionEnvironment.PipTable.Add((uint)(index + 1), pip);

            var runnableProcessPip = (ProcessRunnablePip)(RunnablePip.Create(m_loggingContext, m_executionEnvironment, pipId, PipType.Process, 0, taskFactory, 0));
            m_runnablePips.Add(index, runnableProcessPip);      // For verification

            return GeneratePipInfoWithRunnablePipAndIndex(ref runnableProcessPip, index);
        }

        private void AddMultiplePips(int end, int start = 1)
        {
            for (var i = start; i <= end; i++)
            {
                m_perPipPerformanceInformationStore.AddPip(CreateSamplePip(i));
            }
        }

        private void VerifyMultiplePips(string[] arr)
        {
            for (var i = 0; i < arr.Length; i++)
            {
                var jsonObj = JsonConvert.DeserializeObject<PerProcessPipPerformanceInformation>(arr[i]);
                int index = jsonObj.IOReadMb;
                ProcessRunnablePip runnablePip = (ProcessRunnablePip)m_runnablePips[index];
                PerProcessPipPerformanceInformation pipInfo = GeneratePipInfoWithRunnablePipAndIndex(ref runnablePip, index);

                Assert.Equal(PerProcessPipPerformanceInformationStore.SerializePipPerfInfo(pipInfo), arr[i]);
            }
        }

        private PerProcessPipPerformanceInformation GeneratePipInfoWithRunnablePipAndIndex(ref ProcessRunnablePip runnablePip, int index)
        {
            return new PerProcessPipPerformanceInformation(ref runnablePip, index, index, index, index);
        }

        private string[] AssertMessageSizesAndParseJsonArray(string[] arr)
        {
            List<object> resp = new List<object>();
            Assert.True(arr.Length <= m_configuration.Logging.MaxNumPipTelemetryBatches);
            foreach (var message in arr)
            {
                var jsonObj = JsonConvert.DeserializeObject<Dictionary<string, object[]>>(message);
                Assert.True(message.Length <= m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
                foreach (var pipInfoArr in jsonObj.Values)
                {
                    resp.AddRange(pipInfoArr.AsEnumerable());
                }
            }
            return resp.Select(a => JsonConvert.SerializeObject(a)).ToArray();
        }
    }
}