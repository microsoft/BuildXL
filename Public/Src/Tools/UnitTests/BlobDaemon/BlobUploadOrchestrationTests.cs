// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.CLI;
using Test.BuildXL.TestUtilities.Xunit;
using Test.Tool.ServicePipDaemonTestUtilities;
using Tool.BlobDaemon;
using Tool.ServicePipDaemon;
using Xunit;

namespace Test.Tool.BlobDaemon
{
    /// <summary>
    /// Tests for the upload orchestration in <see cref="BlobDaemon"/> (the server-side-copy vs. local-upload fallback
    /// decision made in UploadFileAsync). The Azure Storage interactions are stubbed via <see cref="IBlobUploadClient"/>,
    /// and the BuildXL API calls (GetContentLocationInBlobStorage, MaterializeFile) are answered by an in-memory IPC server.
    /// </summary>
    public sealed class BlobUploadOrchestrationTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        // Unique per test instance so setting it on the process environment can't collide across tests running in parallel.
        private readonly string TestAuthEnvVarName = "BLOBDAEMON_TEST_AUTH_VAR_" + Guid.NewGuid().ToString("N");

        public BlobUploadOrchestrationTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void SuccessfulServerSideCopyDoesNotFallBackToLocalUpload()
        {
            var (mock, materializeCallCount, ipcResult) = RunSingleOutputFileUpload(serverSideCopySucceeds: true);

            XAssert.IsTrue(ipcResult.Succeeded, ipcResult.Payload);
            XAssert.AreEqual(1, mock.TryServerSideCopyCallCount);
            XAssert.AreEqual(0, mock.UploadCallCount, "A successful server-side copy must not trigger a local upload.");
            XAssert.AreEqual(0, materializeCallCount, "A successful server-side copy must not trigger a materialization.");
        }

        [Fact]
        public void FailedServerSideCopyFallsBackToMaterializeAndLocalUpload()
        {
            // Regression guard: a server-side copy that does not succeed must fall through to the
            // materialize + local-upload path (this was silently skipped when failed copies were treated as success).
            var (mock, materializeCallCount, ipcResult) = RunSingleOutputFileUpload(serverSideCopySucceeds: false);

            XAssert.IsTrue(ipcResult.Succeeded, ipcResult.Payload);
            XAssert.AreEqual(1, mock.TryServerSideCopyCallCount);
            XAssert.AreEqual(1, mock.UploadCallCount, "A failed server-side copy must fall back to a local upload.");
            XAssert.AreEqual(1, materializeCallCount, "A failed server-side copy must fall back to materializing the file.");
        }

