// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Engine;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Processes;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;
using Process = BuildXL.Pips.Operations.Process;

namespace Test.BuildXL.Scheduler
{
    [Trait("Category", "PipExecutorTest")]
    public sealed class PipExecutorTest : TemporaryStorageTestBase
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int chmod(string path, int mode);

        private static readonly string TestPath = OperatingSystemHelper.IsUnixOS
            ? "/tmp/TestPath/test"
            : @"\\TestPath\test";

        public PipExecutorTest(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Engine.Cache.ETWLogger.Log);
        }

        [Fact]
        public async Task WriteFile()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();
                    PipData contents = PipDataBuilder.CreatePipData(
                        env.Context.StringTable,
                        " ",
                        PipDataFragmentEscaping.CRuntimeArgumentRules,
                        "Success");
                    var pip = new WriteFile(destinationArtifact, contents, WriteFileEncoding.Utf8, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual("Success", actual);
                });
        }

        [Fact]
        public async Task WriteFileSkippedIfUpToDate()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();
                    PipData contents = PipDataBuilder.CreatePipData(
                        env.Context.StringTable,
                        " ",
                        PipDataFragmentEscaping.CRuntimeArgumentRules,
                        "Success");
                    var pip = new WriteFile(destinationArtifact, contents, WriteFileEncoding.Utf8, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));

                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    await VerifyPipResult(PipResultStatus.UpToDate, env, pip);
                    await VerifyPipResult(PipResultStatus.UpToDate, env, pip);

                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual("Success", actual);
                });
        }

        [Fact]
        public async Task WriteFilePathTooLong()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string destination = GetFullPath(Path.Combine(new string('a', 260), "file.txt"));
                    AbsolutePath destinationPath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationPath).CreateNextWrittenVersion();
                    PipData contents = PipDataBuilder.CreatePipData(
                        env.Context.StringTable,
                        " ",
                        PipDataFragmentEscaping.CRuntimeArgumentRules,
                        "Success");
                    var pip = new WriteFile(destinationArtifact, contents, WriteFileEncoding.Utf8, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    await VerifyPipResult(PipResultStatus.Failed, env, pip);
                });

            SetExpectedFailures(1, 0, "DX0006");
        }

        [Fact]
        public async Task CopyFile()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourceAbsolutePath);

                    string destination = GetFullPath("dest");

                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                    File.WriteAllText(source, "Success");

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    XAssert.IsTrue(sourceArtifact.IsSourceFile,
                        "Source artifact must be a 'source' file (write count 0) since CopyFile conditionally stores content to the cache (intermediate content should be stored already when it runs)");
                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual("Success", actual);
                });
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public async Task CopySymlinkTest(bool allowCopySymlink, bool storeOutputsToCache)
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source.link");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourceAbsolutePath);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                    string sourceTarget = GetFullPath("source.txt");
                    File.WriteAllText(sourceTarget, "Success");
                    XAssert.PossiblySucceeded(FileUtilities.TryCreateSymbolicLink(source, sourceTarget, true), "Unable to create symlink");

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    await VerifyPipResult(allowCopySymlink ? PipResultStatus.Succeeded : PipResultStatus.Failed, env, pip);
                    if (allowCopySymlink)
                    {
                        string actual = File.ReadAllText(destination);
                        XAssert.AreEqual("Success", actual);
                    }
                },
                config: pathTable => GetConfiguration(pathTable, allowCopySymlink: allowCopySymlink, storeOutputsToCache: storeOutputsToCache));

            if (!allowCopySymlink)
            {
                SetExpectedFailures(1, 0, "DX0008");
            }
        }

        [Fact]
        public async Task CopyFileWhenSourceIsIntermediate()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourceAbsolutePath).CreateNextWrittenVersion();

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                    // The copy-source is an intermediate file. We need to respect the invariant that on-disk intermediate files are
                    // also in cache.
                    File.WriteAllText(source, "Success");
                    var possiblyStored = await env.LocalDiskContentStore.TryStoreAsync(
                        env.Cache.ArtifactContentCache,
                        FileRealizationMode.Copy,
                        sourceAbsolutePath,
                        tryFlushPageCacheToFileSystem: true);
                    if (!possiblyStored.Succeeded)
                    {
                        XAssert.Fail("Failed to store copy-source to cache: {0}", possiblyStored.Failure.DescribeIncludingInnerFailures());
                    }

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    XAssert.IsTrue(sourceArtifact.IsOutputFile,
                        "Source artifact must be an 'intermediate' file (write count > 0) since CopyFile conditionally stores content to the cache (intermediate content should be stored already when it runs)");
                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual("Success", actual);
                });
        }

        [Fact]
        public async Task CopyFileDestinationDirectoryPathTooLong()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourcePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourcePath);

                    string destination = GetFullPath(Path.Combine(new string('a', 260), "file.txt"));
                    AbsolutePath destinationPath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationPath).CreateNextWrittenVersion();

                    File.WriteAllText(source, "Success");

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    XAssert.IsTrue(sourceArtifact.IsSourceFile,
                        "Source artifact must be a 'source' file (write count 0) since CopyFile conditionally stores content to the cache (intermediate content should be stored already when it runs)");
                    await VerifyPipResult(PipResultStatus.Failed, env, pip);
                });

            SetExpectedFailures(1, 1, "DX0737", "DX0008");
        }

        [Fact]
        public async Task CopyFileSkippedIfUpToDate()
        {
            const string Contents = "Matches!";

            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourceAbsolutePath);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                    File.WriteAllText(destination, Contents);
                    File.WriteAllText(source, Contents);

                    // We need the destination in the file content table to know its up to date.
                    await env.FileContentTable.GetAndRecordContentHashAsync(destination);

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    XAssert.IsTrue(sourceArtifact.IsSourceFile,
                        "Source artifact must be a 'source' file (write count 0) since CopyFile conditionally stores content to the cache (intermediate content should be stored already when it runs)");
                    await VerifyPipResult(PipResultStatus.UpToDate, env, pip);
                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual(Contents, actual);
                });
        }

        [Fact]
        public async Task CopyFileSkippedIfUpToDateViaPreviousCopy()
        {
            const string Contents = "Matches!";

            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourceAbsolutePath);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                    File.WriteAllText(destination, Contents);
                    File.WriteAllText(source, Contents);

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));

                    XAssert.IsTrue(sourceArtifact.IsSourceFile,
                        "Source artifact must be a 'source' file (write count 0) since CopyFile conditionally stores content to the cache (intermediate content should be stored already when it runs)");
                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    await VerifyPipResult(PipResultStatus.UpToDate, env, pip);
                    await VerifyPipResult(PipResultStatus.UpToDate, env, pip);

                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual(Contents, actual);
                });
        }

        [Fact]
        public async Task CopyFileProceedsIfMismatched()
        {
            const string Contents = "Matches!";

            // It's important that BadContents is longer to make sure truncation occurs.
            const string BadContents = "Anti-Matches!";

            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourceAbsolutePath);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateSourceFile(destinationAbsolutePath).CreateNextWrittenVersion();

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));
                    XAssert.IsTrue(sourceArtifact.IsSourceFile,
                        "Source artifact must be a 'source' file (write count 0) since CopyFile conditionally stores content to the cache (intermediate content should be stored already when it runs)");
                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual(Contents, actual);
                });
        }

        [Fact]
        public async Task CopyFileStoreNoOutputToCacheMaterializeDestination()
        {
            const string Contents = nameof(CopyFileStoreNoOutputToCacheMaterializeDestination);

            await WithExecutionEnvironment(
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                    FileArtifact sourceArtifact = FileArtifact.CreateSourceFile(sourceAbsolutePath);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);
                    FileArtifact destinationArtifact = FileArtifact.CreateOutputFile(destinationAbsolutePath);

                    File.WriteAllText(source, Contents);

                    var pip = new CopyFile(sourceArtifact, destinationArtifact, ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(env.Context));

                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    XAssert.Equals(PipOutputOrigin.Produced, env.State.FileContentManager.GetPipOutputOrigin(destinationArtifact));

                    await VerifyPipResult(PipResultStatus.UpToDate, env, pip);
                    XAssert.Equals(PipOutputOrigin.UpToDate, env.State.FileContentManager.GetPipOutputOrigin(destinationArtifact));

                    await VerifyPipResult(PipResultStatus.UpToDate, env, pip);
                    XAssert.Equals(PipOutputOrigin.UpToDate, env.State.FileContentManager.GetPipOutputOrigin(destinationArtifact));

                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual(Contents, actual);
                },
                config: pathTable => GetConfiguration(pathTable, storeOutputsToCache: false));
        }

        [Fact]
        public async Task ProcessWithoutCache()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath);

                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    XAssert.AreEqual(1, env.OutputFilesProduced, "produced count");
                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
                    XAssert.AreEqual(2, env.OutputFilesProduced, "produced count");

                    string actual = File.ReadAllText(destination);
                    XAssert.AreEqual(Contents, actual);
                });
        }

        [Fact]
        public async Task TestServicePipReceivesAggregatedPermissions()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    string destination2 = GetFullPath("dest2");
                    AbsolutePath destinationAbsolutePath2 = AbsolutePath.Create(env.Context.PathTable, destination2);

                    File.WriteAllText(source, "123");

                    // because omitDependencies: true, 'servicePip' would fail if it didn't receive permissions of 'clientPip'
                    Process servicePip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, omitDependencies: true, serviceInfo: ServiceInfo.Service(new PipId(324)));
                    Process clientPip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath2, serviceInfo: ServiceInfo.ServiceClient(new[] { servicePip.PipId }));
                    env.SetServicePipClients(new Dictionary<PipId, IReadOnlyCollection<Pip>>
                    {
                        [servicePip.PipId] = new[] { clientPip }
                    });

                    var testRunChecker = new TestRunChecker();
                    await testRunChecker.VerifySucceeded(env, servicePip);
                    await testRunChecker.VerifySucceeded(env, clientPip);
                });
        }

        [Fact]
        public Task TestSucceedingIpcPip()
        {
            var ipcProvider = new DummyIpcProvider(statusToAlwaysReturn: IpcResultStatus.Success);
            return TestIpcPip(ipcProvider, true);
        }

        [Fact]
        public Task TestFailingIpcPip()
        {
            var ipcProvider = new DummyIpcProvider(statusToAlwaysReturn: IpcResultStatus.GenericError);
            return TestIpcPip(ipcProvider, false);
        }

        private async Task TestIpcPip(IIpcProvider ipcProvider, bool expectedToSucceed)
        {
            var ipcOperationPayload = "hi";

            await WithExecutionEnvironmentAndIpcServer(
                ipcProvider,
                ipcExecutor: new LambdaIpcOperationExecutor((op) => IpcResult.Success(op.Payload)), // the server echoes whatever operation payload it receives
                act: async (env, moniker, ipcServer) => // ipcServer has been started; ipc pips will connect to it; 
                {
                    var workingDir = AbsolutePath.Create(env.Context.PathTable, GetFullPath("ipc-wd"));

                    // construct an IPC pip
                    var ipcInfo = new IpcClientInfo(moniker.ToStringId(env.Context.StringTable), new ClientConfig());
                    var ipcPip = AssignFakePipId(IpcPip.CreateFromStringPayload(env.Context, workingDir, ipcInfo, ipcOperationPayload, PipProvenance.CreateDummy(env.Context)));

                    var expectedOutputFile = ipcPip.OutputFile.Path.ToString(env.Context.PathTable);
                    var expectedOutputFileContent = ipcOperationPayload;

                    // execute pip several times
                    if (expectedToSucceed)
                    {
                        var testRunChecker = new TestRunChecker(expectedOutputFile);
                        await testRunChecker.VerifySucceeded(env, ipcPip, expectedOutputFileContent, expectMarkedPerpertuallyDirty: true);
                        await testRunChecker.VerifyUpToDate(env, ipcPip, expectedOutputFileContent, expectMarkedPerpertuallyDirty: true);
                        File.Delete(expectedOutputFile);
                        await testRunChecker.VerifySucceeded(env, ipcPip, expectedOutputFileContent, expectMarkedPerpertuallyDirty: true);
                    }
                    else
                    {
                        var testRunChecker = new TestRunChecker();
                        await testRunChecker.VerifyFailed(env, ipcPip, expectMarkedPerpertuallyDirty: true);
                        AssertErrorEventLogged(EventId.PipIpcFailed, count: 1);
                        await testRunChecker.VerifyFailed(env, ipcPip, expectMarkedPerpertuallyDirty: true);
                        AssertErrorEventLogged(EventId.PipIpcFailed, count: 1);
                    }
                },
                cache: InMemoryCacheFactory.Create);
        }

        [Fact]
        public async Task ProcessWithCache()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    await testRunChecker.VerifySucceeded(env, pip, Contents);

                    await testRunChecker.VerifyUpToDate(env, pip, Contents);

                    File.Delete(destination);

                    await testRunChecker.VerifyDeployedFromCache(env, pip, Contents);
                });
        }

        private static IConfiguration GetUnsafeOptions(PathTable pathTable)
            => GetConfiguration(pathTable, monitorFileAccesses: true, unexpectedFileAccessesAreErrors: false);

        private static IConfiguration GetSaferOptions(PathTable pathTable)
            => GetConfiguration(pathTable, monitorFileAccesses: true, unexpectedFileAccessesAreErrors: true);

        [Fact]
        public async Task ExecutingProcessWithLessSafeSandboxOptionsShouldGetCacheHit()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            var context = BuildXLContext.CreateInstanceForTesting();
            var cache = InMemoryCacheFactory.Create();
            
            string source = GetFullPath("source");
            string destination = GetFullPath("dest");
            File.WriteAllText(source, Contents);
            File.WriteAllText(destination, BadContents);

            Func<IPipExecutionEnvironment, Process> createPip = (env) =>
            {
                AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                return CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath);
            };

            // run the pip with "safer options" (monitorFileAccesses: true) --> expect it to succeed
            var env1 = CreateExecutionEnvironment(context, cache: () => cache, config: GetSaferOptions);
            await new TestRunChecker(destination).VerifySucceeded(env1, createPip(env1), Contents);

            // run the pip with "less safe options" (monitorFileAccesses: false) --> expect cache hit
            var env2 = CreateExecutionEnvironment(context, cache: () => cache, config: GetUnsafeOptions);
            await new TestRunChecker(destination).VerifyDeployedFromCache(env2, createPip(env2), Contents);
        }

        [Fact]
        public async Task ExecutingProcessWithMoreSafeSandboxOptionsShouldGetCacheMiss()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            var context = BuildXLContext.CreateInstanceForTesting();
            var cache = InMemoryCacheFactory.Create();

            string source = GetFullPath("source");
            string destination = GetFullPath("dest");
            File.WriteAllText(source, Contents);
            File.WriteAllText(destination, BadContents);

            Func<IPipExecutionEnvironment, Process> createPip = (env) =>
            {
                AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                return CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath);
            };

            // run the pip with "unsafe options" (monitorFileAccesses: false, unexpecteFileAccessesAreErrors: false) --> expect it to succeed
            var env1 = CreateExecutionEnvironment(context, cache: () => cache, config: GetUnsafeOptions);
            await new TestRunChecker(destination).VerifySucceeded(env1, createPip(env1), Contents);

            // run the pip with "safer options" (monitorFileAccesses: true, unexpecteFileAccessesAreErrors: false) --> expect cache miss
            var env2 = CreateExecutionEnvironment(context, cache: () => cache, config: GetSaferOptions);
            var pip2 = createPip(env2);
            var testChecker = new TestRunChecker(destination);
            await testChecker.VerifySucceeded(env2, pip2, Contents);

            // run the same pip again against the same environment --> expect 'UpToDate' cache hit 
            await testChecker.VerifyUpToDate(env2, pip2, Contents);

            // run the same pip again against a new environment with the same unsafe options --> expect 'DeployedFromCache' cache hit
            var env3 = CreateExecutionEnvironment(context, cache: () => cache, config: GetSaferOptions);
            await new TestRunChecker(destination).VerifyDeployedFromCache(env3, createPip(env3), Contents);
        }

        [Fact]
        public async Task ExecutingProcessWithPreserveOutputOnShouldGetCacheHit()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            var context = BuildXLContext.CreateInstanceForTesting();
            var cache = InMemoryCacheFactory.Create();

            string source = GetFullPath("source");
            string destination = GetFullPath("dest");
            File.WriteAllText(source, Contents);
            File.WriteAllText(destination, BadContents);

            Func<IPipExecutionEnvironment, Process> createPip = (env) =>
            {
                AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                return CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, options: Process.Options.AllowPreserveOutputs);
            };

            // run the pip with "safer options" (preserveOutputs: disabled) --> expect it to succeed
            var env1 = CreateExecutionEnvironment(
                context, 
                cache: () => cache, 
                config: pt => GetConfiguration(pt, preserveOutputs: PreserveOutputsMode.Disabled));
            await new TestRunChecker(destination).VerifySucceeded(env1, createPip(env1), Contents);

            // run the pip with "less safe options" (preserveOutputs: enabled) --> expect cache hit
            var env2 = CreateExecutionEnvironment(
                context, 
                cache: () => cache, 
                config: pt => GetConfiguration(pt, preserveOutputs: PreserveOutputsMode.Enabled));
            await new TestRunChecker(destination).VerifyDeployedFromCache(env2, createPip(env2), Contents);
        }

        [Fact]
        public async Task ExecutingProcessWithDifferentPreserveOutputSaltShouldGetCacheMiss()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            var context = BuildXLContext.CreateInstanceForTesting();
            var cache = InMemoryCacheFactory.Create();

            string source = GetFullPath("source");
            string destination = GetFullPath("dest");
            File.WriteAllText(source, Contents);
            File.WriteAllText(destination, BadContents);

            Func<IPipExecutionEnvironment, Process> createPip = (env) =>
            {
                AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);
                AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                return CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, options: Process.Options.AllowPreserveOutputs);
            };

            // run the pip with (preserveOutputs: enabled) --> expect it to succeed
            var env1 = CreateExecutionEnvironment(
                context,
                cache: () => cache,
                config: pt => GetConfiguration(pt, preserveOutputs: PreserveOutputsMode.Enabled));
            var pip1 = createPip(env1);
            var testChecker = new TestRunChecker(destination);
            await testChecker.VerifySucceeded(env1, pip1, Contents);

            // run the same pip again against the same environment --> expect 'UpToDate' cache hit 
            await testChecker.VerifyUpToDate(env1, pip1, Contents);

            // run the pip with different preserve outputs salt (preserveOutputs: enabled) --> expect cache miss
            var env2 = CreateExecutionEnvironment(
                context,
                cache: () => cache,
                config: pt => GetConfiguration(pt, preserveOutputs: PreserveOutputsMode.Enabled));
            await new TestRunChecker(destination).VerifySucceeded(env2, createPip(env2), Contents);

            // run the pip with (preserveOutputs: disabled) --> expect cache miss
            var env3 = CreateExecutionEnvironment(
                context,
                cache: () => cache,
                config: pt => GetConfiguration(pt, preserveOutputs: PreserveOutputsMode.Disabled));
            await new TestRunChecker(destination).VerifySucceeded(env3, createPip(env3), Contents);
        }

        [Fact]
        public async Task TemporaryOutputsAreNotStoredInCacheIfAbsent()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    string temporaryOutput = GetFullPath("tempOutput");
                    AbsolutePath temporaryOutputAbsolutePath = AbsolutePath.Create(env.Context.PathTable, temporaryOutput);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, temporaryOutput: temporaryOutputAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    await testRunChecker.VerifySucceeded(env, pip, Contents);

                    await testRunChecker.VerifyUpToDate(env, pip, Contents);

                    File.Delete(destination);

                    await testRunChecker.VerifyDeployedFromCache(env, pip, Contents);
                });
        }

        [Fact]
        public async Task TemporaryOutputsAreNotStoredInCacheIfPresent()
        {
            const string Contents = "Matches!";
            const string TempContents = "TempMatches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    string temporaryOutput = GetFullPath("tempOutput");
                    AbsolutePath temporaryOutputAbsolutePath = AbsolutePath.Create(env.Context.PathTable, temporaryOutput);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);
                    File.WriteAllText(temporaryOutput, TempContents);

                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, temporaryOutput: temporaryOutputAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    await testRunChecker.VerifySucceeded(env, pip, Contents);

                    await testRunChecker.VerifyUpToDate(env, pip, Contents);

                    File.Delete(destination);
                    File.Delete(temporaryOutput);

                    await testRunChecker.VerifyDeployedFromCache(env, pip, Contents);

                    // Temporary outputs are not stored in cache, so should be only one OutputFilesProduced!
                    XAssert.IsFalse(File.Exists(temporaryOutput), "Temporary output should not be recovered from cache");
                });
        }

        [Fact]
        public async Task ProcessWithCacheAndOutputsForcedWritable()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    env.InMemoryContentCache.ReinitializeRealizationModeTracking();

                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    // We add an option to opt-out this single pip from hardlinking.
                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, options: Process.Options.OutputsMustRemainWritable);
                    var testRunChecker = new TestRunChecker(destination);

                    await testRunChecker.VerifySucceeded(env, pip, Contents);

                    await testRunChecker.VerifyUpToDate(env, pip, Contents);

                    // The file should be writable (i.e., copy realization mode), since we opted out of hardlinking. So we can modify it, and expect the pip to re-run.
                    XAssert.AreEqual(FileRealizationMode.Copy, env.InMemoryContentCache.GetRealizationMode(destination));
                    env.InMemoryContentCache.ReinitializeRealizationModeTracking();

                    try
                    {
                        File.WriteAllText(destination, BadContents);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        XAssert.Fail("Failed writing to the destination file. This implies that the outputs were not left writable, despite requesting that via Process.Options.OutputsMustRemainWritable");
                    }

                    // Now we re-run with read-only outputs allowed. This should be a cache hit (this option doesn't affect execution), leaving a read-only output.
                    Process hardlinkingPip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, options: Process.Options.None);

                    await testRunChecker.VerifyDeployedFromCache(env, hardlinkingPip, Contents);

                    // The file should not be writable (i.e., not copy realization mode) this time.
                    XAssert.AreNotEqual(FileRealizationMode.Copy, env.InMemoryContentCache.GetRealizationMode(destination));
                    env.InMemoryContentCache.ReinitializeRealizationModeTracking();

                    // Now re-run as a cache miss (should still be read-only; this is the production rather than deployment path).
                    File.WriteAllText(source, BadContents);

                    await testRunChecker.VerifySucceeded(env, hardlinkingPip, BadContents);

                    // The file should not be writable (i.e., not copy realization mode) this time.
                    XAssert.AreNotEqual(FileRealizationMode.Copy, env.InMemoryContentCache.GetRealizationMode(destination));
                    env.InMemoryContentCache.ReinitializeRealizationModeTracking();
                });
        }

        [Fact]
        public async Task ProcessWarningWithCache()
        {
            const string BadContents = "Anti-Matches!";
            string Expected = "WARNING" + Environment.NewLine;

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string workingDirectory = GetFullPath("work");
                    AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(env.Context.PathTable, workingDirectory);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);

                    Process pip = CreateWarningProcess(env.Context, workingDirectoryAbsolutePath, destinationAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    testRunChecker.ExpectWarning();
                    await testRunChecker.VerifySucceeded(env, pip, Expected);

                    testRunChecker.ExpectWarningFromCache();

                    await testRunChecker.VerifyUpToDate(env, pip, Expected);

                    File.Delete(destination);

                    testRunChecker.ExpectWarningFromCache();
                    await testRunChecker.VerifyDeployedFromCache(env, pip, Expected);
                },
                null,
                pathTable => GetConfiguration(pathTable, enableLazyOutputs: false));

            SetExpectedFailures(0, 3, "DX0065");
        }

        [Fact]
        public async Task ProcessWarningWithCacheAndWarnAsError()
        {
            const string BadContents = "Anti-Matches!";
            string Expected = "WARNING" + Environment.NewLine;

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string workingDirectory = GetFullPath("work");
                    AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(env.Context.PathTable, workingDirectory);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);

                    Process pip = CreateWarningProcess(env.Context, workingDirectoryAbsolutePath, destinationAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    testRunChecker.ExpectWarning();
                    await testRunChecker.VerifySucceeded(env, pip, Expected, expectMarkedPerpertuallyDirty: true);

                    // This warning should not come from the cache since /warnaserror is enabled
                    testRunChecker.ExpectWarning();

                    await testRunChecker.VerifySucceeded(env, pip, Expected, expectMarkedPerpertuallyDirty: true);
                },
                null,
                (pathTable) =>
                {
                    var config2 = ConfigurationHelpers.GetDefaultForTesting(pathTable, AbsolutePath.Create(pathTable, TestPath));
                    config2.Logging.TreatWarningsAsErrors = true;
                    return config2;
                });

            AssertWarningEventLogged(EventId.PipProcessWarning, 2);
            AssertInformationalEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.ScheduleProcessNotStoredToWarningsUnderWarnAsError, 2);
        }

        /// <summary>
        /// This test produces a failing process (determined by process return code) that is equipped with custom regex.
        /// Test verifies that
        /// 1) Process indeed fails.
        /// 2) Error log only contains the pattern that matches to regex.
        /// </summary>
        [Theory]
        [InlineData(OutputReportingMode.FullOutputAlways, 0)]
        [InlineData(OutputReportingMode.FullOutputOnError, 0)]
        [InlineData(OutputReportingMode.FullOutputOnWarningOrError, 0)]
        [InlineData(OutputReportingMode.TruncatedOutputOnError, 0)]
        [InlineData(OutputReportingMode.FullOutputAlways, 2 * SandboxedProcessPipExecutor.MaxConsoleLength)]
        [InlineData(OutputReportingMode.FullOutputOnError, 2 * SandboxedProcessPipExecutor.MaxConsoleLength)]
        [InlineData(OutputReportingMode.FullOutputOnWarningOrError, 2 * SandboxedProcessPipExecutor.MaxConsoleLength)]
        [InlineData(OutputReportingMode.TruncatedOutputOnError, 2 * SandboxedProcessPipExecutor.MaxConsoleLength)]
        [InlineData(OutputReportingMode.FullOutputOnError, 2 * SandboxedProcessPipExecutor.MaxConsoleLength, false)]
        [InlineData(OutputReportingMode.TruncatedOutputOnError, 2 * SandboxedProcessPipExecutor.MaxConsoleLength, true)]
        public async Task FailingProcessWithErrorRegex(OutputReportingMode outputReportingMode, int errorMessageLength, bool regexMatchesSomething = true)
        {
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string workingDirectory = GetFullPath("work");
                    AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(env.Context.PathTable, workingDirectory);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);

                    Process pip = CreateErrorProcess(
                        env.Context,
                        workingDirectoryAbsolutePath,
                        destinationAbsolutePath, 
                        errorPattern: "ERROR",
                        errorMessageLength: errorMessageLength);
                    var testRunChecker = new TestRunChecker();

                    await testRunChecker.VerifyFailed(env, pip);

                    AssertErrorEventLogged(EventId.PipProcessError);

                    string log = EventListener.GetLog();
                    XAssert.IsTrue(log.Contains("DX00" + (int)EventId.PipProcessError));
                    XAssert.IsTrue(log.Contains("ERROR"), "text 'ERROR' should not be filtered out by error regex.");

                    if (outputReportingMode == OutputReportingMode.TruncatedOutputOnError)
                    {
                        XAssert.IsFalse(log.Contains("WARNING"), "text 'WARNING' should be filtered out by error regex.");
                    }
                    else
                    {
                        // When full build output is requested, we expect to to have the non-filtered process output to appear in a DX0066 message
                        XAssert.IsTrue(log.Contains("DX00" + (int)EventId.PipProcessOutput));
                        XAssert.IsTrue(log.Contains("WARNING"));
                    }

                    // Validates that errors don't get truncated when the regex doesn't match anything
                    if (!regexMatchesSomething && outputReportingMode != OutputReportingMode.TruncatedOutputOnError)
                    {
                        // "Z" is a marker at the end of the error message. We should see this in the DX64 message as long as
                        // we aren't truncating the error
                        XAssert.IsTrue(Regex.IsMatch(log, $@"(?<Prefix>dx00{(int)EventId.PipProcessError})(?<AnythingButZ>[^z]*)(?<ZForEndOfError>z)", RegexOptions.IgnoreCase), 
                            "Non-truncated error message was not found in error event. Full output:" + log);
                    }
                    
                },
                null,
                pathTable => GetConfiguration(pathTable, enableLazyOutputs: false, outputReportingMode: outputReportingMode));
        }

        [Fact]
        public async Task FailingProcessWithChunkSizeErrorAndErrorRegex()
        {
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string workingDirectory = GetFullPath("work");
                    AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(env.Context.PathTable, workingDirectory);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);

                    var builder = new StringBuilder();
                    builder.AppendLine("@echo off");
                    for (int i = 0; i < SandboxedProcessPipExecutor.OutputChunkInLines * 3/2 ; ++i)
                    {
                        if (i % 2 == 0)
                        {
                            builder.AppendLine("echo ERROR - " + i);
                        }
                        else
                        {
                            builder.AppendLine("echo GOOD - " + i);
                        }
                    }

                    Process pip = CreateErrorProcess(
                        env.Context,
                        workingDirectoryAbsolutePath,
                        destinationAbsolutePath,
                        errorPattern: "ERROR",
                        errorMessageLength: 0,
                        scriptContent: builder.ToString());
                    var testRunChecker = new TestRunChecker();

                    await testRunChecker.VerifyFailed(env, pip);

                    AssertErrorEventLogged(EventId.PipProcessError, count: 1);
                },
                null,
                pathTable => GetConfiguration(pathTable, enableLazyOutputs: false, outputReportingMode: OutputReportingMode.FullOutputOnError));
        }

        [Trait(BuildXL.TestUtilities.Features.Feature, BuildXL.TestUtilities.Features.NonStandardOptions)]
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessUncacheableDueToFileMonitoringViolationsInSealedDirectory()
        {
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),

                // We set monitoring violations to warnings so the pip completes (though uncached).
                config: pathTable => GetConfiguration(pathTable, fileAccessIgnoreCodeCoverage: true, failUnexpectedFileAccesses: false, unexpectedFileAccessesAreErrors: false),
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    DirectoryArtifact directoryArtifact = SealDirectoryWithProbeTargets(env, sourceAbsolutePath, omitContents: true);
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    string expected = CreateDirectoryWithProbeTargets(env.Context.PathTable, sourceAbsolutePath, fileAContents: "A", fileBContents: "B2");
                    await testRunChecker.VerifySucceeded(env, pip, expected, expectMarkedPerpertuallyDirty: true);

                    // Expecting 4 warnings, of which 3 are collapsed into one due to similar file access type.
                    // Events ignore the function used for the access.
                    AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, count: 2);
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 1);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 1);

                    await testRunChecker.VerifySucceeded(env, pip, expected, expectMarkedPerpertuallyDirty: true);

                    // Expecting 4 warnings, of which 3 are collapsed into one due to similar file access type.
                    // Events ignore the function used for the access.
                    AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, count: 2);
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 1);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 1);
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessFailsDueToFileMonitoringViolationsInSealedDirectory()
        {
            await WithExecutionEnvironment(

                // We set monitoring violations to errors so the pip fails due to the directory dependency issue
                config: pathTable => GetConfiguration(pathTable, fileAccessIgnoreCodeCoverage: true, failUnexpectedFileAccesses: false, unexpectedFileAccessesAreErrors: true),
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    DirectoryArtifact directoryArtifact = SealDirectoryWithProbeTargets(env, sourceAbsolutePath, omitContents: true);
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath);
                    var testRunChecker = new TestRunChecker();

                    CreateDirectoryWithProbeTargets(env.Context.PathTable, sourceAbsolutePath, fileAContents: "A", fileBContents: "B2");
                    // Pip with file monitoring errors succeeds in PipExecutor but fails in the post-process step.
                    await testRunChecker.VerifySucceeded(env, pip, expectMarkedPerpertuallyDirty: true);
                    
                    AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, count: 2);
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 1);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);

                    // Uncacheable pip due to the file monitoring errors
                    await testRunChecker.VerifySucceeded(env, pip, expectMarkedPerpertuallyDirty: true);

                    AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, count: 2);
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 1);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessCachedWithWhitelistedFileMonitoringViolations()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),

                // We set monitoring violations to warnings so the pip completes.
                config: pathTable => GetConfiguration(pathTable, fileAccessIgnoreCodeCoverage: true, failUnexpectedFileAccesses: false),
                whitelistCreator: (context) =>
                {
                    var whitelist = new FileAccessWhitelist(context);

                    whitelist.Add(
                        new ExecutablePathWhitelistEntry(
                            AbsolutePath.Create(context.PathTable, CmdHelper.OsShellExe),
                            FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName("source"))),
                            allowsCaching: true,
                            name: "whitelist1"));
                    whitelist.Add(
                        new ExecutablePathWhitelistEntry(
                            AbsolutePath.Create(context.PathTable, CmdHelper.OsShellExe),
                            FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName("dest"))),
                            allowsCaching: true,
                            name: "whitelist2"));

                    return whitelist;
                },
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, omitDependencies: true);
                    var testRunChecker = new TestRunChecker(destination);
                    await testRunChecker.VerifySucceeded(env, pip, Contents);

                    // Expecting 3 events, of which 2 are collapsed into one due to similar file access type.
                    // Events ignore the function used for the access.
                    AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedCacheable, count: 2);

                    await testRunChecker.VerifyUpToDate(env, pip, Contents);
                    AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedCacheable, count: 0);
                });
        }

        // This test uses mock implementations for the interfaces needed by the EngineCache class and tests
        // the case when another file has posted a cache content for the same strong fingerprint.
        // In such case BuildXL detects the replaced local cache content with the remote one and uses that for consequent pip executions.
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessCachedForCacheConvergence()
        {
            string localContents;
            IgnoreWarnings();
            await WithCachingExecutionEnvironmentForCacheConvergence(
                GetFullPath(".cache"),

                // We set monitoring violations to warnings so the pip completes.
                config: pathTable => GetConfigurationForCacheConvergence(pathTable, fileAccessIgnoreCodeCoverage: true),
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("foobar.txt");
                    List<AbsolutePath> destinationAbsolutePath = new List<AbsolutePath>() { AbsolutePath.Create(env.Context.PathTable, destination) };

                    Process pip1 = CreateEchoProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, 123, OperatingSystemHelper.IsUnixOS ? "/usr/bin/jot -r 1 1 65000 > foobar.txt" : "%time% > foobar.txt");
                    var testRunChecker = new TestRunChecker(destination);

                    await VerifyPipResult(PipResultStatus.Succeeded, env, pip1, false);
                    XAssert.AreEqual(1, env.OutputFilesProduced, "produced count");
                    XAssert.AreEqual(0, env.OutputFilesUpToDate, "up to date count");
                    XAssert.AreEqual(0, env.OutputFilesDeployedFromCache, "deployed from cache count");

                    string actual = File.ReadAllText(destination);
                    localContents = actual;

                    // Rely on timing so that pip1 and pip2 achieve different results without changing the pip's fingerprint.
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    Process pip2 = CreateEchoProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, 124, OperatingSystemHelper.IsUnixOS ? "/usr/bin/jot -r 1 1 65000 > foobar.txt" : "%time% > foobar.txt");

                    // Ensure pip has cache miss so that it will run and subsequently do the convergence check
                    env.InjectedCacheMissPips.Add(pip2.PipId);
                    var cache = env.Cache;
                    var twoPhaseStore = (TestPipExecutorTwoPhaseFingerprintStore)cache.TwoPhaseFingerprintStore;
                    var priorPublishTries = twoPhaseStore.M_publishTries;

                    await VerifyPipResult(PipResultStatus.DeployedFromCache, env, pip2, false, checkNoProcessExecutionOnCacheHit: false, expectedExecutionLevel: PipExecutionLevel.Executed);

                    // Ensure a publish was attempted so we know there was a cache miss and the pip reran 
                    XAssert.AreEqual(priorPublishTries + 1, twoPhaseStore.M_publishTries);

                    XAssert.AreEqual(1, env.OutputFilesProduced, "produced count");
                    XAssert.AreEqual(0, env.OutputFilesUpToDate, "up to date count");
                    XAssert.AreEqual(1, env.OutputFilesDeployedFromCache, "deployed from cache count");

                    actual = File.ReadAllText(destination);
                    XAssert.AreEqual(localContents, actual);

                    // Make sure convergence isn't classified as a remote hit. It might be converging to a remote, but that
                    // would be making an assumption. This counter is used for logging and it is very confusing to users
                    // to give a false positive on a remote hit when there was no remote cache. It is better to error on
                    // the side of undercounting remote hits than overcounting them.
                    long remoteHits = env.Counters.GetCounterValue(PipExecutorCounter.RemoteCacheHitsForProcessPipDescriptorAndContent);
                    XAssert.AreEqual(0, remoteHits);
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessWithCacheMaintainsCase()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    var destinationFileNameInPathTable = destinationAbsolutePath.GetName(env.Context.PathTable).ToString(env.Context.StringTable);

                    string expectedCaseDestinationFileName = "DESt";

                    // Ensure that file name in path table does NOT match the actual file name to be created by the process
                    // This ensures that unless file name casing is appropriately stored and used in the cache
                    // the file name will not match after the deploy from cache step below.
                    XAssert.AreNotEqual(expectedCaseDestinationFileName, destinationFileNameInPathTable);

                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, destinationFileName: expectedCaseDestinationFileName);
                    var testRunChecker = new TestRunChecker(destination);

                    await testRunChecker.VerifySucceeded(env, pip, Contents);

                    await testRunChecker.VerifyUpToDate(env, pip, Contents);

                    // Ensure copy pip is creating file name with appropriate case
                    XAssert.AreEqual(expectedCaseDestinationFileName, FileUtilities.GetFileName(destination).Result);

                    File.Delete(destination);

                    await testRunChecker.VerifyDeployedFromCache(env, pip, Contents);

                    // Ensure process gets appropriate case when deployed from cache
                    XAssert.AreEqual(expectedCaseDestinationFileName, FileUtilities.GetFileName(destination).Result);
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessNotCachedWithWhitelistedFileMonitoringViolations()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),

                // We set monitoring violations to warnings so the pip completes.
                config: pathTable => GetConfiguration(pathTable, fileAccessIgnoreCodeCoverage: true, failUnexpectedFileAccesses: false),
                whitelistCreator: (context) =>
                {
                    var whitelist = new FileAccessWhitelist(context);

                    whitelist.Add(
                        new ExecutablePathWhitelistEntry(
                            AbsolutePath.Create(context.PathTable, CmdHelper.OsShellExe),
                            FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName("source"))),
                            allowsCaching: false,
                            name: "whitelist1"));
                    whitelist.Add(
                        new ExecutablePathWhitelistEntry(
                            AbsolutePath.Create(context.PathTable, CmdHelper.OsShellExe),
                            FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName("dest"))),
                            allowsCaching: true,
                            name: "whitelist2"));

                    return whitelist;
                },
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    Process pip = CreateCopyProcess(env.Context, sourceAbsolutePath, destinationAbsolutePath, omitDependencies: true);

                    for (int i = 1; i <= 2; i++)
                    {
                        var perf = (ProcessPipExecutionPerformance)await VerifyPipResult(PipResultStatus.Succeeded, env, pip, expectMarkedPerpertuallyDirty: true);
                        XAssert.AreEqual(i, env.OutputFilesProduced, "produced count");
                        XAssert.AreEqual(0, env.OutputFilesUpToDate, "up to date count");
                        XAssert.AreEqual(0, env.OutputFilesDeployedFromCache, "deployed from cache count");

                        // Expecting 3 events, of which 2 are collapsed into one due to similar file access type.
                        // Events ignore the function used for the access.
                        AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable, count: 2);
                        AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);

                        // Expecting 3 violations, of which 2 are collapsed into one due to similar file access type.
                        // Events ignore the function used for the access.
                        XAssert.AreEqual(2, perf.FileMonitoringViolations.NumFileAccessesWhitelistedButNotCacheable);

                        string actual = File.ReadAllText(destination);
                        XAssert.AreEqual(Contents, actual);
                    }
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessNotCachedWithWhitelistedFileMonitoringViolationsInSealedDirectory()
        {
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),

                // We set monitoring violations to warnings so the pip completes.
                config: pathTable => GetConfiguration(pathTable, fileAccessIgnoreCodeCoverage: true, failUnexpectedFileAccesses: false),
                whitelistCreator: (context) =>
                {
                    var whitelist = new FileAccessWhitelist(context);

                    whitelist.Add(
                        new ExecutablePathWhitelistEntry(
                            AbsolutePath.Create(context.PathTable, CmdHelper.OsShellExe),
                            FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName("a"))),
                            allowsCaching: false,
                            name: "whitelist"));

                    return whitelist;
                },
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    // We omit a and b from the seal; instead they should be handled by the whitelist.
                    DirectoryArtifact directoryArtifact = SealDirectoryWithProbeTargets(env, sourceAbsolutePath, omitContents: true);
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath);

                    string expected = CreateDirectoryWithProbeTargets(env.Context.PathTable, sourceAbsolutePath, fileAContents: "A", fileBContents: "B2");
                    var testRunChecker = new TestRunChecker(destination);

                    await testRunChecker.VerifySucceeded(env, pip, expected, expectMarkedPerpertuallyDirty: true);

                    // Expecting 4 events, of which 3 are collapsed into one due to similar file access type.
                    // Events ignore the function used for the access.
                    AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable, count: 2);

                    // The sealed-directory specific event is only emitted when there are non-whitelisted violations.
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 0);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 1);

                    await testRunChecker.VerifySucceeded(env, pip, expected, expectMarkedPerpertuallyDirty: true);

                    // Expecting 4 events, of which 3 are collapsed into one due to similar file access type.
                    // Events ignore the function used for the access.
                    AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable, count: 2);
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 0);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 1);
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessCachedWithWhitelistedFileMonitoringViolationsInSealedDirectory()
        {
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),

                // We set monitoring violations to warnings so the pip completes.
                config: pathTable => GetConfiguration(pathTable, fileAccessIgnoreCodeCoverage: true, failUnexpectedFileAccesses: false),
                whitelistCreator: (context) =>
                {
                    var whitelist = new FileAccessWhitelist(context);

                    whitelist.Add(
                        new ExecutablePathWhitelistEntry(
                            AbsolutePath.Create(context.PathTable, CmdHelper.OsShellExe),
                            FileAccessWhitelist.RegexWithProperties(Regex.Escape(Path.GetFileName("a"))),
                            allowsCaching: true,
                            name: "whitelist"));

                    return whitelist;
                },
                act: async env =>
                {
                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    // We omit a and b from the seal; instead 'a' should be handled by the whitelist and so 'b' should not be accessed.
                    // (logic is 'if a is absent, poke b')
                    DirectoryArtifact directoryArtifact = SealDirectoryWithProbeTargets(env, sourceAbsolutePath, omitContents: true);
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    string expected = CreateDirectoryWithProbeTargets(env.Context.PathTable, sourceAbsolutePath, fileAContents: "A", fileBContents: "B2");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    // Expecting 4 events, of which 3 are collapsed into one due to similar file access type.
                    // Events ignore the function used for the access.
                    AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedCacheable, count: 2);

                    // The sealed-directory specific event is only emitted when there are non-whitelisted violations.
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 0);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 0);

                    await testRunChecker.VerifyUpToDate(env, pip, expected);
                    AssertInformationalEventLogged(EventId.PipProcessDisallowedFileAccessWhitelistedCacheable, count: 0);
                    AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 0);
                    AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 0);
                });
        }

        [Fact(Skip = "Currently, mount tokenization is not supported")]
        public async Task CachedProcessAfterReroot()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            string cacheDir = GetFullPath(".cache");
            string originalDir = GetFullPath("original");
            string rerootDir = GetFullPath("reroot");

            Directory.CreateDirectory(originalDir);
            Directory.CreateDirectory(rerootDir);

            // ROOT => original
            await WithCachingExecutionEnvironment(
                cacheDir,
                async env =>
                {
                    string originalSource = GetFullPath(@"original\source");
                    AbsolutePath originalSourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, originalSource);

                    string originalDestination = GetFullPath(@"original\dest");
                    AbsolutePath originalDestinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, originalDestination);

                    File.WriteAllText(originalDestination, BadContents);
                    File.WriteAllText(originalSource, Contents);

                    Process originalPip = CreateCopyProcess(env.Context, originalSourceAbsolutePath, originalDestinationAbsolutePath, options: Process.Options.ProducesPathIndependentOutputs);

                    await VerifyPipResult(PipResultStatus.Succeeded, env, originalPip);
                    XAssert.AreEqual(1, env.OutputFilesProduced, "produced count");
                    XAssert.AreEqual(0, env.OutputFilesUpToDate, "up to date count");
                    XAssert.AreEqual(0, env.OutputFilesDeployedFromCache, "deployed from cache count");

                    string actual = File.ReadAllText(originalDestination);
                    XAssert.AreEqual(Contents, actual);
                },
                createMountExpander: pathTable =>
                {
                    var mounts = new MountPathExpander(pathTable);
                    mounts.Add(
                        pathTable,
                        new Mount()
                        {
                            Name = PathAtom.Create(pathTable.StringTable, "ROOT"),
                            Path = AbsolutePath.Create(pathTable, originalDir)
                        });
                    return mounts;
                });

            // ROOT => reroot (but the same content is present, so we should get a cache hit due to root trimming in fingerprinting and in the cache descriptor).
            await WithCachingExecutionEnvironment(
                cacheDir,
                async env =>
                {
                    string rerootedSource = GetFullPath(@"reroot\source");
                    AbsolutePath rerootedSourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, rerootedSource);

                    string rerootedDestination = GetFullPath(@"reroot\dest");
                    AbsolutePath rerootedDestinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, rerootedDestination);

                    Process rerootedPip = CreateCopyProcess(env.Context, rerootedSourceAbsolutePath, rerootedDestinationAbsolutePath, options: Process.Options.ProducesPathIndependentOutputs);
                    var testRunChecker = new TestRunChecker(rerootedDestination);

                    // Both the source and destination should be the same (see above after the pip runs); just the root has moved.
                    File.WriteAllText(rerootedDestination, Contents);
                    File.WriteAllText(rerootedSource, Contents);

                    // First let's ensure that UpToDate-ness is possible if somehow we already *knew* we had the right content.
                    // (without this call we'd pessimistically replace the destination rather than hashing it).
                    await env.FileContentTable.GetAndRecordContentHashAsync(rerootedDestination);

                    await testRunChecker.VerifyUpToDate(env, rerootedPip, Contents);

                    File.Delete(rerootedDestination);

                    await testRunChecker.VerifyDeployedFromCache(env, rerootedPip, Contents);
                },
                createMountExpander: pathTable =>
                {
                    var mounts = new MountPathExpander(pathTable);
                    mounts.Add(
                        pathTable,
                        new Mount()
                        {
                            Name = PathAtom.Create(pathTable.StringTable, "ROOT"),
                            Path = AbsolutePath.Create(pathTable, rerootDir)
                        });
                    return mounts;
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task ProcessWithDirectoryEnumeration_Bug1021066()
        {
            string sourceDir = GetFullPath("inc");
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    env.RecordExecution();
                    var mutableConfiguration = (ConfigurationImpl)env.Configuration;

                    // Bug #1021066 only reproduces if this flag is set
                    mutableConfiguration.Schedule.TreatDirectoryAsAbsentFileOnHashingInputContent = true;

                    var pathTable = env.Context.PathTable;
                    PathTable.DebugPathTable = pathTable;

                    AbsolutePath sourceDirAbsolutePath = AbsolutePath.Create(pathTable, sourceDir);

                    string destination = GetFullPath("out");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(pathTable, destination);

                    DirectoryArtifact directoryArtifact = SealSourceDirectoryWithProbeTargets(env, sourceDirAbsolutePath, true);
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath,
                        script: OperatingSystemHelper.IsUnixOS ? "/usr/bin/find . -type f -exec /bin/cat {} + > /dev/null" : "echo off & for /f %i IN ('dir *.* /b /s') do type %i");

                    var testRunChecker = new TestRunChecker(destination);
                    Dictionary<string, string> fs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // The following cases exercise runtime observation and usage thereof, such as VerifyInputAssertions.
                    // Note that the input directory is not explicitly sealed (no scheduler); the pip executor used here returns 
                    // 'sealed' hashes for files so long as they exist.

                    WriteFile(fs, pathTable, sourceDirAbsolutePath, file: OperatingSystemHelper.IsUnixOS ? "dir/A" : @"dir\A", contents: "A0");
                    await testRunChecker.VerifySucceeded(env, pip, GetExpected(fs));
                    VerifyExecutionObservedEnumerationsAndClear(env, pathTable, sourceDirAbsolutePath, ".", "dir");

                    // Add new file in nested invalidates cache
                    WriteFile(fs, pathTable, sourceDirAbsolutePath, file: OperatingSystemHelper.IsUnixOS ? "dir/B" : @"dir\B", contents: "B0");
                    await testRunChecker.VerifySucceeded(env, pip, GetExpected(fs));
                    VerifyExecutionObservedEnumerationsAndClear(env, pathTable, sourceDirAbsolutePath, ".", "dir");

                    // Rebuild without changes does not invalidate cache
                    await testRunChecker.VerifyUpToDate(env, pip, GetExpected(fs));

                    // Touching file does not invalidate cache
                    WriteFile(fs, pathTable, sourceDirAbsolutePath, file: @"dir\B", contents: "B0");
                    await testRunChecker.VerifyUpToDate(env, pip, GetExpected(fs));

                    // Change file invalidates cache
                    WriteFile(fs, pathTable, sourceDirAbsolutePath, file: OperatingSystemHelper.IsUnixOS ? "dir/B" : @"dir\B", contents: "B1");
                    await testRunChecker.VerifySucceeded(env, pip, GetExpected(fs));
                    VerifyExecutionObservedEnumerationsAndClear(env, pathTable, sourceDirAbsolutePath, ".", "dir");

                    // Add new file under root directory
                    WriteFile(fs, pathTable, sourceDirAbsolutePath, file: @"C", contents: "C0");
                    await testRunChecker.VerifySucceeded(env, pip, GetExpected(fs));
                    VerifyExecutionObservedEnumerationsAndClear(env, pathTable, sourceDirAbsolutePath, ".", "dir");

                    // Add new directory under root directory
                    WriteFile(fs, pathTable, sourceDirAbsolutePath, file: OperatingSystemHelper.IsUnixOS ? "dir2/B" : @"dir2\B", contents: "B0");
                    await testRunChecker.VerifySucceeded(env, pip, GetExpected(fs));
                    VerifyExecutionObservedEnumerationsAndClear(env, pathTable, sourceDirAbsolutePath, ".", "dir", "dir2");
                },
                createMountExpander: pathTable =>
                {
                    var mounts = new MountPathExpander(pathTable);
                    mounts.Add(
                        pathTable,
                        new Mount()
                        {
                            Name = PathAtom.Create(pathTable.StringTable, "ROOT"),
                            Path = AbsolutePath.Create(pathTable, sourceDir),
                            IsReadable = true,
                            TrackSourceFileChanges = true
                        });
                    return mounts;
                });
        }

        private void VerifyExecutionObservedEnumerationsAndClear(
            DummyPipExecutionEnvironment env,
            PathTable pathTable,
            AbsolutePath directory,
            params string[] relativeSubDirectories)
        {
            string expanded = directory.ToString(pathTable);

            HashSet<AbsolutePath> expectedEnumerations = new HashSet<AbsolutePath>(
                relativeSubDirectories.Select(sd => sd == "." ? directory : directory.Combine(pathTable, RelativePath.Create(pathTable.StringTable, sd))));

            var fingerprintData = env.ExecutionLogRecorder
                .GetEvents<ProcessFingerprintComputationEventData>()
                .Where(pf => pf.Kind == FingerprintComputationKind.Execution)
                .SingleOrDefault();

            foreach (var observedInput in fingerprintData.StrongFingerprintComputations[0].ObservedInputs)
            {
                if (observedInput.Type == ObservedInputType.DirectoryEnumeration)
                {
                    expectedEnumerations.Remove(observedInput.Path);
                }
            }

            Assert.Empty(expectedEnumerations);

            env.ExecutionLogRecorder.Clear();
        }

        private string GetExpected(Dictionary<string, string> fs)
        {
            // Replace path separator with char.MinValue to ensure proper sort order
            return string.Join(
                string.Empty, 
                fs.OrderBy(kvp => kvp.Key.Replace(Path.DirectorySeparatorChar, char.MinValue), StringComparer.OrdinalIgnoreCase)
                    .Select(kvp => kvp.Value)) + Environment.NewLine;
        }

        private void WriteFile(
            Dictionary<string, string> fs,
            PathTable pathTable,
            AbsolutePath directory,
            string file,
            string contents)
        {
            string expanded = Path.Combine(directory.ToString(pathTable), file);
            Directory.CreateDirectory(Path.GetDirectoryName(expanded));
            if (contents == null)
            {
                fs.Remove(file);
                File.Delete(expanded);
            }
            else
            {
                fs[file] = contents;
                File.WriteAllText(expanded, contents);
            }
        }

        [Fact]
        public async Task ProcessWithDirectoryInput()
        {
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    var pathTable = env.Context.PathTable;

                    string sourceDir = GetFullPath("inc");
                    AbsolutePath sourceDirAbsolutePath = AbsolutePath.Create(pathTable, sourceDir);

                    string destination = GetFullPath("out");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(pathTable, destination);

                    DirectoryArtifact directoryArtifact = SealDirectoryWithProbeTargets(env, sourceDirAbsolutePath);
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath);

                    var testRunChecker = new TestRunChecker(destination);

                    // The following cases exercise runtime observation and usage thereof, such as VerifyInputAssertions.
                    // Note that the input directory is not explicitly sealed (no scheduler); the pip executor used here returns 
                    // 'sealed' hashes for files so long as they exist.

                    string expected = CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A0", fileBContents: "B0");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    expected = CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A", fileBContents: "B");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    // Up-to-date since B wasn't read last time.
                    expected = CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A", fileBContents: "B2");
                    await testRunChecker.VerifyUpToDate(env, pip, expected);

                    // Runs since A was deleted.
                    expected = CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    // Runs since B was changed (to a value used before, but not observed)
                    expected = CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    // FixPoint when using B
                    expected = CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2");
                    await testRunChecker.VerifyUpToDate(env, pip, expected);

                    // Verify deployed from cache since it matches the first run
                    expected = CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A0", fileBContents: "B0");
                    await testRunChecker.VerifyDeployedFromCache(env, pip, expected);
                });
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public Task ProcessWithSourceDirectorTopDirectoryOnlyDirectoriesInput()
        {
            return ProcessWithSourceDirectoryHelper(false);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public Task ProcessWithSourceDirectoryAllDirectoriesInput()
        {
            return ProcessWithSourceDirectoryHelper(true);
        }

        public Task ProcessWithSourceDirectoryHelper(bool allDirectories)
        {
            return WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    var pathTable = env.Context.PathTable;

                    string sourceDir = GetFullPath("inc");
                    AbsolutePath sourceDirAbsolutePath = AbsolutePath.Create(pathTable, sourceDir);

                    string destination = GetFullPath("out");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(pathTable, destination);

                    var dummyContents = new List<AbsolutePath>
                                        {
                                            sourceDirAbsolutePath.Combine(pathTable, "a"),
                                            sourceDirAbsolutePath.Combine(pathTable, "b"),
                                            sourceDirAbsolutePath.Combine(pathTable, "echo"),
                                        };
                    if (allDirectories)
                    {
                        dummyContents.Add(sourceDirAbsolutePath.Combine(pathTable, "CFolder").Combine(pathTable, "c"));
                    }

                    DirectoryArtifact directoryArtifact = SealSourceDirectoryWithProbeTargets(env, sourceDirAbsolutePath, allDirectories, dummyContents.ToArray());

                    var script = allDirectories ?
                        OperatingSystemHelper.IsUnixOS ? 
                            "if [ -f a ]; then /bin/cat a; else /bin/cat b; fi; if [ -f CFolder/c ]; then /bin/cat CFolder/c; fi;" :
                            "( if exist a (type a) else (type b) ) & ( if exist CFolder\\c (type CFolder\\c) )" :
                        null;
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath, script);

                    var testRunChecker = new TestRunChecker(destination);
                    testRunChecker.ExpectedContentsSuffix = Environment.NewLine;

                    // The following cases exercise runtime observation and usage thereof, such as VerifyInputAssertions.
                    // Note that the input directory is not explicitly sealed (no scheduler); the pip executor used here returns 
                    // 'sealed' hashes for files so long as they exist.

                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A", fileBContents: "B", fileCContents: "C");
                    await testRunChecker.VerifySucceeded(env, pip, allDirectories ? "AC" : "A");

                    // Up-to-date since B wasn't read last time.
                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A", fileBContents: "B2", fileCContents: "C");
                    await testRunChecker.VerifyUpToDate(env, pip, allDirectories ? "AC" : "A");

                    // Runs since A was deleted.
                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B", fileCContents: "C");
                    await testRunChecker.VerifySucceeded(env, pip, allDirectories ? "BC" : "B");

                    // Runs since C was changed if we are testing all directories
                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B", fileCContents: "C2");
                    if (allDirectories)
                    {
                        await testRunChecker.VerifySucceeded(env, pip, "BC2");
                    }
                    else
                    {
                        await testRunChecker.VerifyUpToDate(env, pip, "B");
                    }

                    // Runs since B was changed (to a value used before, but not observed and C is back)
                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2", fileCContents: "C2");
                    await testRunChecker.VerifySucceeded(env, pip, allDirectories ? "B2C2" : "B2");

                    // Runs since C was changed when doing all directories
                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2", fileCContents: "C");
                    if (allDirectories)
                    {
                        await testRunChecker.VerifySucceeded(env, pip, "B2C");
                    }
                    else
                    {
                        await testRunChecker.VerifyUpToDate(env, pip, "B2");
                    }

                    // Fixpoint when using B
                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2", fileCContents: "C");
                    await testRunChecker.VerifyUpToDate(env, pip, allDirectories ? "B2C" : "B2");

                    // Rerun (yet fail) because echo has changed which is probed under the sealed directory
                    CreateDirectoryWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2", fileCContents: "C");
                    File.WriteAllText(sourceDirAbsolutePath.Combine(pathTable, "echo").ToString(pathTable), "FAIL ECHO INVOCATION");
                    await testRunChecker.VerifyFailed(env, pip, allDirectories ? "B2C" : "B2");
                    SetExpectedFailures(1, 0, "'echo.' is not recognized as an internal or external command");
                });
        }

        /// <summary>
        /// Tests that we can have a partially-sealed directory input for D\A and D\B while also writing D\C (write access to B should not be denied).
        /// </summary>
        [Fact]
        public async Task ProcessWithOutputInsideDirectoryInput()
        {
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    var pathTable = env.Context.PathTable;

                    string dir = GetFullPath("out");
                    AbsolutePath dirAbsolutePath = AbsolutePath.Create(pathTable, dir);
                    string destination = Path.Combine(dir, "c");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(pathTable, destination);

                    DirectoryArtifact directoryArtifact = SealDirectoryWithProbeTargets(env, dirAbsolutePath);
                    Process pip = CreateDirectoryProbingProcess(env.Context, directoryArtifact, destinationAbsolutePath);
                    var testRunChecker = new TestRunChecker(destination);

                    string expected = CreateDirectoryWithProbeTargets(env.Context.PathTable, dirAbsolutePath, fileAContents: "A", fileBContents: "B");
                    await testRunChecker.VerifySucceeded(env, pip, expected);
                });
        }

        [Fact]
        public async Task ProcessWithMultipleDirectoryInputs()
        {
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    var pathTable = env.Context.PathTable;

                    string sourceDir = GetFullPath("inc");
                    AbsolutePath sourceDirAbsolutePath = AbsolutePath.Create(pathTable, sourceDir);

                    string destination = GetFullPath("out");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(pathTable, destination);

                    Tuple<DirectoryArtifact, DirectoryArtifact> subdirectories = SealMultipleDirectoriesWithProbeTargets(env, sourceDirAbsolutePath);
                    Process pip = CreateMultiDirectoryProbingProcess(env.Context, sourceDirAbsolutePath, subdirectories, destinationAbsolutePath);

                    var testRunChecker = new TestRunChecker(destination);

                    // The following cases exercise runtime observation and usage thereof, such as VerifyInputAssertions.
                    // Note that the input directory is not explicitly sealed (no scheduler); the pip executor used here returns 
                    // 'sealed' hashes for files so long as they exist.

                    string expected = CreateMultipleDirectoriesWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A", fileBContents: "B");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    // Up-to-date since B wasn't read last time.
                    expected = CreateMultipleDirectoriesWithProbeTargets(pathTable, sourceDirAbsolutePath, fileAContents: "A", fileBContents: "B2");
                    await testRunChecker.VerifyUpToDate(env, pip, expected);

                    // Runs since A was deleted.
                    expected = CreateMultipleDirectoriesWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    // Runs since B was changed (to a value used before, but not observed)
                    expected = CreateMultipleDirectoriesWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2");
                    await testRunChecker.VerifySucceeded(env, pip, expected);

                    // FixPoint when using B
                    expected = CreateMultipleDirectoriesWithProbeTargets(pathTable, sourceDirAbsolutePath, fileBContents: "B2");
                    await testRunChecker.VerifyUpToDate(env, pip, expected);
                });
        }

        [Fact]
        public async Task ProcessCleansTempDirBeforeRunning()
        {
            await WithExecutionEnvironment(
                async env =>
                {
                    // Create a temp directory with a file in it from a previous run
                    string tempDir = GetFullPath("temp");
                    AbsolutePath tempPath = AbsolutePath.Create(env.Context.PathTable, tempDir);
                    Directory.CreateDirectory(tempDir);

                    string oldFile = Path.Combine(tempDir, "oldfile.txt");
                    AbsolutePath oldFilePath = AbsolutePath.Create(env.Context.PathTable, oldFile);
                    File.WriteAllText(oldFile, "asdf");

                    // Run a pip that checks whether the temp file exists or not
                    var pipDataBuilder = new PipDataBuilder(env.Context.StringTable);
                    if (OperatingSystemHelper.IsUnixOS)
                    {
                        pipDataBuilder.Add("-c");
                    }
                    else
                    {
                        pipDataBuilder.Add("/d");
                        pipDataBuilder.Add("/c");
                    }
                    
                    using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                    {
                        pipDataBuilder.Add(OperatingSystemHelper.IsUnixOS ? "if [ -f " + oldFile + " ]; then echo exists; else echo not exist; fi;" : "if exist \"" + oldFile + "\" (echo exists) else (echo not exist)");
                    }

                    // Redirect standard out to a file
                    string stdOut = GetFullPath("stdout.txt");
                    AbsolutePath stdOutPath = AbsolutePath.Create(env.Context.PathTable, stdOut);
                    var standardOut = FileArtifact.CreateSourceFile(stdOutPath).CreateNextWrittenVersion();

                    FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(env.Context.PathTable, CmdHelper.OsShellExe));

                    Process p =
                        AssignFakePipId(new Process(
                            executable: exe,
                            workingDirectory: AbsolutePath.Create(env.Context.PathTable, TemporaryDirectory),
                            arguments: pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                            responseFile: FileArtifact.Invalid,
                            responseFileData: PipData.Invalid,
                            environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                            standardInput: FileArtifact.Invalid,
                            standardOutput: standardOut,
                            standardError: FileArtifact.Invalid,
                            standardDirectory: FileArtifact.CreateSourceFile(stdOutPath.GetParent(env.Context.PathTable)).CreateNextWrittenVersion(),
                            warningTimeout: null,
                            timeout: null,
                            dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe),
                            outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(standardOut.WithAttributes()),
                            directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                            directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                            orderDependencies: ReadOnlyArray<PipId>.Empty,
                            untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(env.Context.PathTable)),
                            untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(env.Context.PathTable)),
                            tags: ReadOnlyArray<StringId>.Empty,
                            successExitCodes: ReadOnlyArray<int>.Empty,
                            semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                            options: Process.Options.ProducesPathIndependentOutputs,
                            provenance: PipProvenance.CreateDummy(env.Context),
                            toolDescription: StringId.Invalid,
                            additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                            tempDirectory: tempPath));

                    await VerifyPipResult(PipResultStatus.Succeeded, env, p);
                    XAssert.AreEqual(1, env.OutputFilesProduced, "produced count");

                    // Verify that the file did not exist by the time the pip ran
                    string actual = File.ReadAllText(stdOut).Trim();
                    XAssert.AreEqual("not exist", actual);
                });
        }

        [Fact]
        public async Task ProcessRetriesDueToExitCode()
        {
            const int RetryCount = 3;

            await WithExecutionEnvironment(
                config: pathTable => GetConfiguration(pathTable, retryCount: RetryCount),
                act: async env =>
                      {
                          var pathTable = env.Context.PathTable;
                          var stringTable = env.Context.StringTable;

                          var batchScriptPath = AbsolutePath.Create(pathTable, OperatingSystemHelper.IsUnixOS ? GetFullPath("test.sh"): GetFullPath("test.bat"));
                          File.WriteAllText(
                              batchScriptPath.ToString(pathTable),
                              OperatingSystemHelper.IsUnixOS ? "echo test;exit 3;printf '\n'" : @"
ECHO test
EXIT /b 3
");
                          if (OperatingSystemHelper.IsUnixOS)
                          {
                              chmod(batchScriptPath.ToString(pathTable), 0x1ff);
                          }
                          // Redirect standard out to a file
                          var stdOutFile = FileArtifact.CreateOutputFile(AbsolutePath.Create(pathTable, GetFullPath("stdout.txt")));

                          // Build arguments.
                          var pipDataBuilder = new PipDataBuilder(stringTable);
                          if (OperatingSystemHelper.IsUnixOS)
                          {
                              pipDataBuilder.Add("-c");
                          }
                          else
                          {
                            pipDataBuilder.Add("/d");
                            pipDataBuilder.Add("/c");
                          }
                          using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
                          {
                              pipDataBuilder.Add(batchScriptPath);
                          }

                          FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));

                          Process p =
                              AssignFakePipId(
                                  new Process(
                                      executable: exe,
                                      workingDirectory: AbsolutePath.Create(pathTable, TemporaryDirectory),
                                      arguments: pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                                      responseFile: FileArtifact.Invalid,
                                      responseFileData: PipData.Invalid,
                                      environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                                      standardInput: FileArtifact.Invalid,
                                      standardOutput: stdOutFile,
                                      standardError: FileArtifact.Invalid,
                                      standardDirectory: stdOutFile.Path.GetParent(pathTable),
                                      warningTimeout: null,
                                      timeout: null,
                                      dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe, FileArtifact.CreateSourceFile(batchScriptPath)),
                                      outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(stdOutFile.WithAttributes()),
                                      directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                                      directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                                      orderDependencies: ReadOnlyArray<PipId>.Empty,
                                      untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                                      untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                                      tags: ReadOnlyArray<StringId>.Empty,
                                      successExitCodes: ReadOnlyArray<int>.Empty,
                                      retryExitCodes: ReadOnlyArray<int>.FromWithoutCopy(3),
                                      semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                                      options: Process.Options.ProducesPathIndependentOutputs,
                                      provenance: PipProvenance.CreateDummy(env.Context),
                                      toolDescription: StringId.Invalid,
                                      additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty));

                          await VerifyPipResult(PipResultStatus.Failed, env, p);
                          AssertVerboseEventLogged(EventId.PipWillBeRetriedDueToExitCode, count: RetryCount);
                      });

            SetExpectedFailures(1, 0);
        }

        [Fact]
        public async Task PreserveOutputsWithDisallowedSandbox()
        {
            const string PriorOutput = "PriorOutputContent";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    XAssert.AreNotEqual(await RunPreviousOutputsTest(env, PriorOutput, allowPreviousOutputs: true), PriorOutput);
                },
                null,
                pathTable => GetConfiguration(pathTable, enableLazyOutputs: false,
                preserveOutputs: PreserveOutputsMode.Disabled));
        }

        [Fact]
        public async Task PreserveOutputsWithAllowedSandboxButDisallowedPip()
        {
            const string PriorOutput = "PriorOutputContent";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    XAssert.AreNotEqual(await RunPreviousOutputsTest(env, PriorOutput, allowPreviousOutputs: false), PriorOutput);
                },
                null,
                pathTable => GetConfiguration(pathTable, enableLazyOutputs: false,
                preserveOutputs: PreserveOutputsMode.Enabled));
        }

        [Fact]
        public async Task PreserveOutputsAllowed()
        {
            const string PriorOutput = "PriorOutputContent";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    XAssert.AreEqual(await RunPreviousOutputsTest(env, PriorOutput, allowPreviousOutputs: true), PriorOutput);
                },
                null,
                pathTable => GetConfiguration(pathTable, enableLazyOutputs: false, preserveOutputs: PreserveOutputsMode.Enabled));
        }

        private async Task<string> RunPreviousOutputsTest(DummyPipExecutionEnvironment env, string content, bool allowPreviousOutputs)
        {
            string workingDirectory = GetFullPath("work");
            AbsolutePath workingDirectoryAbsolutePath = AbsolutePath.Create(env.Context.PathTable, workingDirectory);

            string out1 = GetFullPath("out1");
            AbsolutePath out1AbsolutePath = AbsolutePath.Create(env.Context.PathTable, out1);
            File.WriteAllText(out1, content);

            string out2 = GetFullPath("out2");
            AbsolutePath out2AbsolutePath = AbsolutePath.Create(env.Context.PathTable, out2);

            Process pip = CreatePreviousOutputCheckerProcess(env.Context, workingDirectoryAbsolutePath, out1AbsolutePath, out2AbsolutePath, allowPreviousOutputs);

            await VerifyPipResult(PipResultStatus.Succeeded, env, pip);
            return File.ReadAllText(out2);
        }

        /// <summary>
        /// This test uses inline data to be independent of the order in which the cache enumerate the publish entries.
        /// </summary>
        /// <remarks>
        /// In this test we publish Hello-0, Hello-1 entries to the cache. When we enumerate the publish entry refs from the cache, 
        /// We don't want to depend on the order in which Hello-0 and Hello-1 come. If Hello-0 comes later, then reverting 
        /// to Hello-0 will fail. But if Hello-1 comes later, then reverting to Hello-1 will fail. Thus, we use inline data
        /// to test both reverting to Hello-0 and Hello-1.
        /// </remarks>
        [Theory]
        [InlineData("Hello-0")]
        [InlineData("Hello-1")]
        public async Task TestCacheHitPipResultShouldContainDynamicallyObservedInputs(string revert)
        {
            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    string inputPath = GetFullPath("input");
                    FileArtifact dependency = FileArtifact.CreateSourceFile(AbsolutePath.Create(env.Context.PathTable, inputPath));
                    File.WriteAllText(inputPath, "Hello");
                    string outputPath = GetFullPath("out");
                    FileArtifact output = FileArtifact.CreateOutputFile(AbsolutePath.Create(env.Context.PathTable, outputPath));
                    string directoryPath = GetFullPath("dir");
                    DirectoryArtifact directoryDependency = DirectoryArtifact.CreateWithZeroPartialSealId(AbsolutePath.Create(env.Context.PathTable, directoryPath));
                    Directory.CreateDirectory(directoryPath);
                    AbsolutePath directoryMemberPath = directoryDependency.Path.Combine(env.Context.PathTable, "dir-input");
                    string expandedDirectoryMemberPath = directoryMemberPath.ToString(env.Context.PathTable);
                    File.WriteAllText(expandedDirectoryMemberPath, "Hello-0");
                    env.SetSealedDirectoryContents(directoryDependency, FileArtifact.CreateSourceFile(directoryMemberPath));
                    AbsolutePath standardDirectory = output.Path.GetParent(env.Context.PathTable);
                    AbsolutePath workingDirectory = directoryDependency.Path;
                    string script = OperatingSystemHelper.IsUnixOS ? I($"/bin/cat {expandedDirectoryMemberPath} > {outputPath}") : I($"type {expandedDirectoryMemberPath} > {outputPath}");
                    Process process = CreateCmdProcess(
                        env.Context,
                        script,
                        workingDirectory,
                        standardDirectory,
                        outputs: new[] { output.WithAttributes() },
                        directoryDependencies: new[] { directoryDependency });

                    // Run pip with "Hello-0" and then with "Hello-1" for the observed input.
                    // Thus, we will have two published entries in the cache with the same weak FP and path set.
                    await VerifyPipResult(
                        PipResultStatus.Succeeded,
                        env,
                        process,
                        customVerify:
                            pipResult =>
                            {
                                XAssert.IsTrue(pipResult.DynamicallyObservedFiles.Contains(directoryMemberPath));
                            });

                    File.WriteAllText(expandedDirectoryMemberPath, "Hello-1");

                    await VerifyPipResult(
                        PipResultStatus.Succeeded, 
                        env, 
                        process,
                        customVerify:
                            pipResult =>
                            {
                                XAssert.IsTrue(pipResult.DynamicallyObservedFiles.Contains(directoryMemberPath));
                            });

                    // Modify static input and the run pip with random GUID value for observed input.
                    File.WriteAllText(inputPath, "World");
                    File.WriteAllText(expandedDirectoryMemberPath, Guid.NewGuid().ToString());
                    await VerifyPipResult(PipResultStatus.Succeeded, env, process);

                    // Revert static input to its original value.
                    // Revert observed input to either "Hello-0" or "Hello-1" depending on the test.
                    File.WriteAllText(inputPath, "Hello");
                    File.WriteAllText(expandedDirectoryMemberPath, revert);

                    await VerifyPipResult(
                        PipResultStatus.DeployedFromCache, 
                        env, 
                        process,
                        customVerify:
                            pipResult =>
                            {
                                // BUG1158938 causes it to fail.
                                XAssert.IsTrue(pipResult.DynamicallyObservedFiles.Contains(directoryMemberPath));
                            });
                });
        }

        private static async Task<PipExecutionPerformance> VerifyPipResult(
            PipResultStatus expected,
            DummyPipExecutionEnvironment env,
            Pip pip,
            bool verifyFingerprint = true,
            bool checkNoProcessExecutionOnCacheHit = true,
            bool expectMarkedPerpertuallyDirty = false,
            PipExecutionLevel? expectedExecutionLevel = null,
            Action<PipResult> customVerify = null)
        {
            // TODO: Add code below to validate the two way fingerprint.
            XAssert.IsTrue(expected.IndicatesExecution(), "Expected result shouldn't be returned by PipExecutor");

            ContentFingerprint calculatedFingerprint = ContentFingerprint.Zero;
            bool checkFingerprint = pip is Process && verifyFingerprint;

            // Reset the file content manager to ensure pip runs with clean materialization state
            env.ResetFileContentManager();
            var operationTracker = new OperationTracker(env.LoggingContext);

            PipResult result;
            using (var operationContext = operationTracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, pip.PipId, pip.PipType, env.LoggingContext))
            {
                result = await TestPipExecutor.ExecuteAsync(operationContext, env, pip);
            }

            XAssert.AreEqual(expected, result.Status, "PipResult's status didn't match");
            XAssert.AreEqual(
                expectMarkedPerpertuallyDirty,
                result.MustBeConsideredPerpetuallyDirty,
                "PipResult's MustBeConsideredPerpetuallyDirty didn't match");

            if (checkFingerprint)
            {
                // Need to call this after running pip executor to ensure inputs are hashed
                calculatedFingerprint = env.ContentFingerprinter.ComputeWeakFingerprint((Process)pip);

                ProcessPipExecutionPerformance processPerf = (ProcessPipExecutionPerformance)result.PerformanceInfo;
                XAssert.AreEqual(calculatedFingerprint.Hash, processPerf.Fingerprint, "Calculated fingerprint and returned value not equal.");
            }

            if (pip is Process)
            {
                XAssert.IsNotNull(result.PerformanceInfo, "Performance info should have been reported");
                XAssert.AreEqual(expectedExecutionLevel ?? expected.ToExecutionLevel(), result.PerformanceInfo.ExecutionLevel);
                Assert.IsType(typeof(ProcessPipExecutionPerformance), result.PerformanceInfo);

                if (checkNoProcessExecutionOnCacheHit)
                {
                    if (result.PerformanceInfo.ExecutionLevel == PipExecutionLevel.Cached ||
                        result.PerformanceInfo.ExecutionLevel == PipExecutionLevel.UpToDate)
                    {
                        XAssert.AreEqual(TimeSpan.Zero, ((ProcessPipExecutionPerformance)result.PerformanceInfo).ProcessExecutionTime);
                    }
                }
            }

            customVerify?.Invoke(result);

            return result.PerformanceInfo;
        }

        private static string CreateInMemoryJsonConfigString(string cacheId)
        {
            const string DefaultInMemoryJsonConfigString = @"{{ 
                ""Assembly"":""BuildXL.Cache.InMemory"",
                ""Type"": ""BuildXL.Cache.InMemory.MemCacheFactory"", 
                ""CacheId"":""{0}"",
                ""StrictMetadataCasCoupling"":false
            }}";

            return string.Format(DefaultInMemoryJsonConfigString, cacheId);
        }

        private static Task WithCachingExecutionEnvironmentForCacheConvergence(
            string cacheDir,
            Func<DummyPipExecutionEnvironment, Task> act,
            Func<PathTable, SemanticPathExpander> createMountExpander = null,
            Func<PathTable, IConfiguration> config = null,
            Func<PathTable, StringTable, FileAccessWhitelist> whitelistCreator = null)
        {
            return WithExecutionEnvironmentForCacheConvergence(act, createMountExpander, config: config, whitelistCreator: whitelistCreator);
        }

        private static Task WithCachingExecutionEnvironment(
            string cacheDir,
            Func<DummyPipExecutionEnvironment, Task> act,
            Func<PathTable, SemanticPathExpander> createMountExpander = null,
            Func<PathTable, IConfiguration> config = null,
            Func<PipExecutionContext, FileAccessWhitelist> whitelistCreator = null,
            bool useInMemoryCache = true)
        {
            return WithExecutionEnvironment(act, InMemoryCacheFactory.Create, createMountExpander, config: config, whitelistCreator: whitelistCreator);
        }

        private static Task WithExecutionEnvironmentForCacheConvergence(
            Func<DummyPipExecutionEnvironment, Task> act,
            Func<PathTable, SemanticPathExpander> createMountExpander = null,
            Func<PathTable, IConfiguration> config = null,
            Func<PathTable, StringTable, FileAccessWhitelist> whitelistCreator = null)
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            FileAccessWhitelist fileAccessWhitelist = whitelistCreator?.Invoke(context.PathTable, context.StringTable);

            return act(
                    CreateExecutionEnvironmentForCacheConvergence(
                        context,
                        mountExpander: createMountExpander == null ? SemanticPathExpander.Default : createMountExpander(context.PathTable),
                        config: config,
                        fileAccessWhitelist: fileAccessWhitelist));
        }

        private static Task WithExecutionEnvironmentAndIpcServer(
            IIpcProvider ipcProvider,
            IIpcOperationExecutor ipcExecutor,
            Func<DummyPipExecutionEnvironment, IIpcMoniker, IServer, Task> act,
            Func<EngineCache> cache = null,
            Func<PathTable, SemanticPathExpander> createMountExpander = null,
            Func<PathTable, IConfiguration> config = null,
            Func<PipExecutionContext, FileAccessWhitelist> whitelistCreator = null,
            bool useInMemoryCache = true)
        {
            return WithExecutionEnvironment(
                (env) =>
                {
                    return WithIpcServer(
                        ipcProvider,
                        ipcExecutor,
                        new ServerConfig(),
                        (moniker, server) => act(env, moniker, server));
                },
                cache,
                createMountExpander,
                config: config,
                whitelistCreator: whitelistCreator,
                ipcProvider: ipcProvider);
        }

        private static Task WithExecutionEnvironment(
            Func<DummyPipExecutionEnvironment, Task> act,
            Func<EngineCache> cache = null,
            Func<PathTable, SemanticPathExpander> createMountExpander = null,
            Func<PathTable, IConfiguration> config = null,
            Func<PipExecutionContext, FileAccessWhitelist> whitelistCreator = null,
            IIpcProvider ipcProvider = null)
        {
            var context = BuildXLContext.CreateInstanceForTesting();

            FileAccessWhitelist fileAccessWhitelist = whitelistCreator?.Invoke(context);

            return act(
                    CreateExecutionEnvironment(
                        context,
                        cache,
                        createMountExpander == null ? SemanticPathExpander.Default : createMountExpander(context.PathTable),
                        config: config,
                        fileAccessWhitelist: fileAccessWhitelist,
                        ipcProvider: ipcProvider));
        }

        private static IConfiguration GetConfiguration(
            PathTable pathTable,
            bool fileAccessIgnoreCodeCoverage = true,
            bool enableLazyOutputs = true,
            bool failUnexpectedFileAccesses = true,
            bool unexpectedFileAccessesAreErrors = true,
            bool allowCopySymlink = true,
            PreserveOutputsMode preserveOutputs = PreserveOutputsMode.Disabled,
            int? retryCount = null,
            bool monitorFileAccesses = true,
            OutputReportingMode outputReportingMode = OutputReportingMode.TruncatedOutputOnError,
            bool storeOutputsToCache = true)
        {
            var config = ConfigurationHelpers.GetDefaultForTesting(pathTable, AbsolutePath.Create(pathTable, TestPath));
            config.Sandbox.FileAccessIgnoreCodeCoverage = fileAccessIgnoreCodeCoverage;
            config.Schedule.EnableLazyOutputMaterialization = enableLazyOutputs;
            config.Sandbox.FailUnexpectedFileAccesses = failUnexpectedFileAccesses;
            config.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = unexpectedFileAccessesAreErrors;
            config.Sandbox.OutputReportingMode = outputReportingMode;
            config.Schedule.AllowCopySymlink = allowCopySymlink;
            config.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = preserveOutputs;
            config.Sandbox.UnsafeSandboxConfigurationMutable.MonitorFileAccesses = monitorFileAccesses;
            config.Schedule.StoreOutputsToCache = storeOutputsToCache;

            if (retryCount.HasValue)
            {
                config.Schedule.ProcessRetries = retryCount.Value;
            }

            return config;
        }

        private static IConfiguration GetConfigurationForCacheConvergence(
            PathTable pathTable,
            bool fileAccessIgnoreCodeCoverage = true,
            bool enableLazyOutputs = true,
            bool failUnexpectedFileAccesses = true,
            bool unexpectedFileAccessesAreErrors = true,
            bool enableDeterminismProbe = false)
        {
            var config = ConfigurationHelpers.GetDefaultForTesting(pathTable, AbsolutePath.Create(pathTable, TestPath));
            config.Sandbox.FileAccessIgnoreCodeCoverage = fileAccessIgnoreCodeCoverage;
            config.Schedule.EnableLazyOutputMaterialization = enableLazyOutputs;
            config.Sandbox.FailUnexpectedFileAccesses = failUnexpectedFileAccesses;
            config.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = unexpectedFileAccessesAreErrors;
            config.Cache.DeterminismProbe = enableDeterminismProbe;
            return config;
        }

        private static DummyPipExecutionEnvironment CreateExecutionEnvironmentForCacheConvergence(
            BuildXLContext context,
            SemanticPathExpander mountExpander = null,
            Func<PathTable, IConfiguration> config = null,
            FileAccessWhitelist fileAccessWhitelist = null)
        {
            // TestPipExecutorArtifactContentCache0
            IConfiguration configInstance = config == null ? GetConfigurationForCacheConvergence(context.PathTable) : config(context.PathTable);

            EngineCache cacheLayer = new EngineCache(
                    new InMemoryArtifactContentCache(),
                    new TestPipExecutorTwoPhaseFingerprintStore());

            var env = new DummyPipExecutionEnvironment(
                CreateLoggingContextForTest(),
                context,
                configInstance,
                pipCache: cacheLayer,
                semanticPathExpander: mountExpander,
                fileAccessWhitelist: fileAccessWhitelist,
                allowUnspecifiedSealedDirectories: false,
                sandboxedKextConnection: GetSandboxedKextConnection());
            env.ContentFingerprinter.FingerprintTextEnabled = true;
            return env;
        }

        private static DummyPipExecutionEnvironment CreateExecutionEnvironment(
            BuildXLContext context,
            Func<EngineCache> cache = null,
            SemanticPathExpander mountExpander = null,
            Func<PathTable, IConfiguration> config = null,
            FileAccessWhitelist fileAccessWhitelist = null,
            IIpcProvider ipcProvider = null)
        {
            IConfiguration configInstance = config == null ? GetConfiguration(context.PathTable) : config(context.PathTable);

            EngineCache cacheLayer = cache != null
                ? cache()
                : new EngineCache(
                    new InMemoryArtifactContentCache(),

                    // Note that we have an 'empty' store (no hits ever) rather than a normal in memory one.
                    new EmptyTwoPhaseFingerprintStore());

            var env = new DummyPipExecutionEnvironment(
                CreateLoggingContextForTest(),
                context,
                configInstance,
                pipCache: cacheLayer,
                semanticPathExpander: mountExpander,
                fileAccessWhitelist: fileAccessWhitelist,
                allowUnspecifiedSealedDirectories: false,
                ipcProvider: ipcProvider,
                sandboxedKextConnection: GetSandboxedKextConnection());
            env.ContentFingerprinter.FingerprintTextEnabled = true;

            // A bit of a hack for tests that validate things that get logged to the Static context
            Events.StaticContext = env.LoggingContext;
            return env;
        }

        private static Process CreateCopyProcess(
            PipExecutionContext context, 
            AbsolutePath source, 
            AbsolutePath destination, 
            bool omitDependencies = false,
            Process.Options options = default, 
            AbsolutePath? temporaryOutput = null, 
            ServiceInfo serviceInfo = default, 
            string destinationFileName = null)
        {
            var pathTable = context.PathTable;
            var pipDataBuilder = new PipDataBuilder(context.StringTable);
            if (OperatingSystemHelper.IsUnixOS)
            {
                pipDataBuilder.Add("-c \"");
            }
            else
            {
                pipDataBuilder.Add("/d");
                pipDataBuilder.Add("/c");
            }

            using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.CRuntimeArgumentRules, " "))
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    pipDataBuilder.Add("/bin/cp");
                    pipDataBuilder.Add(source);
                }
                else
                {
                    pipDataBuilder.Add("type");
                    pipDataBuilder.Add(source);
                    pipDataBuilder.Add(">");
                }

                if (!string.IsNullOrEmpty(destinationFileName))
                {
                    pipDataBuilder.Add(destinationFileName);
                }
                else
                {
                    pipDataBuilder.Add(destination);
                }
            }

            if (OperatingSystemHelper.IsUnixOS)
            {
                using (pipDataBuilder.StartFragment(PipDataFragmentEscaping.NoEscaping, " "))
                {
                    pipDataBuilder.Add("\"");
                }
            }

            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));

            var outputs = new List<FileArtifactWithAttributes>()
            {
                FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(destination), FileExistence.Required).CreateNextWrittenVersion()
            };

            if (temporaryOutput != null)
            {
                outputs.Add(FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(temporaryOutput.Value), FileExistence.Temporary).CreateNextWrittenVersion());
            }

            return AssignFakePipId(new Process(
                executable: exe,
                workingDirectory: source.GetParent(pathTable),
                arguments: pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: FileArtifact.CreateSourceFile(destination.GetParent(pathTable)).CreateNextWrittenVersion(),
                warningTimeout: null,
                timeout: null,
                dependencies: omitDependencies ? ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe) : ReadOnlyArray<FileArtifact>.FromWithoutCopy(FileArtifact.CreateSourceFile(source), exe),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.From(outputs),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                options: options,
                serviceInfo: serviceInfo,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty));
        }

        /// <summary>
        /// Seals a directory in the execution environment representing the files to be created by <see cref="CreateDirectoryWithProbeTargets"/>.
        /// This may be called only once for each environment and root since it reserves the same directory artifact and paths each time.
        /// </summary>
        private static DirectoryArtifact SealDirectoryWithProbeTargets(DummyPipExecutionEnvironment env, AbsolutePath root, bool omitContents = false)
        {
            var directoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(root);

            if (!omitContents)
            {
                AbsolutePath fileA = root.Combine(env.Context.PathTable, PathAtom.Create(env.Context.StringTable, "a"));
                AbsolutePath fileB = root.Combine(env.Context.PathTable, PathAtom.Create(env.Context.StringTable, "b"));
                env.SetSealedDirectoryContents(directoryArtifact, FileArtifact.CreateSourceFile(fileA), FileArtifact.CreateSourceFile(fileB));
            }
            else
            {
                env.SetSealedDirectoryContents(directoryArtifact);
            }

            return directoryArtifact;
        }

        /// <summary>
        /// Seals a directory in the execution environment representing the files to be created by <see cref="CreateDirectoryWithProbeTargets"/>.
        /// This may be called only once for each environment and root since it reserves the same directory artifact and paths each time.
        /// </summary>
        private static DirectoryArtifact SealSourceDirectoryWithProbeTargets(DummyPipExecutionEnvironment env, AbsolutePath root, bool allDirectories, params AbsolutePath[] dynamicallyAccessedPathInDirectory)
        {
            var directoryArtifact = SealDirectoryWithProbeTargets(env, root, omitContents: true);
            env.SetSealedSourceDirectory(directoryArtifact, allDirectories, dynamicallyAccessedPathInDirectory);

            return directoryArtifact;
        }

        /// <summary>
        /// Creates an input directory for <see cref="CreateDirectoryProbingProcess" />. If null is specified for file contents,
        /// that file is ensured to not exist.
        /// This may be called multiple times in one test. Returns the expected contents of the output file.
        /// </summary>
        private static string CreateDirectoryWithProbeTargets(
            PathTable pathTable,
            AbsolutePath directory,
            string fileAContents = null,
            string fileBContents = null,
            string fileCContents = null)
        {
            string expanded = directory.ToString(pathTable);
            Directory.CreateDirectory(expanded);

            string fileA = Path.Combine(expanded, "a");
            string fileB = Path.Combine(expanded, "b");
            string folderC = Path.Combine(expanded, "CFolder");
            string fileC = Path.Combine(folderC, "c");

            if (fileAContents == null)
            {
                File.Delete(fileA);
            }
            else
            {
                File.WriteAllText(fileA, fileAContents);
            }

            if (fileBContents == null)
            {
                File.Delete(fileB);
            }
            else
            {
                File.WriteAllText(fileB, fileBContents);
            }

            if (fileCContents == null)
            {
                if (Directory.Exists(folderC))
                {
                    File.Delete(fileC);
                    Directory.Delete(folderC);
                }
            }
            else
            {
                Directory.CreateDirectory(folderC);
                File.WriteAllText(fileC, fileCContents);
            }

            return (fileAContents ?? fileBContents ?? string.Empty) + fileCContents + Environment.NewLine;
        }

        /// <summary>
        /// Seals a directory in the execution environment representing the files to be created by <see cref="CreateMultipleDirectoriesWithProbeTargets"/>.
        /// This may be called only once for each environment and root since it reserves the same directory artifact and paths each time.
        /// </summary>
        private static Tuple<DirectoryArtifact, DirectoryArtifact> SealMultipleDirectoriesWithProbeTargets(DummyPipExecutionEnvironment env, AbsolutePath root)
        {
            AbsolutePath rootA = root.Combine(env.Context.PathTable, PathAtom.Create(env.Context.StringTable, "a"));
            AbsolutePath fileA = rootA.Combine(env.Context.PathTable, PathAtom.Create(env.Context.StringTable, "a"));
            var directoryArtifactA = DirectoryArtifact.CreateWithZeroPartialSealId(rootA);

            AbsolutePath rootB = root.Combine(env.Context.PathTable, PathAtom.Create(env.Context.StringTable, "b"));
            AbsolutePath fileB = rootB.Combine(env.Context.PathTable, PathAtom.Create(env.Context.StringTable, "b"));
            var directoryArtifactB = DirectoryArtifact.CreateWithZeroPartialSealId(rootB);

            env.SetSealedDirectoryContents(directoryArtifactA, FileArtifact.CreateSourceFile(fileA));
            env.SetSealedDirectoryContents(directoryArtifactB, FileArtifact.CreateSourceFile(fileB));

            return Tuple.Create(directoryArtifactA, directoryArtifactB);
        }

        /// <summary>
        /// Creates input directories for <see cref="CreateMultiDirectoryProbingProcess" />. If null is specified for file
        /// contents, that file is ensured to not exist.
        /// This may be called multiple times in one test. Returns the expected contents of the output file.
        /// </summary>
        private static string CreateMultipleDirectoriesWithProbeTargets(
            PathTable pathTable,
            AbsolutePath directory,
            string fileAContents = null,
            string fileBContents = null)
        {
            string expanded = directory.ToString(pathTable);
            string fileA = Path.Combine(expanded, OperatingSystemHelper.IsUnixOS ? "a/a" : @"a\a");
            string fileB = Path.Combine(expanded, OperatingSystemHelper.IsUnixOS ? "b/b" : @"b\b");

            Directory.CreateDirectory(Path.GetDirectoryName(fileA));
            Directory.CreateDirectory(Path.GetDirectoryName(fileB));

            if (fileAContents == null)
            {
                File.Delete(fileA);
            }
            else
            {
                File.WriteAllText(fileA, fileAContents);
            }

            if (fileBContents == null)
            {
                File.Delete(fileB);
            }
            else
            {
                File.WriteAllText(fileB, fileBContents);
            }

            return (fileAContents ?? fileBContents ?? string.Empty) + Environment.NewLine;
        }

        /// <summary>
        /// The returned process will probe for 'a' in the given directory, and if not found will then probe for 'b'.
        /// </summary>
        private static Process CreateDirectoryProbingProcess(PipExecutionContext context, DirectoryArtifact directory, AbsolutePath output, string script = null)
        {
            if (script == null)
            {
                script = OperatingSystemHelper.IsUnixOS ? "if [ -f a ]; then /bin/cat a; else /bin/cat b; fi;" : "if exist a (type a) else (type b)";
            }

            var pathTable = context.PathTable;

            FileArtifact stdout = FileArtifact.CreateSourceFile(output).CreateNextWrittenVersion();
            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));
            return AssignFakePipId(new Process(
                executable: exe,
                workingDirectory: directory.Path,
                arguments: PipDataBuilder.CreatePipData(
                    pathTable.StringTable,
                    " ",
                    PipDataFragmentEscaping.NoEscaping,
                    OperatingSystemHelper.IsUnixOS ? "-c \" " + script + " \"" : "/d /c (" + script + ") & echo."
                    ),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: stdout,
                standardError: FileArtifact.Invalid,
                standardDirectory: output.GetParent(pathTable),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(stdout.WithAttributes()),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(directory),
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty));
        }

        /// <summary>
        /// The returned process will probe for 'a\a' in the given directory, and if not found will then probe for 'b\b'.
        /// The dependency is represented as a directory artifact.
        /// </summary>
        private static Process CreateMultiDirectoryProbingProcess(PipExecutionContext context, AbsolutePath directoryRoot, Tuple<DirectoryArtifact, DirectoryArtifact> subdirectories, AbsolutePath output)
        {
            var pathTable = context.PathTable;
            FileArtifact stdout = FileArtifact.CreateSourceFile(output).CreateNextWrittenVersion();
            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));

            return AssignFakePipId(new Process(
                executable: exe,
                workingDirectory: directoryRoot,
                arguments: PipDataBuilder.CreatePipData(
                            context.StringTable,
                            " ",
                            PipDataFragmentEscaping.NoEscaping,
                            OperatingSystemHelper.IsUnixOS ? "-c \"if [ -f a/a ]; then /bin/cat a/a; else /bin/cat b/b; fi;\"" : @"/d /c (if exist a\a (type a\a) else (type b\b)) & echo."
                    ),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: stdout,
                standardError: FileArtifact.Invalid,
                standardDirectory: output.GetParent(pathTable),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(stdout.WithAttributes()),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(subdirectories.Item1, subdirectories.Item2),
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty));
        }

        /// <summary>
        /// The returned process will probe and repeat the content of outputToCheck into resultPath. It will then overwrite outputToCheck.
        /// </summary>
        private static Process CreatePreviousOutputCheckerProcess(PipExecutionContext context, AbsolutePath directoryRoot, AbsolutePath outputToCheck, AbsolutePath resultPath, bool allowPreserveOutputs)
        {
            var pathTable = context.PathTable;
            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));

            return AssignFakePipId(new Process(
                executable: exe,
                workingDirectory: directoryRoot,
                arguments: PipDataBuilder.CreatePipData(
                            context.StringTable,
                            " ",
                            PipDataFragmentEscaping.NoEscaping,
                            string.Format(OperatingSystemHelper.IsUnixOS ? "-c \"/bin/cat {0} > {1}; echo 'hi' > {0}\"" : "/d /c type {0} 1> {1} 2>&1 & echo 'hi' > {0}",
                            outputToCheck.ToString(context.PathTable),
                            resultPath.ToString(context.PathTable))
                    ),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: resultPath.GetParent(pathTable),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(new FileArtifact[] { exe }),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(
                    new FileArtifact(outputToCheck).CreateNextWrittenVersion().WithAttributes(),
                    new FileArtifact(resultPath).CreateNextWrittenVersion().WithAttributes()),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                options: allowPreserveOutputs ? Process.Options.AllowPreserveOutputs : Process.Options.None));
        }

        private static Process CreateCmdProcess(
            PipExecutionContext context,
            string script,
            AbsolutePath workingDirectory,
            AbsolutePath standardDirectory,
            IReadOnlyList<EnvironmentVariable> environmentVariables = null,
            IReadOnlyList<FileArtifact> dependencies = null,
            IReadOnlyList<FileArtifactWithAttributes> outputs = null,
            IReadOnlyList<DirectoryArtifact> directoryDependencies = null,
            IReadOnlyList<DirectoryArtifact> directoryOutputs = null,
            IReadOnlyList<AbsolutePath> untrackedPaths = null,
            IReadOnlyList<AbsolutePath> untrackedScopes = null,
            IReadOnlyList<int> successExitCodes = null,
            Process.Options options = Process.Options.None)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(script));
            
            var pathTable = context.PathTable;

            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));
            return AssignFakePipId(new Process(
                executable: exe,
                workingDirectory: workingDirectory,
                arguments: PipDataBuilder.CreatePipData(
                    pathTable.StringTable,
                    " ",
                    PipDataFragmentEscaping.NoEscaping,
                    OperatingSystemHelper.IsUnixOS ? I($"-c \"{script}\"") : "/d /c " + script
                    ),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: FromReadOnlyList(environmentVariables),
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: standardDirectory,
                warningTimeout: null,
                timeout: null,
                dependencies: FromReadOnlyList(dependencies, exe),
                outputs: FromReadOnlyList(outputs),
                directoryDependencies: FromReadOnlyList(directoryDependencies),
                directoryOutputs: FromReadOnlyList(directoryOutputs),
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: FromReadOnlyList(untrackedPaths, CmdHelper.GetCmdDependencies(pathTable).ToArray()),
                untrackedScopes: FromReadOnlyList(untrackedScopes, CmdHelper.GetCmdDependencyScopes(pathTable).ToArray()),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: FromReadOnlyList(successExitCodes),
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                options: options));
        }

        private static ReadOnlyArray<T> FromReadOnlyList<T>(IReadOnlyList<T> list, params T[] additions)
        {
            if (list == null || list.Count == 0)
            {
                if (additions.Length == 0)
                {
                    return ReadOnlyArray<T>.Empty;
                }

                return ReadOnlyArray<T>.From(additions);
            }

            if (additions.Length == 0)
            {
                return ReadOnlyArray<T>.FromWithoutCopy(list.ToArray());
            }

            return ReadOnlyArray<T>.From(list.Concat(additions));
        }

        [Fact]
        public async Task CopyFileWritable()
        {
            const string Contents = "Matches!";
            const string BadContents = "Anti-Matches!";

            await WithCachingExecutionEnvironment(
                GetFullPath(".cache"),
                async env =>
                {
                    env.InMemoryContentCache.ReinitializeRealizationModeTracking();

                    string source = GetFullPath("source");
                    AbsolutePath sourceAbsolutePath = AbsolutePath.Create(env.Context.PathTable, source);

                    string destination = GetFullPath("dest");
                    AbsolutePath destinationAbsolutePath = AbsolutePath.Create(env.Context.PathTable, destination);

                    File.WriteAllText(destination, BadContents);
                    File.WriteAllText(source, Contents);

                    var pip = new CopyFile(
                        FileArtifact.CreateSourceFile(sourceAbsolutePath),
                        FileArtifact.CreateOutputFile(destinationAbsolutePath),
                        ReadOnlyArray<StringId>.Empty,
                        PipProvenance.CreateDummy(env.Context),
                        global::BuildXL.Pips.Operations.CopyFile.Options.OutputsMustRemainWritable);

                    var testRunChecker = new TestRunChecker(destination);

                    await testRunChecker.VerifySucceeded(env, pip, Contents);

                    await testRunChecker.VerifyUpToDate(env, pip, Contents);

                    // The file should be writable (i.e., copy realization mode), since we opted out of hardlinking. So we can modify it, and expect the pip to re-run.
                    XAssert.AreEqual(FileRealizationMode.Copy, env.InMemoryContentCache.GetRealizationMode(destination));
                    env.InMemoryContentCache.ReinitializeRealizationModeTracking();
                    XAssert.IsTrue(File.Exists(destination), "File does not exist");
                    try
                    {
                        File.WriteAllText(destination, BadContents);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        XAssert.Fail("Failed writing to the destination file. This implies that the outputs were not left writable, despite requesting that via Process.Options.OutputsMustRemainWritable");
                    }
                });
        }

        /// <summary>
        /// The returned process will produce a warning.
        /// </summary>
        private static Process CreateWarningProcess(PipExecutionContext context, AbsolutePath directory, AbsolutePath output)
        {
            var pathTable = context.PathTable;

            FileArtifact stdout = FileArtifact.CreateSourceFile(output).CreateNextWrittenVersion();
            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));
            return AssignFakePipId(new Process(
                executable: exe,
                workingDirectory: directory,
                arguments: PipDataBuilder.CreatePipData(
                    pathTable.StringTable,
                    " ",
                    PipDataFragmentEscaping.NoEscaping,
                    OperatingSystemHelper.IsUnixOS
                        ? "-c 'echo WARNING'"
                        : "/d /c echo WARNING"
                    ),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: stdout,
                standardError: FileArtifact.Invalid,
                standardDirectory: output.GetParent(pathTable),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(stdout.WithAttributes()),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                warningRegex: new RegexDescriptor(StringId.Create(context.StringTable, "WARNING"), RegexOptions.IgnoreCase)));
        }

        private static string[] GenerateTestErrorMessages(int errorMessageLength)
        {
            
            if (errorMessageLength <= 0)
            {
                return new string[0];
            }

            List<string> toEcho = new List<string>();
            StringBuilder sb = new StringBuilder(errorMessageLength);
            for (int i = 0; i < errorMessageLength; i++)
            {
                if (i == errorMessageLength - 1)
                {
                    sb.Append('Z');
                }
                else if (i > 4 && i % (i / 4) == 0)
                {
                    toEcho.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append('M');
                }
            }

            toEcho.Add(sb.ToString());

            return toEcho.ToArray();
        }

        /// <summary>
        /// This method returns a process that will fail. It produces multi-line output with custom error regex (or default, if not provided).
        /// </summary>
        private Process CreateErrorProcess(
            PipExecutionContext context,
            AbsolutePath directory,
            AbsolutePath output,
            string errorPattern = null,
            int errorMessageLength = 0,
            string scriptContent = null)
        {
            var pathTable = context.PathTable;
            RegexDescriptor errorRegex = errorPattern == null
                ? default
                : new RegexDescriptor(StringId.Create(context.StringTable, errorPattern), RegexOptions.IgnoreCase);

            if (scriptContent == null)
            {
                string[] errorMessages = GenerateTestErrorMessages(errorMessageLength);
                StringBuilder command = new StringBuilder();
                
                if (OperatingSystemHelper.IsUnixOS)
                {
                    command.AppendLine("@echo off");
                }
                
                command.AppendLine(I($"echo ERROR "));
                foreach (var errorMessage in errorMessages)
                {
                    command.AppendLine(I($"echo ERROR {errorMessage}"));
                }

                command.AppendLine("echo WARNING");
                scriptContent = command.ToString();
            }

            string cmdScript = OperatingSystemHelper.IsUnixOS ? GetFullPath("script.sh") : GetFullPath("script.cmd");
            File.WriteAllText(cmdScript, scriptContent);
            if (OperatingSystemHelper.IsUnixOS)
            {
                chmod(cmdScript, 0x1ff);
            }

            FileArtifact cmdScriptFile = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, cmdScript));

            FileArtifact stdout = FileArtifact.CreateSourceFile(output).CreateNextWrittenVersion();
            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));
            return AssignFakePipId(
                new Process(
                    executable: exe,
                    workingDirectory: directory,
                    arguments: PipDataBuilder.CreatePipData(
                        pathTable.StringTable,
                        " ",
                        PipDataFragmentEscaping.NoEscaping,
                        OperatingSystemHelper.IsUnixOS ? I($"-c \"{cmdScript}\"") : I($"/d /c {cmdScript}")
                    ),
                    responseFile: FileArtifact.Invalid,
                    responseFileData: PipData.Invalid,
                    environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                    standardInput: FileArtifact.Invalid,
                    standardOutput: stdout,
                    standardError: FileArtifact.Invalid,
                    standardDirectory: output.GetParent(pathTable),
                    warningTimeout: null,
                    timeout: null,
                    dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe, cmdScriptFile),
                    outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(stdout.WithAttributes()),
                    directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                    directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                    orderDependencies: ReadOnlyArray<PipId>.Empty,
                    untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                    tags: ReadOnlyArray<StringId>.Empty,
                    successExitCodes: ReadOnlyArray<int>.From(new[] { 777 }),
                    semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                    provenance: PipProvenance.CreateDummy(context),
                    toolDescription: StringId.Invalid,
                    additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty,
                    errorRegex: errorRegex));
        }

        /// <summary>
        /// The returned process will produce a warning.
        /// </summary>
        private static Process CreateEchoProcess(PipExecutionContext context, AbsolutePath source, List<AbsolutePath> destinations, uint pipId, string param)
        {
            var pathTable = context.PathTable;

            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, CmdHelper.OsShellExe));

            List<FileArtifactWithAttributes> outputs = new List<FileArtifactWithAttributes>();
            foreach (AbsolutePath destination in destinations)
            {
                outputs.Add(FileArtifactWithAttributes.Create(FileArtifact.CreateSourceFile(destination), FileExistence.Required).CreateNextWrittenVersion());
            }

            return AssignFakePipId(new Process(
                executable: exe,
                workingDirectory: source.GetParent(pathTable),
                arguments: PipDataBuilder.CreatePipData(
                    pathTable.StringTable,
                    " ",
                    PipDataFragmentEscaping.NoEscaping,
                    OperatingSystemHelper.IsUnixOS
                        ? "-c ' echo " + param + " '"
                        : "/d /c echo " + param),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: FileArtifact.CreateSourceFile(destinations[0].GetParent(pathTable)).CreateNextWrittenVersion(),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.FromWithoutCopy(exe),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.From(outputs),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencies(pathTable)),
                untrackedScopes: ReadOnlyArray<AbsolutePath>.From(CmdHelper.GetCmdDependencyScopes(pathTable)),
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty), pipId);
        }

        private static TPip AssignFakePipId<TPip>(TPip pip, uint pipId = 123) where TPip : Pip
        {
            pip.PipId = new PipId(pipId);
            return pip;
        }

        /// <summary>
        /// Helper class that keeps track of the counters in the env.
        /// </summary>
        private class TestRunChecker
        {
            private int m_expectedRunCount;
            private int m_expectedUpToDateCount;
            private int m_expectedDeployedFromCache;
            private int m_expectedWarningCount;
            private int m_expectedWarningFromCacheCount;
            private string m_destination;

            public TestRunChecker(string destination = null)
            {
                m_destination = destination;
            }

            public string ExpectedContentsSuffix { get; set; }

            public void ExpectWarning(int nrOfWarnigns = 1)
            {
                m_expectedWarningCount += nrOfWarnigns;
            }

            public void ExpectWarningFromCache(int nrOfWarnigns = 1)
            {
                m_expectedWarningFromCacheCount += nrOfWarnigns;
            }

            public Task VerifySucceeded(DummyPipExecutionEnvironment env, Pip pip, string expectedContents = null, bool expectMarkedPerpertuallyDirty = false)
            {
                Contract.Assert((m_destination == null) == (expectedContents == null), "must specify expected contents if you set a destination file to check");

                m_expectedRunCount++;
                return Verify(PipResultStatus.Succeeded, env, pip, expectedContents, expectMarkedPerpertuallyDirty);
            }

            public Task VerifyUpToDate(DummyPipExecutionEnvironment env, Pip pip, string expectedContents = null, bool expectMarkedPerpertuallyDirty = false)
            {
                Contract.Assert((m_destination == null) == (expectedContents == null), "must specify expected contents if you set a destination file to check");

                m_expectedUpToDateCount++;
                return Verify(PipResultStatus.UpToDate, env, pip, expectedContents, expectMarkedPerpertuallyDirty);
            }

            public Task VerifyFailed(DummyPipExecutionEnvironment env, Pip pip, string expectedContents = null, bool expectMarkedPerpertuallyDirty = false)
            {
                return Verify(PipResultStatus.Failed, env, pip, expectedContents, expectMarkedPerpertuallyDirty);
            }

            public Task VerifyDeployedFromCache(DummyPipExecutionEnvironment env, Pip pip, string expectedContents = null)
            {
                m_expectedDeployedFromCache++;
                return Verify(PipResultStatus.DeployedFromCache, env, pip, expectedContents);
            }

            public async Task Verify(PipResultStatus expectedStatus, DummyPipExecutionEnvironment env, Pip pip, string expectedContents = null, bool expectMarkedPerpertuallyDirty = false)
            {
                Contract.Assert((m_destination == null) == (expectedContents == null), "must specify expected contents if you set a destination file to check");

                await VerifyPipResult(expectedStatus, env, pip, expectMarkedPerpertuallyDirty: expectMarkedPerpertuallyDirty);
                XAssert.AreEqual(m_expectedRunCount, env.OutputFilesProduced, "produced count");
                XAssert.AreEqual(m_expectedUpToDateCount, env.OutputFilesUpToDate, "up to date count");
                XAssert.AreEqual(m_expectedDeployedFromCache, env.OutputFilesDeployedFromCache, "deployed from cache count");

                XAssert.AreEqual(m_expectedWarningFromCacheCount, env.WarningsFromCache);
                XAssert.AreEqual(m_expectedWarningFromCacheCount, env.PipsWithWarningsFromCache);
                XAssert.AreEqual(m_expectedWarningCount, env.Warnings);
                XAssert.AreEqual(m_expectedWarningCount, env.PipsWithWarnings);

                if (m_destination != null)
                {
                    XAssert.AreEqual(
                        I($"'{expectedContents + ExpectedContentsSuffix}'"),
                        I($"'{File.ReadAllText(m_destination)}'"), 
                        "File contents of: " + m_destination);
                }
            }
        }
    }

    // This class creates a fake ITwoPhaseFingerprintStore implementation to be used for mocking the 
    // behavior of duplicated cache entry for this strong fingerprint in the L2 cache.
    // It is necessary to test the convergence of cache content.
    internal sealed class TestPipExecutorTwoPhaseFingerprintStore : ITwoPhaseFingerprintStore
    {
        internal WeakContentFingerprint M_weakFingerprint = WeakContentFingerprint.Zero;
        internal ContentHash M_pathSetHash = ContentHashingUtilities.ZeroHash;
        internal StrongContentFingerprint M_strongFingerprint = StrongContentFingerprint.Zero;
        internal int M_publishTries = 0;
        internal bool DoneOnce = false;
        internal List<long> M_len = new List<long>(2);
        internal CacheEntry M_cacheEntry;

        internal TestPipExecutorTwoPhaseFingerprintStore()
        {
        }

        private async Task<Possible<PublishedEntryRef, Failure>> GetOne()
        {
            await FinishedTask;

            if (M_publishTries == 0)
            {
                return PublishedEntryRef.Default;
            }

            return new PublishedEntryRef(M_pathSetHash, M_strongFingerprint, "foobar", PublishedEntryRefLocality.Remote);
        }

        public IEnumerable<Task<Possible<PublishedEntryRef, Failure>>> ListPublishedEntriesByWeakFingerprint(WeakContentFingerprint weak)
        {
            yield return GetOne();
        }

        public Task<Possible<CacheEntry?, Failure>> TryGetCacheEntryAsync(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, StrongContentFingerprint strongFingerprint)
        {
            return Task.FromResult(new Possible<CacheEntry?, Failure>(M_cacheEntry));
        }

        public async Task<Possible<CacheEntryPublishResult, Failure>> TryPublishCacheEntryAsync(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, StrongContentFingerprint strongFingerprint, CacheEntry entry, CacheEntryPublishMode mode = CacheEntryPublishMode.CreateNew)
        {
            await FinishedTask;

            if (M_publishTries == 0)
            {
                M_weakFingerprint = weakFingerprint;
                M_pathSetHash = pathSetHash;
                M_strongFingerprint = strongFingerprint;
                M_cacheEntry = entry;
            }

            M_publishTries++;

            if (!DoneOnce)
            {
                DoneOnce = true;
                return CacheEntryPublishResult.CreatePublishedResult();
            }

            return CacheEntryPublishResult.CreateConflictResult(M_cacheEntry);
        }

        // This is finished task that is used to prevent compilation warning in async method with no reasonable await.
        private static Task FinishedTask { get; } = Task.FromResult(42);
    }
}
