// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class OutputsRemainWriteableTests : SchedulerIntegrationTestBase
    {
        public OutputsRemainWriteableTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public void OutputsRemainWriteableTest(bool useHardLinksOption, bool outputsWritable)
        {
            // ...........Setting Modes...........
            Configuration.Engine.UseHardlinks = useHardLinksOption;

            FileArtifact fileA = CreateOutputFileArtifact();
            FileArtifact fileB = CreateOutputFileArtifact();
            FileArtifact fileC = CreateOutputFileArtifact();

            // ...........PIP A...........
            var builderA = CreatePipBuilder(new Operation[]
            {
                  Operation.WriteFile(fileA)
            });

            // appending a tag
            builderA.AddTags(Context.StringTable, "pipA");

            // ...........PIP B...........
            var builderB = CreatePipBuilder(new Operation[]
            {
                  Operation.ReadFile(fileA),
                  Operation.CopyFile(fileA,fileB)
            });

            // appending a tag
            builderB.AddTags(Context.StringTable, "pipB");

            // ...........PIP C...........
            var builderC = CreatePipBuilder(new Operation[]
            {
                  Operation.ReadFile(fileB),
                  Operation.WriteFile(fileC)
            });

            // appending a tag
            builderC.AddTags(Context.StringTable, "pipC");

            // casting because I need to init realization mode tracking, otherwise it is null
            IArtifactContentCacheForTest artifactContentCacheForTest = ((IArtifactContentCacheForTest)Cache.ArtifactContentCache);
            artifactContentCacheForTest.ReinitializeRealizationModeTracking();

            if (outputsWritable)
            {
                builderA.Options |= Process.Options.OutputsMustRemainWritable;
                builderB.Options |= Process.Options.OutputsMustRemainWritable;
                builderC.Options |= Process.Options.OutputsMustRemainWritable;
            }

            var pipA = SchedulePipBuilder(builderA);
            var pipB = SchedulePipBuilder(builderB);
            var pipC = SchedulePipBuilder(builderC);

            // run scheduler
            RunScheduler().AssertSuccess().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId, pipC.Process.PipId);

            string pathA = ArtifactToString(fileA);
            string pathB = ArtifactToString(fileB);
            string pathC = ArtifactToString(fileC);

            File.Delete(pathA);
            File.Delete(pathB);
            File.Delete(pathC);

            // run scheduler
            RunScheduler().AssertSuccess().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId, pipC.Process.PipId);

            // realization modes appear not to be important until replay from cache
            // BuildXL keeps outputs writable if either you specify not to use hard links or to keep outputs writable
            if (outputsWritable || !useHardLinksOption)
            {
                XAssert.AreEqual(artifactContentCacheForTest.GetRealizationMode(pathA), FileRealizationMode.Copy);
                XAssert.AreEqual(artifactContentCacheForTest.GetRealizationMode(pathB), FileRealizationMode.Copy);
                XAssert.AreEqual(artifactContentCacheForTest.GetRealizationMode(pathC), FileRealizationMode.Copy);
            }
            else
            {
                // technically, we may still be able to write to these, but we should assume not because BuildXL may use hard links
                XAssert.AreEqual(artifactContentCacheForTest.GetRealizationMode(pathA), FileRealizationMode.HardLinkOrCopy);
                XAssert.AreEqual(artifactContentCacheForTest.GetRealizationMode(pathB), FileRealizationMode.HardLinkOrCopy);
                XAssert.AreEqual(artifactContentCacheForTest.GetRealizationMode(pathC), FileRealizationMode.HardLinkOrCopy);
            }
        }
    }
}
