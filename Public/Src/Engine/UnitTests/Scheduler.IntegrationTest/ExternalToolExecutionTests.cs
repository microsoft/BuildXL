// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class ExternalToolExecutionTests : SchedulerIntegrationTestBase
    {
        public ExternalToolExecutionTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Sandbox.AdminRequiredProcessExecutionMode = global::BuildXL.Utilities.Configuration.AdminRequiredProcessExecutionMode.ExternalTool;
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void RunSimpleTest()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
        }
    }
}
