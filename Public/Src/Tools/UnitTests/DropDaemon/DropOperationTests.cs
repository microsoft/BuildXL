// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;
using BuildXL.Storage;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Drop.WebApi;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.DropDaemon;
using Tool.ServicePipDaemon;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.DropDaemon
{
    public sealed class DropOperationTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public enum DropOp { Create, Finalize }

        public DropOperationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(DropOp.Create, true)]
        [InlineData(DropOp.Create, false)]
        [InlineData(DropOp.Finalize, true)]
        [InlineData(DropOp.Finalize, false)]
        public void TestSingleDropOperation(DropOp dropOp, bool shouldSucceed)
        {
            var dropClient = new MockDropClient(createSucceeds: shouldSucceed, finalizeSucceeds: shouldSucceed);
            WithSetup(dropClient, (daemon, etwListener) =>
            {
                var rpcResult =
                    dropOp == DropOp.Create ? daemon.Create() :
                    dropOp == DropOp.Finalize ? daemon.Finalize() :
                    null;
                Assert.True(rpcResult != null, "unknown drop operation: " + dropOp);
                Assert.Equal(shouldSucceed, rpcResult.Succeeded);

                var expectedEventKind =
                    dropOp == DropOp.Create ? EventKind.DropCreation :
                    dropOp == DropOp.Finalize ? EventKind.DropFinalization :
                    EventKind.TargetAdded; // something bogus, just to make the compiler happy
                AssertDequeueEtwEvent(etwListener, shouldSucceed, expectedEventKind);
            });
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void TestCreateFinalize(bool shouldCreateSucceed, bool shouldFinalizeSucceed)
        {
            var dropClient = new MockDropClient(createSucceeds: shouldCreateSucceed, finalizeSucceeds: shouldFinalizeSucceed);
            WithSetup(dropClient, (daemon, etwListener) =>
            {
                var rpcResult = daemon.Create();
                AssertRpcResult(shouldCreateSucceed, rpcResult);
                rpcResult = daemon.Finalize();
                AssertRpcResult(shouldFinalizeSucceed, rpcResult);

                AssertDequeueEtwEvent(etwListener, shouldCreateSucceed, EventKind.DropCreation);
                AssertDequeueEtwEvent(etwListener, shouldFinalizeSucceed, EventKind.DropFinalization);
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestUrlAndErrorMessageFields(bool shouldSucceed)
        {
            var url = "xya://drop.url";
            var errorMessage = shouldSucceed ? string.Empty : "err";
            var createFunc = shouldSucceed
                ? new MockDropClient.CreateDelegate(() => Task.FromResult(new DropItem()))
                : new MockDropClient.CreateDelegate(() => MockDropClient.FailTask<DropItem>(errorMessage));
            var dropClient = new MockDropClient(dropUrl: url, createFunc: createFunc);
            WithSetup(dropClient, (daemon, etwListener) =>
            {
                var rpcResult = daemon.Create();
                AssertRpcResult(shouldSucceed, rpcResult);
                var dropEvent = AssertDequeueEtwEvent(etwListener, shouldSucceed, EventKind.DropCreation);
                Assert.Equal(url, dropEvent.DropUrl);
                Assert.True(dropEvent.ErrorMessage.Contains(errorMessage));
            });
        }

        [Fact]
        public void TestFailingWithDropErrorDoesNotThrow()
        {
            var dropClient = GetFailingMockDropClient(() => new DropServiceException("injected drop service failure"));
            WithSetup(dropClient, (daemon, etwListener) =>
            {
                // create
                {
                    var rpcResult = daemon.Create();
                    AssertRpcResult(shouldSucceed: false, rpcResult: rpcResult);
                    AssertDequeueEtwEvent(etwListener, succeeded: false, kind: EventKind.DropCreation);
                }

                // add file
                {
                    var rpcResult = daemon.AddFileAsync(new DropItemForFile("file.txt")).Result;
                    AssertRpcResult(shouldSucceed: false, rpcResult: rpcResult);
                }

                // finalize
                {
                    var rpcResult = daemon.Finalize();
                    AssertRpcResult(shouldSucceed: false, rpcResult: rpcResult);
                    AssertDequeueEtwEvent(etwListener, succeeded: false, kind: EventKind.DropFinalization);
                }
            });
        }

        [Fact]
        public void TestFailingWithGenericErrorThrows()
        {
            var dropClient = GetFailingMockDropClient(() => new Exception("injected generic failure"));
            WithSetup(dropClient, (daemon, etwListener) =>
            {
                // create
                {
                    Assert.Throws<Exception>(() => daemon.Create());

                    // etw event must nevertheless be received
                    AssertDequeueEtwEvent(etwListener, succeeded: false, kind: EventKind.DropCreation);
                }

                // add file
                {
                    Assert.Throws<Exception>(() => daemon.AddFileAsync(new DropItemForFile("file.txt")).GetAwaiter().GetResult());
                }

                // finalize
                {
                    Assert.Throws<Exception>(() => daemon.Finalize());

                    // etw event must nevertheless be received
                    AssertDequeueEtwEvent(etwListener, succeeded: false, kind: EventKind.DropFinalization);
                }
            });
        }

        private const bool TestChunkDedup = false;

        [Fact]
        public void TestAddFile_AssociateDoesntNeedServer()
        {
            // this client only touches item.BlobIdentifier and returns 'Associated'
            var dropClient = new MockDropClient(addFileFunc: (item) =>
            {
                Assert.NotNull(item.BlobIdentifier);
                return Task.FromResult(AddFileResult.Associated);
            });

            WithSetup(dropClient, (daemon, etwListener) =>
            {
                var provider = IpcFactory.GetProvider();
                var connStr = provider.CreateNewConnectionString();
                var contentInfo = new FileContentInfo(new ContentHash(HashType.Vso0), length: 123456);

                var client = new Client(provider.GetClient(connStr, new ClientConfig()));
                var addFileItem = new DropItemForBuildXLFile(client, filePath: "file-which-doesnt-exist.txt", fileId: "23423423:1", chunkDedup: TestChunkDedup, fileContentInfo: contentInfo);

                // addfile succeeds without needing BuildXL server nor the file on disk
                IIpcResult result = daemon.AddFileAsync(addFileItem).GetAwaiter().GetResult();
                XAssert.IsTrue(result.Succeeded);

                // calling MaterializeFile fails because no BuildXL server is running
                Assert.Throws<DaemonException>(() => addFileItem.EnsureMaterialized().GetAwaiter().GetResult());
            });
        }

        private const string TestFileContent = "hi";
        private static readonly FileContentInfo TestFileContentInfo = new FileContentInfo(
            ParseContentHash("VSO0:C8DE9915376DBAC9F79AD7888D3C9448BE0F17A0511004F3D4A470F9E94B9F2E00"),
            length: TestFileContent.Length);

        private static ContentHash ParseContentHash(string v)
        {
            ContentHash result;
            XAssert.IsTrue(ContentHash.TryParse(v, out result));
            return result;
        }

        [Fact]
        public void TestAddBuildXLFile_UploadCallsBuildXLServer()
        {
            string fileId = "142342:2";
            string filePath = Path.Combine(TestOutputDirectory, nameof(TestAddBuildXLFile_UploadCallsBuildXLServer) + "-test.txt");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // this client wants to read the file
            var dropClient = new MockDropClient(addFileFunc: (item) =>
            {
                Assert.NotNull(item.BlobIdentifier);
                var fileInfo = item.EnsureMaterialized().GetAwaiter().GetResult();
                XAssert.IsTrue(fileInfo != null && fileInfo.Exists);
                XAssert.AreEqual(TestFileContent, File.ReadAllText(fileInfo.FullName));
                return Task.FromResult(AddFileResult.UploadedAndAssociated);
            });

            WithSetup(dropClient, (daemon, etwListener) =>
            {
                var ipcProvider = IpcFactory.GetProvider();
                var ipcExecutor = new LambdaIpcOperationExecutor(op =>
                {
                    var cmd = ReceiveMaterializeFileCmdAndCheckItMatchesFileId(op.Payload, fileId);
                    File.WriteAllText(filePath, TestFileContent);
                    return IpcResult.Success(cmd.RenderResult(true));
                });
                WithIpcServer(
                    ipcProvider,
                    ipcExecutor,
                    new ServerConfig(),
                    (moniker, mockServer) =>
                    {
                        var client = new Client(ipcProvider.GetClient(ipcProvider.RenderConnectionString(moniker), new ClientConfig()));
                        var addFileItem = new DropItemForBuildXLFile(client, filePath, fileId, TestChunkDedup, fileContentInfo: TestFileContentInfo);

                        // addfile succeeds
                        IIpcResult result = daemon.AddFileAsync(addFileItem).GetAwaiter().GetResult();
                        XAssert.IsTrue(result.Succeeded, result.Payload);
                    });
            });
        }

        [Fact]
        public void TestLazilyMaterializedSymlinkRejected()
        {
            string fileId = "142342:3";
            string filePath = Path.Combine(TestOutputDirectory, nameof(TestLazilyMaterializedSymlinkRejected) + "-test.txt");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // this client wants to read the file
            var dropClient = new MockDropClient(addFileFunc: (item) =>
            {
                Assert.NotNull(item.BlobIdentifier);
                var ex = Assert.Throws<DaemonException>(() => item.EnsureMaterialized().GetAwaiter().GetResult());

                // rethrowing because that's what a real IDropClient would do (then Daemon is expected to handle it)
                throw ex;
            });

            WithSetup(dropClient, (daemon, etwListener) =>
            {
                var ipcProvider = IpcFactory.GetProvider();
                var ipcExecutor = new LambdaIpcOperationExecutor(op =>
                {
                    // this mock BuildXL server materializes a regular file, which we will treat as a symlink in this test
                    var cmd = ReceiveMaterializeFileCmdAndCheckItMatchesFileId(op.Payload, fileId);
                    File.WriteAllText(filePath, TestFileContent);
                    return IpcResult.Success(cmd.RenderResult(true));
                });
                WithIpcServer(
                    ipcProvider,
                    ipcExecutor,
                    new ServerConfig(),
                    (moniker, mockServer) =>
                    {
                        var client = new Client(ipcProvider.GetClient(ipcProvider.RenderConnectionString(moniker), new ClientConfig()));
                        var addFileItem = new DropItemForBuildXLFile(
                            symlinkTester: (file) => file == filePath ? true : false,
                            client: client,
                            filePath: filePath,
                            fileId: fileId,
                            chunkDedup: TestChunkDedup,
                            fileContentInfo: TestFileContentInfo);

                        // addfile files
                        IIpcResult result = daemon.AddFileAsync(addFileItem).GetAwaiter().GetResult();
                        XAssert.IsFalse(result.Succeeded, "expected addfile to fail; instead it succeeded and returned payload: " + result.Payload);
                        XAssert.IsTrue(result.Payload.Contains(DropItemForBuildXLFile.MaterializationResultIsSymlinkErrorPrefix));
                    });
            });
        }

        [Fact]
        public void TestDropDaemonOutrightRejectSymlinks()
        {
            // create a regular file that mocks a symlink (because creating symlinks on Windows is difficult?!?)
            var targetFile = Path.Combine(TestOutputDirectory, nameof(TestDropDaemonOutrightRejectSymlinks) + "-test.txt");
            File.WriteAllText(targetFile, "drop symlink test file");

            // check that drop daemon rejects it outright
            var dropClient = new MockDropClient(addFileSucceeds: true);
            WithSetup(dropClient, async (daemon, etwListener) =>
            {
                var ipcResult = await daemon.AddFileAsync(new DropItemForFile(targetFile), symlinkTester: (file) => file == targetFile ? true : false);
                Assert.False(ipcResult.Succeeded, "adding symlink to drop succeeded while it was expected to fail");
                Assert.True(ipcResult.Payload.Contains(global::Tool.DropDaemon.DropDaemon.SymlinkAddErrorMessagePrefix));
            });
        }

        [Theory]
        [InlineData(".*")]              // all files
        [InlineData(".*a\\.txt$")]      // only a.txt
        [InlineData(".*(a|c)\\.txt$")]  // a.txt and c.txt
        [InlineData(".*d\\.txt$")]      // no files
        public void TestAddDirectoryToDropWithFilters(string filter)
        {
            // TestOutputDirectory
            // |- foo <directory>  <- 'uploading' this directory
            //    |- a.txt
            //    |- b.txt
            //    |- bar <directory>
            //       |- c.txt

            string remoteDirectoryPath = "remoteDirectory";
            string fakeDirectoryId = "123:1:12345";
            var directoryPath = Path.Combine(TestOutputDirectory, "foo");

            var files = new List<(string fileName, string remoteFileName)>
            {
                (Path.Combine(directoryPath, "a.txt"), $"{remoteDirectoryPath}/a.txt"),
                (Path.Combine(directoryPath, "b.txt"), $"{remoteDirectoryPath}/b.txt"),
                (Path.Combine(directoryPath, "bar", "c.txt"), $"{remoteDirectoryPath}/bar/c.txt"),
            };

            var dropPaths = new List<string>();
            var expectedDropPaths = new HashSet<string>();
            var regex = new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            expectedDropPaths.AddRange(files.Where(a => regex.IsMatch(a.fileName)).Select(a => a.remoteFileName));
            
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var dropClient = new MockDropClient(addFileFunc: (item) =>
            {
                dropPaths.Add(item.RelativeDropPath);
                return Task.FromResult(AddFileResult.UploadedAndAssociated);
            });

            var ipcProvider = IpcFactory.GetProvider();
            
            // this lambda mocks BuildXL server receiving 'GetSealedDirectoryContent' API call and returning a response
            var ipcExecutor = new LambdaIpcOperationExecutor(op =>
            {
                var cmd = ReceiveGetSealedDirectoryContentCommandAndCheckItMatchesDirectoryId(op.Payload, fakeDirectoryId);

                // Now 'fake' the response - here we only care about the 'FileName' field.
                // In real life it's not the case, but this is a test and our custom addFileFunc
                // in dropClient simply collects the drop file names.
                var result = files.Select(a => CreateFakeSealedDirectoryFile(a.fileName)).ToList();

                return IpcResult.Success(cmd.RenderResult(result));
            });

            WithIpcServer(
                ipcProvider,
                ipcExecutor,
                new ServerConfig(),
                (moniker, mockServer) =>
                {
                    var bxlApiClient = new Client(ipcProvider.GetClient(ipcProvider.RenderConnectionString(moniker), new ClientConfig()));
                    WithSetup(
                        dropClient,
                        (daemon, etwListener) =>
                        {
                            var addArtifactsCommand = global::Tool.ServicePipDaemon.ServicePipDaemon.ParseArgs(
                                $"addartifacts --ipcServerMoniker {moniker.Id} --directory {directoryPath} --directoryId {fakeDirectoryId} --directoryDropPath {remoteDirectoryPath} --directoryFilter {filter}",
                                new UnixParser());
                            var ipcResult = addArtifactsCommand.Command.ServerAction(addArtifactsCommand, daemon).GetAwaiter().GetResult();

                            XAssert.IsTrue(ipcResult.Succeeded, ipcResult.Payload);
                            XAssert.AreSetsEqual(expectedDropPaths, dropPaths, expectedResult: true);
                        },
                        bxlApiClient);
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestAlwaysFailWhenAddingStaticallyListedFilesWithAbsentFileHash(bool isSourceFile)
        {
            var dropPaths = new List<string>();

            var dropClient = new MockDropClient(addFileFunc: (item) =>
            {
                dropPaths.Add(item.RelativeDropPath);
                return Task.FromResult(AddFileResult.Associated);
            });
           
            var ipcProvider = IpcFactory.GetProvider();
            var bxlApiClient = new Client(ipcProvider.GetClient(ipcProvider.CreateNewConnectionString(), new ClientConfig()));

            WithSetup(
                dropClient, 
                (daemon, etwListener) =>
                {
                    // only hash and file rewrite count are important here; the rest are just fake values
                    var hash = FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.CreateSpecialValue(1)).Render();
                    var addArtifactsCommand = global::Tool.ServicePipDaemon.ServicePipDaemon.ParseArgs(
                        $"addartifacts --ipcServerMoniker {daemon.Config.Moniker} --file non-existent-file.txt --dropPath remote-file-name.txt --hash {hash} --fileId 12345:{(isSourceFile ? 0 : 1)}",
                        new UnixParser());
                    var ipcResult = addArtifactsCommand.Command.ServerAction(addArtifactsCommand, daemon).GetAwaiter().GetResult();

                    XAssert.IsTrue(dropPaths.Count == 0);
                    XAssert.IsFalse(ipcResult.Succeeded);
                    XAssert.AreEqual(IpcResultStatus.InvalidInput, ipcResult.ExitCode);
                },
                bxlApiClient);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestAddingDirectoryContainingFilesWithAbsentFileHash(bool isSourceFile)
        {
            string remoteDirectoryPath = "remoteDirectory";
            string fakeDirectoryId = "123:1:12345";
            var directoryPath = Path.Combine(TestOutputDirectory, "foo");

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var dropPaths = new List<string>();

            var dropClient = new MockDropClient(addFileFunc: (item) =>
            {
                dropPaths.Add(item.RelativeDropPath);
                return Task.FromResult(AddFileResult.Associated);
            });

            var ipcProvider = IpcFactory.GetProvider();
            
            // this lambda mocks BuildXL server receiving 'GetSealedDirectoryContent' API call and returning a response
            var ipcExecutor = new LambdaIpcOperationExecutor(op =>
            {
                var cmd = ReceiveGetSealedDirectoryContentCommandAndCheckItMatchesDirectoryId(op.Payload, fakeDirectoryId);
                
                var file = new SealedDirectoryFile(
                    Path.Combine(directoryPath, "file.txt"),
                    new FileArtifact(new AbsolutePath(1), isSourceFile ? 0 : 1),
                    FileContentInfo.CreateWithUnknownLength(global::BuildXL.Scheduler.WellKnownContentHashes.AbsentFile));

                return IpcResult.Success(cmd.RenderResult(new List<SealedDirectoryFile> { file }));
            });

            WithIpcServer(
                ipcProvider,
                ipcExecutor,
                new ServerConfig(),
                (moniker, mockServer) =>
                {
                    var bxlApiClient = new Client(ipcProvider.GetClient(ipcProvider.RenderConnectionString(moniker), new ClientConfig()));
                    WithSetup(
                        dropClient,
                        (daemon, etwListener) =>
                        {
                            var addArtifactsCommand = global::Tool.ServicePipDaemon.ServicePipDaemon.ParseArgs(
                                $"addartifacts --ipcServerMoniker {moniker.Id} --directory {directoryPath} --directoryId {fakeDirectoryId} --directoryDropPath {remoteDirectoryPath} --directoryFilter .*",
                                new UnixParser());
                            var ipcResult = addArtifactsCommand.Command.ServerAction(addArtifactsCommand, daemon).GetAwaiter().GetResult();
                            
                            XAssert.IsTrue(dropPaths.Count == 0);
                            
                            // if an absent file is a source file, drop operation should have failed; otherwise, we simply skip it
                            XAssert.AreEqual(!isSourceFile, ipcResult.Succeeded);
                            XAssert.AreEqual(isSourceFile ? IpcResultStatus.InvalidInput : IpcResultStatus.Success, ipcResult.ExitCode);
                        },
                        bxlApiClient);
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
        }

        private static SealedDirectoryFile CreateFakeSealedDirectoryFile(string fileName)
        {
            return new SealedDirectoryFile(fileName, new FileArtifact(new AbsolutePath(1), 1), FileContentInfo.CreateWithUnknownLength(ContentHash.Random()));
        }

        private MaterializeFileCommand ReceiveMaterializeFileCmdAndCheckItMatchesFileId(string operationPayload, string expectedFileId)
        {
            var cmd = global::BuildXL.Ipc.ExternalApi.Commands.Command.Deserialize(operationPayload);
            XAssert.AreEqual(typeof(MaterializeFileCommand), cmd.GetType());
            var materializeFileCmd = (MaterializeFileCommand)cmd;
            XAssert.AreEqual(expectedFileId, FileId.ToString(materializeFileCmd.File));
            return materializeFileCmd;
        }

        private GetSealedDirectoryContentCommand ReceiveGetSealedDirectoryContentCommandAndCheckItMatchesDirectoryId(string payload, string expectedDirectoryId)
        {
            var cmd = global::BuildXL.Ipc.ExternalApi.Commands.Command.Deserialize(payload);
            XAssert.AreEqual(typeof(GetSealedDirectoryContentCommand), cmd.GetType());
            var getSealedDirectoryCmd = (GetSealedDirectoryContentCommand)cmd;
            XAssert.AreEqual(expectedDirectoryId, DirectoryId.ToString(getSealedDirectoryCmd.Directory));
            return getSealedDirectoryCmd;
        }

        private MockDropClient GetFailingMockDropClient(Func<Exception> exceptionFactory)
        {
            return new MockDropClient(
                createFunc: () => MockDropClient.CreateFailingTask<DropItem>(exceptionFactory),
                addFileFunc: (item) => MockDropClient.CreateFailingTask<AddFileResult>(exceptionFactory),
                finalizeFunc: () => MockDropClient.CreateFailingTask<FinalizeResult>(exceptionFactory));
        }

        private DropOperationBaseEvent AssertDequeueEtwEvent(DropEtwListener etwListener, bool succeeded, EventKind kind)
        {
            var dropEvent = etwListener.DequeueDropEvent();
            Assert.Equal(succeeded, dropEvent.Succeeded);
            Assert.Equal(kind, dropEvent.Kind);
            return dropEvent;
        }

        private void AssertRpcResult(bool shouldSucceed, IIpcResult rpcResult)
        {
            Assert.NotNull(rpcResult);
            Assert.Equal(shouldSucceed, rpcResult.Succeeded);
        }

        private void WithSetup(IDropClient dropClient, Action<global::Tool.DropDaemon.DropDaemon, DropEtwListener> action, Client apiClient = null)
        {
            var etwListener = ConfigureEtwLogging();
            string moniker = ServicePipDaemon.IpcProvider.RenderConnectionString(ServicePipDaemon.IpcProvider.CreateNewMoniker());
            var daemonConfig = new DaemonConfig(VoidLogger.Instance, moniker: moniker, enableCloudBuildIntegration: false);
            var dropConfig = new DropConfig(string.Empty, null);
            var daemon = new global::Tool.DropDaemon.DropDaemon(UnixParser.Instance, daemonConfig, dropConfig, Task.FromResult(dropClient), client: apiClient);
            action(daemon, etwListener);
        }

        private DropEtwListener ConfigureEtwLogging()
        {
            return new DropEtwListener();
        }
    }
}
