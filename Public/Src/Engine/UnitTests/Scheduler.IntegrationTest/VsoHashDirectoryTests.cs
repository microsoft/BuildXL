// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Feature(Features.OpaqueDirectory)]
    public class VsoHashDirectoryTests : SchedulerIntegrationTestBase
    {
        private static readonly string s_zeroVsoHash = FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.ZeroHash).Render();

        public VsoHashDirectoryTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// A process that uses vsoHash of an opaque (exclusive or shared) directory in its environment variables
        /// should succeed and produce a non-zero hash visible in stdout.
        /// </summary>
        [Theory]
        [InlineData(/*isSharedOpaque*/ true)]
        [InlineData(/*isSharedOpaque*/ false)]
        public void VsoHashOfOpaqueDirectorySucceeds(bool isSharedOpaque)
        {
            Configuration.Sandbox.OutputReportingMode = global::BuildXL.Utilities.Configuration.OutputReportingMode.FullOutputAlways;

            // Create a producer pip that writes to an opaque directory
            var opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            var outputInOpaque = CreateOutputFileArtifact(opaqueDir);
            FileArtifact source = CreateSourceFile();

            ProcessWithOutputs producerPip;
            DirectoryArtifact opaqueDirectory;

            if (isSharedOpaque)
            {
                producerPip = CreateAndScheduleSharedOpaqueProducer(
                    opaqueDir,
                    fileToProduceStatically: FileArtifact.Invalid,
                    sourceFileToRead: source,
                    filesToProduceDynamically: outputInOpaque);

                opaqueDirectory = producerPip.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath);
            }
            else
            {
                producerPip = CreateAndScheduleOpaqueProducer(
                    opaqueDir,
                    sourceFile: source,
                    new System.Collections.Generic.KeyValuePair<FileArtifact, string>(outputInOpaque, null));

                opaqueDirectory = producerPip.ProcessOutputs.GetOpaqueDirectory(opaqueDirPath);
            }

            // Create a consumer pip that uses vsoHash of the opaque as an env var
            var consumerOutput = CreateOutputFileArtifact();
            var consumerBuilder = CreatePipBuilder(new Operation[]
            {
                Operation.ReadEnvVar("DIR_HASH"),
                Operation.WriteFile(consumerOutput)
            });
            consumerBuilder.AddInputDirectory(opaqueDirectory);

            var envVarPipData = new PipDataBuilder(Context.PathTable.StringTable);
            envVarPipData.AddVsoHash(opaqueDirectory);
            consumerBuilder.SetEnvironmentVariable(
                StringId.Create(Context.PathTable.StringTable, "DIR_HASH"),
                envVarPipData.ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping),
                isPassThrough: false);

            var consumerPip = SchedulePipBuilder(consumerBuilder);

            RunScheduler().AssertSuccess();

            // Verify the hash was echoed and is a non-zero VSO hash
            string log = EventListener.GetLog();
            XAssert.IsTrue(log.Contains("VSO0:"), "Expected a non-zero VSO hash in the pip output, but got: " + log);
            XAssert.IsFalse(log.Contains(s_zeroVsoHash), "Expected a non-zero VSO hash, but got the zero hash");
        }
    }
}