        [Fact]
        public void ResolvedContentTypeIsPassedToUploadClient()
        {
            Environment.SetEnvironmentVariable(TestAuthEnvVarName, "dummy-token");
            try
            {
                var mock = new MockBlobUploadClient(serverSideCopySucceeds: true);
                var sourceUri = new Uri("https://cacheaccount.blob.core.windows.net/cache/content");
                var hash = FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.EmptyHash).Render();
                var resolver = new ContentTypeResolver(new Dictionary<string, string> { [".txt"] = "text/plain" });
                string capturedContentType = null;

                var ipcProvider = IpcFactory.GetProvider();
                var ipcExecutor = new LambdaIpcOperationExecutor(operation =>
                {
                    var apiCommand = global::BuildXL.Ipc.ExternalApi.Commands.Command.Deserialize(operation.Payload);
                    switch (apiCommand)
                    {
                        case GetContentLocationInBlobStorage getContentLocation:
                            return IpcResult.Success(getContentLocation.RenderResult(sourceUri));
                        case MaterializeFileCommand materializeFile:
                            return IpcResult.Success(materializeFile.RenderResult(true));
                        default:
                            return new IpcResult(IpcResultStatus.ExecutionError, $"Unexpected command: {apiCommand.GetType().Name}");
                    }
                });

                WithIpcServer(
                    ipcProvider,
                    ipcExecutor,
                    new ServerConfig(),
                    (moniker, mockServer) =>
                    {
                        var bxlApiClient = MockClient.CreateDummyApiClient(ipcProvider, moniker);
                        WithSetup(
                            mock,
                            bxlApiClient,
                            daemon =>
                            {
                                var uploadTarget = "#uri#https://targetaccount.blob.core.windows.net/target/file.txt#";
                                var command = global::Tool.ServicePipDaemon.ServicePipDaemon.ParseArgsForIPCCall(
                                    $"uploadArtifacts --ipcServerMoniker {moniker.Id} --file file.txt --fileId 12345:1 --hash {hash} --fileTarget {uploadTarget} --fileAuthVar {TestAuthEnvVarName}",
                                    new UnixParser());
                                var ipcResult = command.Command.ServerAction(command, daemon).GetAwaiter().GetResult();
                                XAssert.IsTrue(ipcResult.Succeeded, ipcResult.Payload);
                                capturedContentType = daemon.LastContentType;
                            },
                            resolver);
                    });

                XAssert.AreEqual("text/plain", capturedContentType);
            }
            finally
            {
                Environment.SetEnvironmentVariable(TestAuthEnvVarName, null);
            }
        }

        [Fact]
        public void MismatchedFileArgumentCountsReturnError()
        {
            // Two files but a single fileId: the per-file argument arrays are not aligned.
            var result = RunUploadArtifacts("--file a.txt --file b.txt --fileId 12345:1");

            XAssert.IsFalse(result.Succeeded);
            XAssert.AreEqual(IpcResultStatus.GenericError, result.ExitCode);
        }

        [Fact]
        public void MismatchedDirectoryArgumentCountsReturnError()
        {
            // Two directories but a single directoryId: the per-directory argument arrays are not aligned.
            var result = RunUploadArtifacts("--directory d1 --directory d2 --directoryId 1:1:1");

            XAssert.IsFalse(result.Succeeded);
            XAssert.AreEqual(IpcResultStatus.GenericError, result.ExitCode);
        }

        [Fact]
        public void ExplicitlyListedAbsentFileReturnsInvalidInput()
        {
            // An individually specified file that does not exist (absent-file hash) cannot be uploaded.
            var absentHash = FileContentInfo.CreateWithUnknownLength(WellKnownContentHashes.AbsentFile).Render();
            var result = RunUploadArtifacts(
                $"--file gone.txt --fileId 12345:1 --hash {absentHash} --fileTarget #uri#https://example.com/target/file.txt# --fileAuthVar {TestAuthEnvVarName}");

            XAssert.IsFalse(result.Succeeded);
            XAssert.AreEqual(IpcResultStatus.InvalidInput, result.ExitCode);
        }

        [Fact]
        public void NothingToUploadReturnsSuccess()
        {
            // A request with no files and no directories is a no-op that succeeds.
            var result = RunUploadArtifacts(string.Empty);

            XAssert.IsTrue(result.Succeeded, result.Payload);
        }

        private IIpcResult RunUploadArtifacts(string commandArgs)
        {
            // A dummy auth secret so the up-front auth-var validation (which runs before these checks) passes.
            Environment.SetEnvironmentVariable(TestAuthEnvVarName, "dummy-token");
            try
            {
                IIpcResult ipcResult = null;
                var moniker = IpcMoniker.CreateNew();
                var apiClient = new Client(new MockClient(operation => IpcResult.Success("true")));
                WithSetup(
                    new MockBlobUploadClient(serverSideCopySucceeds: true),
                    apiClient,
                    daemon =>
                    {
                        var command = global::Tool.ServicePipDaemon.ServicePipDaemon.ParseArgsForIPCCall(
                            $"uploadArtifacts --ipcServerMoniker {moniker.Id} {commandArgs}",
                            new UnixParser());
                        ipcResult = command.Command.ServerAction(command, daemon).GetAwaiter().GetResult();
                    });
                return ipcResult;
            }
            finally
            {
                Environment.SetEnvironmentVariable(TestAuthEnvVarName, null);
            }
        }

        private (MockBlobUploadClient mock, int materializeCallCount, IIpcResult ipcResult) RunSingleOutputFileUpload(bool serverSideCopySucceeds)
        {
            // The daemon validates that the named auth env var is set before uploading, so provide a dummy value.
            Environment.SetEnvironmentVariable(TestAuthEnvVarName, "dummy-token");
            try
            {
                var mock = new MockBlobUploadClient(serverSideCopySucceeds);
                var sourceUri = new Uri("https://cacheaccount.blob.core.windows.net/cache/content");
                // A non-absent content hash so the file is treated as present; rewrite count 1 (see fileId below) makes it an output file.
                var hash = FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.EmptyHash).Render();
                int materializeCallCount = 0;
                IIpcResult ipcResult = null;

                var ipcProvider = IpcFactory.GetProvider();

                // Mocks the BuildXL server answering the daemon's API calls.
                var ipcExecutor = new LambdaIpcOperationExecutor(operation =>
                {
                    var apiCommand = global::BuildXL.Ipc.ExternalApi.Commands.Command.Deserialize(operation.Payload);
                    switch (apiCommand)
                    {
                        case GetContentLocationInBlobStorage getContentLocation:
                            return IpcResult.Success(getContentLocation.RenderResult(sourceUri));
                        case MaterializeFileCommand materializeFile:
                            Interlocked.Increment(ref materializeCallCount);
                            return IpcResult.Success(materializeFile.RenderResult(true));
                        default:
                            return new IpcResult(IpcResultStatus.ExecutionError, $"Unexpected command: {apiCommand.GetType().Name}");
                    }
                });

                WithIpcServer(
                    ipcProvider,
                    ipcExecutor,
                    new ServerConfig(),
                    (moniker, mockServer) =>
                    {
                        var bxlApiClient = MockClient.CreateDummyApiClient(ipcProvider, moniker);
                        WithSetup(
                            mock,
                            bxlApiClient,
                            daemon =>
                            {
                                var uploadTarget = "#uri#https://targetaccount.blob.core.windows.net/target/file.txt#";
                                var uploadArtifactsCommand = global::Tool.ServicePipDaemon.ServicePipDaemon.ParseArgsForIPCCall(
                                    $"uploadArtifacts --ipcServerMoniker {moniker.Id} --file file.txt --fileId 12345:1 --hash {hash} --fileTarget {uploadTarget} --fileAuthVar {TestAuthEnvVarName}",
                                    new UnixParser());
                                ipcResult = uploadArtifactsCommand.Command.ServerAction(uploadArtifactsCommand, daemon).GetAwaiter().GetResult();
                            });
                    });

                return (mock, materializeCallCount, ipcResult);
            }
            finally
            {
                Environment.SetEnvironmentVariable(TestAuthEnvVarName, null);
            }
        }

        private void WithSetup(IBlobUploadClient mockUploadClient, Client apiClient, Action<TestableBlobDaemon> action, ContentTypeResolver contentTypeResolver = null)
        {
            string moniker = IpcFactory.GetProvider().RenderConnectionString(IpcMoniker.CreateNew());
            var daemonConfig = new DaemonConfig(VoidLogger.Instance, moniker: moniker, enableCloudBuildIntegration: false);
            var blobDaemonConfig = new BlobDaemonConfig(maxDegreeOfParallelism: BlobDaemonConfig.DefaultMaxDegreeOfParallelism);
            using var daemon = new TestableBlobDaemon(UnixParser.Instance, daemonConfig, blobDaemonConfig, mockUploadClient, apiClient, contentTypeResolver);
            action(daemon);
        }

        /// <summary>
        /// A testable subclass of <see cref="BlobDaemon"/> that returns a caller-supplied <see cref="IBlobUploadClient"/>
        /// instead of a real Azure Storage-backed one.
        /// </summary>
        private sealed class TestableBlobDaemon : global::Tool.BlobDaemon.BlobDaemon
        {
            private readonly IBlobUploadClient m_mockUploadClient;

            /// <summary>The content type passed to the most recent <see cref="CreateBlobUploadClient"/> call.</summary>
            public string LastContentType { get; private set; }

            public TestableBlobDaemon(IParser parser, DaemonConfig daemonConfig, BlobDaemonConfig blobDaemonConfig, IBlobUploadClient mockUploadClient, Client client, ContentTypeResolver contentTypeResolver = null)
                : base(parser, daemonConfig, blobDaemonConfig, contentTypeResolver: contentTypeResolver, bxlClient: client)
            {
                m_mockUploadClient = mockUploadClient;
            }

            protected override IBlobUploadClient CreateBlobUploadClient(BlobClient blobClient, string logContext, string contentType)
            {
                LastContentType = contentType;
                return m_mockUploadClient;
            }
        }

        /// <summary>
        /// A test double for <see cref="IBlobUploadClient"/> that records how many times each operation was invoked and
        /// returns a configurable server-side-copy outcome.
        /// </summary>
        private sealed class MockBlobUploadClient : IBlobUploadClient
        {
            private readonly bool m_serverSideCopySucceeds;

            public int TryServerSideCopyCallCount;
            public int UploadCallCount;

            public MockBlobUploadClient(bool serverSideCopySucceeds)
            {
                m_serverSideCopySucceeds = serverSideCopySucceeds;
            }

            public Task<bool> TryServerSideCopyAsync(Uri sourceUri, TimeSpan timeout)
            {
                Interlocked.Increment(ref TryServerSideCopyCallCount);
                return Task.FromResult(m_serverSideCopySucceeds);
            }

            public Task UploadAsync(string localFilePath)
            {
                Interlocked.Increment(ref UploadCallCount);
                return Task.CompletedTask;
            }
        }
    }
}
