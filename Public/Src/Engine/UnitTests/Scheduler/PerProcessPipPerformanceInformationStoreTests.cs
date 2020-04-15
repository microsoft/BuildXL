// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using Newtonsoft.Json;
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
        private const string LargeDesciptionsSuffix = "qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM1234567890"; // Random String
        private readonly static int s_individualPipInfoStringLength = JsonConvert.SerializeObject(CreateSamplePip(1)).Length;

        public PerProcessPipPerformanceInformationStoreTests(ITestOutputHelper output)
            : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
            m_configuration = ConfigurationHelpers.GetDefaultForTesting(m_context.PathTable, AbsolutePath.Create(m_context.PathTable, Path.Combine(TemporaryDirectory, "config.ds")));

            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
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
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp), 1);
        }

        [Fact]
        public void TestMultipleBatchOfPipPerformanceInformation()
        {
            m_configuration.Logging.MaxNumPipTelemetryBatches = 4;
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            AddMultiplePips(200000);
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(4, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp), 200000);
        }

        [Fact]
        public void TestSmallerBatchsOfPipPerformanceInformation()
        {
            m_configuration.Logging.AriaIndividualMessageSizeLimitBytes = s_individualPipInfoStringLength * 4;
            m_configuration.Logging.MaxNumPipTelemetryBatches = 10;
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            AddMultiplePips(100000);
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(10, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp), 100000);
        }

        [Fact]
        public void TestLargeDescPipPerformanceInformation()
        {
            m_configuration.Logging.AriaIndividualMessageSizeLimitBytes = s_individualPipInfoStringLength - 1;
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            AddMultiplePips(100);
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(0, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp), 100000);
        }

        [Fact]
        public void TestLargeDescPipBetweenRegularPipPerformanceInformation()
        {
            m_configuration.Logging.AriaIndividualMessageSizeLimitBytes = s_individualPipInfoStringLength * 2;
            m_configuration.Logging.MaxNumPipTelemetryBatches = 10;
            m_perPipPerformanceInformationStore = new PerProcessPipPerformanceInformationStore(m_configuration.Logging.MaxNumPipTelemetryBatches, m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
            AddMultiplePips(1);
            m_perPipPerformanceInformationStore.AddPip(new PerProcessPipPerformanceInformation($"Pip LARGE Desc: {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix}", 8000, 8000, 8000, 8000));
            AddMultiplePips(10, 2);
            m_perPipPerformanceInformationStore.AddPip(new PerProcessPipPerformanceInformation($"Pip LARGE Desc: {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix}", 9000, 9000, 9000, 9000));
            AddMultiplePips(20, 11);
            m_perPipPerformanceInformationStore.AddPip(new PerProcessPipPerformanceInformation($"Pip LARGE Desc: {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix} {LargeDesciptionsSuffix}", 7000, 7000, 7000, 7000));
            var resp = m_perPipPerformanceInformationStore.GenerateTopPipPerformanceInfoJsonArray();
            Assert.Equal(10, resp.Length);
            VerifyMultiplePips(AssertMessageSizesAndParseJsonArray(resp), 20);
        }

        private static PerProcessPipPerformanceInformation CreateSamplePip(int index)
        {
            return new PerProcessPipPerformanceInformation($"Pip {index.ToString("D10")} Desc: {LargeDesciptionsSuffix}", index, index, index, index);
        }

        private void AddMultiplePips(int end, int start = 1)
        {
            for (var i = start; i <= end; i++)
            {
                m_perPipPerformanceInformationStore.AddPip(CreateSamplePip(i));
            }
        }

        private void VerifyMultiplePips(PerProcessPipPerformanceInformation[] arr, int numberOfPipsAdded)
        {
            for (var i = 0; i < arr.Length; i++, numberOfPipsAdded--)
            {
                Assert.Equal(CreateSamplePip(numberOfPipsAdded), arr[i]);
            }
        }

        private PerProcessPipPerformanceInformation[] AssertMessageSizesAndParseJsonArray(string[] arr)
        {
            List<PerProcessPipPerformanceInformation> resp = new List<PerProcessPipPerformanceInformation>();
            Assert.True(arr.Length <= m_configuration.Logging.MaxNumPipTelemetryBatches);
            foreach (var message in arr)
            {
                var jsonObj = JsonConvert.DeserializeObject<Dictionary<string, PerProcessPipPerformanceInformation[]>>(message);
                Assert.True(message.Length <= m_configuration.Logging.AriaIndividualMessageSizeLimitBytes);
                foreach (var pipInfoArr in jsonObj.Values)
                {
                    resp.AddRange(pipInfoArr.AsEnumerable());
                }
            }
            return resp.ToArray();
        }
    }
}