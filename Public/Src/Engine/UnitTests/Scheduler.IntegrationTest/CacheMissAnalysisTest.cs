// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;
using Newtonsoft.Json.Linq;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class CacheMissAnalysisTest : SchedulerIntegrationTestBase
    {
        public CacheMissAnalysisTest(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void OutputMissingTest()
        {
            string loggedCachemissReason = "";
            EventListener.NestedLoggerHandler += eventData =>
            {
                if (eventData.EventId == (int)EventId.CacheMissAnalysis)
                {
                    loggedCachemissReason = eventData.Payload.ToArray()[1].ToString();
                }
            };


            var dir = Path.Combine(ObjectRoot, "Dir");
            var dirPath = AbsolutePath.Create(Context.PathTable, dir);

            FileArtifact input = CreateSourceFile(root: dirPath, prefix: "input-file");
            FileArtifact output = CreateOutputFileArtifact(root: dirPath, prefix: "output-file");
            var pipBuilder = CreatePipBuilder(new[] { Operation.ReadFile(input), Operation.WriteFile(output) });
            var pip = SchedulePipBuilder(pipBuilder);            
            RunScheduler().AssertSuccess();

            Configuration.Logging.CacheMissAnalysisOption = CacheMissAnalysisOption.LocalMode();
            DiscardFileContentInArtifactCacheIfExists(output);
            File.Delete(ArtifactToString(output));

            RunScheduler().AssertCacheMiss(pip.Process.PipId);

            XAssert.IsTrue(EventListener.GetLog().Contains("MissingOutputs"), "Output was deleted from last run and should be missing in this build.");
            XAssert.IsTrue(EventListener.GetLog().Contains(new JValue(ArtifactToString(output)).ToString()), "Missing output should be listed.");

            AssertWarningEventLogged(EventId.ConvertToRunnableFromCacheFailed, count: 0, allowMore: true);
        }
    }
}
