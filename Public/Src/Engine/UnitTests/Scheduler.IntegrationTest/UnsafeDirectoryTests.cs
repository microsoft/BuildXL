// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "OpaqueDirectoryTests")]
    [Feature(Features.OpaqueDirectory)]
    public class UnsafeDirectoryTests : SchedulerIntegrationTestBase
    {
        public UnsafeDirectoryTests(ITestOutputHelper output) : base(output)
        {
            ((UnsafeSandboxConfiguration)(Configuration.Sandbox.UnsafeSandboxConfiguration)).IgnoreDynamicWritesOnAbsentProbes = true;
        }

        [Fact]
        public void AbsentFileProbeFollowedByWriteInExclusiveOpaqueIsIgnored()
        {
            var opaqueDir = Path.Combine(ObjectRoot, "opaquedir");
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, opaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(opaqueDir);

            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.Probe(absentFile, doNotInfer: true),
                                                       Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                                   });
            var resA = SchedulePipBuilder(builderA);

            // PipB writes absentFile into an exclusive opaque directory
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(resA.ProcessOutputs.GetOutputFiles().First()), // force a dependency
                                                       Operation.WriteFile(absentFile, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.Opaque);
            var resB = SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void AbsentFileProbeFollowedByDynamicWriteIsIgnored()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            FileArtifact absentFile = CreateOutputFileArtifact(sharedOpaqueDir);

            var builderA = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.Probe(absentFile, doNotInfer: true),
                                                       Operation.WriteFile(CreateOutputFileArtifact()) // dummy output
                                                   });
            var resA = SchedulePipBuilder(builderA);

            // PipB writes absentFile into a shared opaque directory sharedopaquedir.
            var builderB = CreatePipBuilder(new Operation[]
                                                   {
                                                       Operation.ReadFile(resA.ProcessOutputs.GetOutputFiles().First()), // force a dependency
                                                       Operation.WriteFile(absentFile, doNotInfer: true),
                                                   });
            builderB.AddOutputDirectory(sharedOpaqueDirPath, SealDirectoryKind.SharedOpaque);
            var resB = SchedulePipBuilder(builderB);

            RunScheduler().AssertSuccess();
        }
    }
}
