// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.Scheduler.Tracing;
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
        public virtual void VsoHashOfOpaqueDirectorySucceeds(bool isSharedOpaque)
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

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData(SealDirectoryKind.Opaque, false)]
        [InlineData(SealDirectoryKind.Opaque, true)]
        [InlineData(SealDirectoryKind.SharedOpaque, false)]
        [InlineData(SealDirectoryKind.SharedOpaque, true)]
        public virtual void HashOnlyDependencyMaterialization(SealDirectoryKind? directoryKind, bool readInput)
        {
            // Delete the producer output and force only the consumer to rerun with a cached producer.
            // The file must remain absent for hash-only inputs and be restored for reads.
            // Changing the producer's source must still invalidate the consumer.
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            var directoryPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "hashInput"));
            var producedFile = CreateOutputFileArtifact(directoryPath.ToString(Context.PathTable));
            var producerSource = CreateSourceFile();
            var producerBuilder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(producerSource),
                Operation.CopyFile(producerSource, producedFile, doNotInfer: directoryKind.HasValue)
            });
            if (directoryKind.HasValue)
            {
                producerBuilder.AddOutputDirectory(directoryPath, directoryKind.Value);
            }

            var producer = SchedulePipBuilder(producerBuilder);
            var consumerSource = CreateSourceFile();
            var consumerOutput = CreateOutputFileArtifact();
            var operations = new System.Collections.Generic.List<Operation>
            {
                Operation.ReadFile(consumerSource),
                Operation.WriteFile(consumerOutput)
            };

            if (readInput)
            {
                // Infer regular file inputs; directory reads are declared through their directory below.
                operations.Add(Operation.ReadFile(producedFile, doNotInfer: directoryKind.HasValue));
            }

            var hashInput = directoryKind.HasValue
                ? FileOrDirectoryArtifact.Create(producer.ProcessOutputs.GetOpaqueDirectory(directoryPath))
                : FileOrDirectoryArtifact.Create(producedFile);
            operations.Add(Operation.EchoVsoHash(hashInput));
            var builder = CreatePipBuilder(operations);
            if (directoryKind.HasValue)
            {
                var directory = producer.ProcessOutputs.GetOpaqueDirectory(directoryPath);
                if (readInput)
                {
                    builder.AddInputDirectory(directory);
                }
            }
            builder.AddTags(Context.StringTable, "consumer");
            var consumer = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess().AssertCacheMiss(producer.Process.PipId, consumer.Process.PipId);
            Assert.True(File.Exists(ArtifactToString(producedFile)));
            File.Delete(ArtifactToString(producedFile));
            File.WriteAllText(ArtifactToString(consumerSource), "Force consumer execution");
            Configuration.Filter = "tag='consumer'";

            RunScheduler().AssertSuccess().AssertCacheHit(producer.Process.PipId).AssertCacheMiss(consumer.Process.PipId);
            Assert.Equal(readInput, File.Exists(ArtifactToString(producedFile)));

            File.WriteAllText(ArtifactToString(producerSource), "Change producer content");
            RunScheduler().AssertSuccess().AssertCacheMiss(producer.Process.PipId, consumer.Process.PipId);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public virtual void HashOnlyDependenciesDoNotAuthorizeReads(bool directoryInput)
        {
            var directoryPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "hashOnly"));
            var file = CreateOutputFileArtifact(directoryPath.ToString(Context.PathTable));
            var producerBuilder = CreatePipBuilder(new[] { Operation.WriteFile(file, content: "present") });
            producerBuilder.AddOutputDirectory(directoryPath, SealDirectoryKind.SharedOpaque);
            var producer = SchedulePipBuilder(producerBuilder);
            var consumerBuilder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(file, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            if (directoryInput)
            {
                consumerBuilder.AddHashInputDirectory(producer.ProcessOutputs.GetOpaqueDirectory(directoryPath));
            }
            else
            {
                consumerBuilder.AddHashInputFile(file);
            }

            SchedulePipBuilder(consumerBuilder);
            RunScheduler().AssertFailure();
            AssertVerboseEventLogged(
                global::BuildXL.Processes.Tracing.LogEventId.PipProcessDisallowedFileAccess,
                count: 1,
                // Unix may report separate denied probe and read accesses for the same file.
                allowMore: OperatingSystemHelper.IsUnixOS);
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public virtual void HashOnlyInputsWithRegularDirectoryAtSameRoot(bool regularDirectoryContainsHashFile)
        {
            // Same-root shared opaques retain distinct membership: restore only regular-directory members,
            // even when a regular member is also declared as a hash-only file input.
            Configuration.Schedule.EnableLazyOutputMaterialization = true;

            var directoryPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "overlappingOpaque"));
            var regularFile = CreateUniqueObjPath("regularFile", directoryPath.ToString(Context.PathTable));
            var hashOnlyMember = CreateUniqueObjPath("hashOnlyMember", directoryPath.ToString(Context.PathTable));
            
            var regularProducerBuilder = CreatePipBuilder(new[] { Operation.WriteFile(FileArtifact.CreateOutputFile(regularFile), doNotInfer: true) });
            regularProducerBuilder.AddOutputDirectory(directoryPath, SealDirectoryKind.SharedOpaque);
            var regularProducer = SchedulePipBuilder(regularProducerBuilder);

            var hashProducerBuilder = CreatePipBuilder(new[] { Operation.WriteFile(FileArtifact.CreateOutputFile(hashOnlyMember), doNotInfer: true) });
            hashProducerBuilder.AddOutputDirectory(directoryPath, SealDirectoryKind.SharedOpaque);
            var hashProducer = SchedulePipBuilder(hashProducerBuilder);

            var regularDirectory = regularProducer.ProcessOutputs.GetOpaqueDirectory(directoryPath);
            var hashDirectory = hashProducer.ProcessOutputs.GetOpaqueDirectory(directoryPath);
            Assert.True(PipConstructionHelper.TryAssertOutputExistenceInOpaqueDirectory(
                regularDirectory, regularFile, out var regularInput));
            Assert.True(PipConstructionHelper.TryAssertOutputExistenceInOpaqueDirectory(
                regularDirectoryContainsHashFile ? regularDirectory : hashDirectory,
                regularDirectoryContainsHashFile ? regularFile : hashOnlyMember,
                out var hashFile));

            var source = CreateSourceFile();
            var consumerOutput = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(regularInput, doNotInfer: true),
                Operation.ReadFile(source),
                Operation.WriteFile(consumerOutput)
            });
            builder.AddInputDirectory(regularDirectory);
            builder.AddHashInputDirectory(hashDirectory);
            builder.AddHashInputFile(hashFile);
            builder.AddTags(Context.StringTable, "consumer");
            var consumer = SchedulePipBuilder(builder);
            Assert.Contains(hashDirectory, consumer.Process.HashOnlyDirectoryDependencies);
            Assert.Contains(hashFile, consumer.Process.HashOnlyFileDependencies);
            RunScheduler().AssertSuccess();

            File.Delete(regularFile.ToString(Context.PathTable));
            File.Delete(hashOnlyMember.ToString(Context.PathTable));
            File.WriteAllText(ArtifactToString(source), "Force execution");
            Configuration.Filter = "tag='consumer'";
            RunScheduler().AssertSuccess()
                .AssertCacheHit(regularProducer.Process.PipId, hashProducer.Process.PipId)
                .AssertCacheMiss(consumer.Process.PipId);
            Assert.True(File.Exists(regularFile.ToString(Context.PathTable)));
            Assert.False(File.Exists(hashOnlyMember.ToString(Context.PathTable)));
        }

        [Fact]
        public virtual void ActualReadOverridesHashOnlyOpaque()
        {
            // A regular file input inside a hash-only opaque is materialized without restoring its hash-only sibling.
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            var directoryPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "mixedOpaque"));
            var actualInput = CreateOutputFileArtifact(directoryPath.ToString(Context.PathTable));
            var hashOnlyMember = CreateOutputFileArtifact(directoryPath.ToString(Context.PathTable));
            var producerBuilder = CreatePipBuilder(new[]
            {
                Operation.WriteFile(actualInput),
                Operation.WriteFile(hashOnlyMember, doNotInfer: true)
            });
            producerBuilder.AddOutputDirectory(directoryPath, SealDirectoryKind.SharedOpaque);
            var producer = SchedulePipBuilder(producerBuilder);

            var source = CreateSourceFile();
            var consumerBuilder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(actualInput),
                Operation.ReadFile(source),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            var directory = producer.ProcessOutputs.GetOpaqueDirectory(directoryPath);
            consumerBuilder.AddHashInputDirectory(directory);
            consumerBuilder.AddTags(Context.StringTable, "consumer");
            var consumer = SchedulePipBuilder(consumerBuilder);
            RunScheduler().AssertSuccess();

            File.Delete(ArtifactToString(actualInput));
            File.Delete(ArtifactToString(hashOnlyMember));
            File.WriteAllText(ArtifactToString(source), "Force execution");
            Configuration.Filter = "tag='consumer'";
            RunScheduler().AssertSuccess().AssertCacheHit(producer.Process.PipId).AssertCacheMiss(consumer.Process.PipId);
            Assert.True(File.Exists(ArtifactToString(actualInput)));
            Assert.False(File.Exists(ArtifactToString(hashOnlyMember)));
        }

        [Fact]
        public virtual void HashOnlyOpaqueMembershipChangesInvalidateConsumer()
        {
            // Membership changes, including becoming empty, invalidate the consumer even when member content is unchanged.
            // A cached producer must not restore members when the consumer reruns.
            Configuration.Schedule.EnableLazyOutputMaterialization = true;
            var directoryPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "membershipOpaque"));
            var firstMember = CreateOutputFileArtifact(directoryPath.ToString(Context.PathTable));
            var secondMember = CreateOutputFileArtifact(directoryPath.ToString(Context.PathTable));
            var membershipSource = CreateSourceFile();
            File.WriteAllText(ArtifactToString(membershipSource), "first");
            var producerBuilder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(membershipSource),
                Operation.WriteFileIfInputEqual(firstMember, ArtifactToString(membershipSource), "first", "same content"),
                Operation.WriteFileIfInputEqual(secondMember, ArtifactToString(membershipSource), "second", "same content")
            });
            producerBuilder.AddOutputDirectory(directoryPath, SealDirectoryKind.SharedOpaque);
            var producer = SchedulePipBuilder(producerBuilder);

            var consumerSource = CreateSourceFile();
            var consumerOutput = CreateOutputFileArtifact();
            var directory = producer.ProcessOutputs.GetOpaqueDirectory(directoryPath);
            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(consumerSource),
                Operation.WriteFile(consumerOutput),
                Operation.EchoVsoHash(FileOrDirectoryArtifact.Create(directory))
            });
            builder.AddTags(Context.StringTable, "consumer");
            var consumer = SchedulePipBuilder(builder);
            
            RunScheduler().AssertSuccess().AssertCacheMiss(producer.Process.PipId, consumer.Process.PipId);
            Assert.True(File.Exists(ArtifactToString(firstMember)));
            Assert.False(File.Exists(ArtifactToString(secondMember)));

            File.Delete(ArtifactToString(firstMember));
            File.WriteAllText(ArtifactToString(membershipSource), "second");
            Configuration.Filter = "tag='consumer'";
            RunScheduler().AssertSuccess().AssertCacheMiss(producer.Process.PipId, consumer.Process.PipId);
            Assert.False(File.Exists(ArtifactToString(firstMember)));
            Assert.True(File.Exists(ArtifactToString(secondMember)));

            File.Delete(ArtifactToString(secondMember));
            File.WriteAllText(ArtifactToString(consumerSource), "Force execution with cached directory metadata");
            RunScheduler().AssertSuccess().AssertCacheHit(producer.Process.PipId).AssertCacheMiss(consumer.Process.PipId);
            Assert.False(File.Exists(ArtifactToString(firstMember)));
            Assert.False(File.Exists(ArtifactToString(secondMember)));

            File.WriteAllText(ArtifactToString(membershipSource), "empty");
            RunScheduler().AssertSuccess().AssertCacheMiss(producer.Process.PipId, consumer.Process.PipId);
            Assert.False(File.Exists(ArtifactToString(firstMember)));
            Assert.False(File.Exists(ArtifactToString(secondMember)));
        }
    }
}