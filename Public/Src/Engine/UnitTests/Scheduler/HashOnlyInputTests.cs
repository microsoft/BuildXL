// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Ipc.Common;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.Scheduler.Utils;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    [Trait("Category", "FileContentManagerTests")]
    public sealed class HashOnlyInputTests : SchedulerTestBase
    {
        public HashOnlyInputTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void IpcServiceHashOnlyInputsAreMetadataDependencies()
        {
            // Remote service startup needs hash-only metadata, but ordinary IPC input placement must exclude it.
            using var pipTable = new PipTable(Context.PathTable, Context.SymbolTable, 1024, 1, debug: false);
            var configFile = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "config.dsc"));
            var config = ConfigurationHelpers.GetDefaultForTesting(Context.PathTable, configFile);
            var env = new DummyPipExecutionEnvironment(
                CreateLoggingContextForTest(), Context, config, pipTable: pipTable, sandboxConnection: GetEBPFAwareSandboxConnection());
            var fcm = new FileContentManager(env, new NullOperationTracker());
            var directory = new DirectoryArtifact(ObjectRootPath, 1, isSharedOpaque: true);
            var member = CreateOutputFileArtifact();
            env.RegisterDynamicOutputDirectory(directory);
            fcm.ReportDynamicDirectoryContents(
                directory, new[] { FileArtifactWithAttributes.Create(member, FileExistence.Required) }, PipOutputOrigin.Produced);
            var service = new Utils.ProcessBuilder()
                .WithContext(Context)
                .WithExecutable(CreateSourceFile())
                .WithWorkingDirectory(ObjectRootPath)
                .WithStandardDirectory(ObjectRootPath)
                .WithArguments(new PipDataBuilder(Context.StringTable).ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping))
                .Build()
                .Override(
                    hashOnlyFileDependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] { member }),
                    hashOnlyDirectoryDependencies: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new[] { directory }));
            var serviceId = pipTable.Add(1, service);
            var ipc = IpcPip.CreateFromStringPayload(
                Context, ObjectRootPath,
                new IpcClientInfo(IpcMoniker.CreateNew().ToStringId(Context.StringTable), new ClientConfig()),
                "message", CreateProvenance(), servicePipDependencies: new[] { serviceId });

            var files = new HashSet<FileArtifact>();
            fcm.CollectPipInputsToMaterialize(pipTable, ipc, files);
            Assert.DoesNotContain(member, files);

            var directories = new MultiValueDictionary<FileArtifact, DirectoryArtifact>();
            fcm.CollectPipFiles(true, pipTable, ipc, files, directories, includeHashOnlyInputs: true);
            Assert.Contains(member, files);
            Assert.Contains(directory, directories[member]);

            files.Clear();
            fcm.CollectPipFiles(true, pipTable, ipc, files, includeHashOnlyInputs: true, shouldIncludeServiceFiles: _ => false);
            Assert.DoesNotContain(member, files);
        }

        [Fact]
        public void HashOnlyOpaqueMetadataIsNotAByteDependency()
        {
            // Preserve hash-only input roles in graph/cache metadata without collecting their bytes for placement.
            var configFile = AbsolutePath.Create(Context.PathTable, Path.Combine(SourceRoot, "config.dsc"));
            var config = ConfigurationHelpers.GetDefaultForTesting(Context.PathTable, configFile);
            var env = new DummyPipExecutionEnvironment(CreateLoggingContextForTest(), Context, config, sandboxConnection: GetEBPFAwareSandboxConnection());
            var fcm = new FileContentManager(env, new NullOperationTracker());
            var directory = new DirectoryArtifact(AbsolutePath.Create(Context.PathTable, A("t", "hashOnly")), 1, isSharedOpaque: true);
            var member = FileArtifact.CreateOutputFile(AbsolutePath.Create(Context.PathTable, A("t", "hashOnly", "member.txt")));
            var bytes = System.Text.Encoding.UTF8.GetBytes("content");
            fcm.ReportInputContent(member, FileMaterializationInfo.CreateWithUnknownName(
                new FileContentInfo(ContentHashingUtilities.HashBytes(bytes), bytes.Length)));
            env.RegisterDynamicOutputDirectory(directory);
            fcm.ReportDynamicDirectoryContents(
                directory,
                new[] { FileArtifactWithAttributes.Create(member, FileExistence.Required) },
                PipOutputOrigin.Produced);
            var executable = FileArtifact.CreateSourceFile(AbsolutePath.Create(Context.PathTable, A("t", "tool.exe")));
            var process = new Utils.ProcessBuilder()
                .WithContext(Context)
                .WithExecutable(executable)
                .WithWorkingDirectory(directory.Path)
                .WithStandardDirectory(directory.Path)
                .WithArguments(new PipDataBuilder(Context.StringTable).ToPipData(string.Empty, PipDataFragmentEscaping.NoEscaping))
                .Build()
                .Override(
                    hashOnlyFileDependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new[] { member }),
                    hashOnlyDirectoryDependencies: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(new[] { directory }));

            var graphInputs = new HashSet<FileOrDirectoryArtifact>();
            PipArtifacts.ForEachInput(process, input => graphInputs.Add(input), includeLazyInputs: false, includeHashOnlyInputs: true);
            Assert.Contains(FileOrDirectoryArtifact.Create(member), graphInputs);
            Assert.Contains(FileOrDirectoryArtifact.Create(directory), graphInputs);

            var regularInputs = new HashSet<FileOrDirectoryArtifact>();
            PipArtifacts.ForEachInput(process, input => regularInputs.Add(input), includeLazyInputs: false, includeHashOnlyInputs: false);
            Assert.DoesNotContain(FileOrDirectoryArtifact.Create(member), regularInputs);
            Assert.DoesNotContain(FileOrDirectoryArtifact.Create(directory), regularInputs);

            var inputRoles = new Dictionary<FileOrDirectoryArtifact, bool>();
            Assert.True(PipArtifacts.ForEachInput(process, (input, isHashOnly) =>
            {
                inputRoles.Add(input, isHashOnly);
                return true;
            }, includeLazyInputs: false, includeHashOnlyInputs: true));
            Assert.Equal(graphInputs.Count, inputRoles.Count);
            Assert.True(inputRoles[FileOrDirectoryArtifact.Create(member)]);
            Assert.True(inputRoles[FileOrDirectoryArtifact.Create(directory)]);
            foreach (var input in regularInputs)
            {
                Assert.False(inputRoles[input]);
            }

            var roleAwareRegularInputs = new HashSet<FileOrDirectoryArtifact>();
            Assert.True(PipArtifacts.ForEachInput(process, (input, isHashOnly) =>
            {
                Assert.False(isHashOnly);
                return roleAwareRegularInputs.Add(input);
            }, includeLazyInputs: false, includeHashOnlyInputs: false));
            Assert.True(regularInputs.SetEquals(roleAwareRegularInputs));

            int visitedInputs = 0;
            Assert.False(PipArtifacts.ForEachInput(process, (input, isHashOnly) =>
            {
                visitedInputs++;
                return false;
            }, includeLazyInputs: false, includeHashOnlyInputs: true));
            Assert.Equal(1, visitedInputs);

            var cacheInfo = CacheableProcess.GetProcessCacheInfo(process, Context);
            Assert.DoesNotContain(member, cacheInfo.RegularDependencies);
            Assert.DoesNotContain(directory, cacheInfo.RegularDirectoryDependencies);
            Assert.Contains(member, cacheInfo.Dependencies);
            Assert.Contains(directory, cacheInfo.DirectoryDependencies);

            var materializedFiles = new HashSet<FileArtifact>();
            fcm.CollectPipInputsToMaterialize(env.PipTable, process, materializedFiles);
            Assert.DoesNotContain(member, materializedFiles);

            var metadataFiles = new HashSet<FileArtifact>();
            var directories = new MultiValueDictionary<FileArtifact, DirectoryArtifact>();
            fcm.CollectPipFiles(true, env.PipTable, process, metadataFiles, directories, includeHashOnlyInputs: true);
            Assert.Contains(member, metadataFiles);
            Assert.Contains(directory, directories[member]);
            Assert.False(File.Exists(member.Path.ToString(Context.PathTable)));
        }
    }
}
