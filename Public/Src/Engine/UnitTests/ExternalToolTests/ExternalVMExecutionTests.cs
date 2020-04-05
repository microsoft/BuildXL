// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace ExternalToolTest.BuildXL.Scheduler
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true, requiresSymlinkPermission: true)]
    public class ExternalVmExecutionTests : ExternalToolExecutionTests
    {
        public ExternalVmExecutionTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Sandbox.AdminRequiredProcessExecutionMode = global::BuildXL.Utilities.Configuration.AdminRequiredProcessExecutionMode.ExternalVM;
            Configuration.Sandbox.RedirectedTempFolderRootForVmExecution = CreateUniqueDirectory(ObjectRootPath);
        }

        [Fact]
        public void RunWithLimitedConcurrency()
        {
            Configuration.Sandbox.VmConcurrencyLimit = 1;
            const int PipCount = 5;

            FileArtifact shared = CreateOutputFileArtifact();

            for (int i = 0; i < PipCount; ++i)
            {
                ProcessBuilder builder = CreatePipBuilder(new[]
                {
                    Operation.ReadFile(CreateSourceFile()),
                    Operation.WriteFile(CreateOutputFileArtifact()) ,
                    Operation.WriteFile(shared, "#", doNotInfer: true)
                });
                builder.Options |= Process.Options.RequiresAdmin;
                builder.AddUntrackedFile(shared);
                SchedulePipBuilder(builder);
            }

            RunScheduler().AssertSuccess();
            string result = File.ReadAllText(ArtifactToString(shared));
            XAssert.AreEqual(new string('#', PipCount), result);
        }
    }
}
