// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Ipc
{
    public sealed class ExternalApiTests : IpcTestBase
    {
        private PathTable PathTable { get; }

        public ExternalApiTests(ITestOutputHelper output) : base(output)
        {
            PathTable = new PathTable();
        }

        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { 0, 1, 2 },
            new object[] { true, false },
            new object[] { 0, 1, 2 })]
        public async Task TestGetSealedDirectoryContentAsync(uint partialSealId, bool isSharedOpaque, int numSealedDirectoryFiles)
        {
            // skip the invalid configuration
            if (partialSealId == 0 && isSharedOpaque)
            {
                return;
            }

            var dirPath = X("/a/b/");
            var dirArtifact = new DirectoryArtifact(AbsPath(dirPath), partialSealId, isSharedOpaque);
            var cmdResult = Enumerable
                .Range(0, numSealedDirectoryFiles)
                .Select(i => new SealedDirectoryFile($"f{i}", SourceFile(X($"{dirPath}/f{i}")), RandomFileContentInfo()))
                .ToList();

            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (GetSealedDirectoryContentCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.AreEqual(dirArtifact, cmd.Directory);
                XAssert.AreEqual(dirPath, cmd.FullDirectoryPath);
                return IpcResult.Success(cmd.RenderResult(cmdResult));
            });

            var maybeResult = await apiClient.GetSealedDirectoryContent(dirArtifact, dirPath);
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.ArrayEqual(cmdResult.ToArray(), maybeResult.Result.ToArray());
        }

        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { "hi" },
            new object[] { true, false },
            new object[] { true, false })]
        public async Task TestLogMessageAsync(string message, bool isWarning, bool expectedResult)
        {
            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (LogMessageCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.AreEqual(message, cmd.Message);
                XAssert.AreEqual(isWarning, cmd.IsWarning);
                return IpcResult.Success(cmd.RenderResult(expectedResult));
            });
            var maybeResult = await apiClient.LogMessage(message, isWarning);
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.AreEqual(expectedResult, maybeResult.Result);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public async Task TestMaterializeFileAsync(bool isSourceFile, bool expectedResult)
        {
            var path = X("/x/y/z");
            var fileArtifact = isSourceFile ? SourceFile(path) : OutputFile(path);
            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (MaterializeFileCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.AreEqual(fileArtifact, cmd.File);
                XAssert.AreEqual(path, cmd.FullFilePath);
                return IpcResult.Success(cmd.RenderResult(expectedResult));
            });
            var maybeResult = await apiClient.MaterializeFile(fileArtifact, path);
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.AreEqual(expectedResult, maybeResult.Result);
        }

        [Fact]
        public async Task TestRegisterFilesForBuildManifestAsync()
        {
            string dropName = "DropName";

            List<BuildManifestEntry> buildManifestEntries = new List<BuildManifestEntry>();
            buildManifestEntries.Add(new BuildManifestEntry("/a/b", ContentHash.Random(), X("/x/y/z"), FileArtifact.Invalid));
            buildManifestEntries.Add(new BuildManifestEntry("/a/c", ContentHash.Random(), X("/w/x/y/z"), FileArtifact.Invalid));

            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (RegisterFilesForBuildManifestCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.AreEqual(dropName, cmd.DropName);
                XAssert.ArrayEqual(buildManifestEntries.ToArray(), cmd.BuildManifestEntries);
                return IpcResult.Success(cmd.RenderResult(new BuildManifestEntry[0]));
            });

            var maybeResult = await apiClient.RegisterFilesForBuildManifest(dropName, buildManifestEntries.ToArray());
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.AreEqual(0, maybeResult.Result.Length);
        }

        [Fact]
        public async Task TestRegisterFilesForBuildManifestFailureAsync()
        {
            string dropName = "DropName";

            List<BuildManifestEntry> buildManifestEntries = new List<BuildManifestEntry>();
            buildManifestEntries.Add(new BuildManifestEntry("/a/b", ContentHash.Random(), X("/x/y/z"), FileArtifact.Invalid));

            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (RegisterFilesForBuildManifestCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.AreEqual(dropName, cmd.DropName);
                XAssert.ArrayEqual(buildManifestEntries.ToArray(), cmd.BuildManifestEntries);
                return IpcResult.Success(cmd.RenderResult(buildManifestEntries.ToArray()));
            });

            var maybeResult = await apiClient.RegisterFilesForBuildManifest(dropName, buildManifestEntries.ToArray());
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.ArrayEqual(buildManifestEntries.ToArray(), maybeResult.Result);
        }

        [Fact]
        public async Task TestRecomputeContentHashCommandAsync()
        {
            string fullPath = X("/a/b");
            string hashType = "vso";
            RecomputeContentHashEntry recomputehashEntry = new RecomputeContentHashEntry(fullPath, ContentHash.Random());

            ContentHash expectedHash = ContentHash.Random();
            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (RecomputeContentHashCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.AreEqual(hashType, cmd.RequestedHashType);
                XAssert.IsTrue(recomputehashEntry.Hash.Equals(cmd.Entry.Hash));
                XAssert.AreEqual(recomputehashEntry.FullPath, cmd.Entry.FullPath);
                return IpcResult.Success(cmd.RenderResult(new RecomputeContentHashEntry(fullPath, expectedHash)));
            });

            var maybeResult = await apiClient.RecomputeContentHashFiles(FileArtifact.Invalid, hashType, recomputehashEntry);
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.IsTrue(expectedHash.Equals(maybeResult.Result.Hash));
            XAssert.AreEqual(fullPath, maybeResult.Result.FullPath);
        }

        [Fact]
        public async Task TestHashContentStreamAsync()
        {
            StreamWithLength stream = new MemoryStream(Encoding.UTF8.GetBytes("SampleString")).AssertHasLength();

            ContentHash shaHash = await ContentHashingUtilities.HashContentStreamAsync(stream, HashType.SHA256);
            ContentHash vsoHash = await ContentHashingUtilities.HashContentStreamAsync(stream, HashType.Vso0);

            XAssert.AreEqual(shaHash.HashType, HashType.SHA256);
            XAssert.AreEqual(vsoHash.HashType, HashType.Vso0);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        public async Task TestGenerateBuildManifestDataAsync(int count)
        {
            string dropName = "DropName";
            List<BuildManifestFileInfo> expectedData = new List<BuildManifestFileInfo>();

            for (int i = 0; i < count; i++)
            {
                expectedData.Add(new BuildManifestFileInfo($"/path/to/{i}", new ContentHash(HashType.Vso0), new[] { new ContentHash(HashType.SHA1) }));
            }

            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (GenerateBuildManifestFileListCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.AreEqual(dropName, cmd.DropName);
                return IpcResult.Success(cmd.RenderResult(GenerateBuildManifestFileListResult.CreateForSuccess(expectedData)));
            });

            var maybeResult = await apiClient.GenerateBuildManifestFileList(dropName);
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.AreEqual(GenerateBuildManifestFileListResult.OperationStatus.Success, maybeResult.Result.Status);
            XAssert.IsTrue(expectedData.SequenceEqual(maybeResult.Result.FileList));
        }

        [Fact]
        public void TestBuildManifestFileInfoParsing()
        {
            BuildManifestFileInfo info1 = new BuildManifestFileInfo("/path/a", new ContentHash(HashType.Vso0), new[] { new ContentHash(HashType.SHA1), new ContentHash(HashType.SHA256) });
            BuildManifestFileInfo info2 = new BuildManifestFileInfo("/path/x", new ContentHash(HashType.Vso0), new[] { new ContentHash(HashType.SHA1) });

            string str1 = info1.ToString();
            string str2 = info2.ToString();

            XAssert.IsTrue(BuildManifestFileInfo.TryParse(str1, out BuildManifestFileInfo parsedInfo1));
            XAssert.IsTrue(BuildManifestFileInfo.TryParse(str2, out BuildManifestFileInfo parsedInfo2));

            XAssert.AreEqual(info1, parsedInfo1);
            XAssert.AreEqual(info2, parsedInfo2);

            XAssert.IsFalse(BuildManifestFileInfo.TryParse("123|VSO:123|SHA1:123", out _));
            XAssert.IsFalse(BuildManifestFileInfo.TryParse("123|VSO:123|SHA1:123|", out _));
            XAssert.IsFalse(BuildManifestFileInfo.TryParse("123|VSO:123", out _));
            XAssert.IsFalse(BuildManifestFileInfo.TryParse("123", out _));
            XAssert.IsFalse(BuildManifestFileInfo.TryParse("|123", out _));
        }

        [Fact]
        public void TestGenerateBuildManifestFileListResultRenderRoundTrip()
        {
            var files = new List<BuildManifestFileInfo>();

            // OperationStatus.Success, empty list
            var expected = GenerateBuildManifestFileListResult.CreateForSuccess(files);
            var success = GenerateBuildManifestFileListResult.TryParse(expected.Render(), out var actual);
            XAssert.IsTrue(success);
            XAssert.IsTrue(resultsAreEqual(expected, actual));

            // OperationStatus.Success, non-empty list
            files.Add(new BuildManifestFileInfo("path", new ContentHash(HashType.Vso0), new[] { new ContentHash(HashType.SHA1) }));
            expected = GenerateBuildManifestFileListResult.CreateForSuccess(files);
            success = GenerateBuildManifestFileListResult.TryParse(expected.Render(), out actual);
            XAssert.IsTrue(success);
            XAssert.IsTrue(resultsAreEqual(expected, actual));

            expected = GenerateBuildManifestFileListResult.CreateForFailure(GenerateBuildManifestFileListResult.OperationStatus.InternalError, "error");
            success = GenerateBuildManifestFileListResult.TryParse(expected.Render(), out actual);
            XAssert.IsTrue(success);
            XAssert.IsTrue(resultsAreEqual(expected, actual));

            expected = GenerateBuildManifestFileListResult.CreateForFailure(GenerateBuildManifestFileListResult.OperationStatus.UserError, $"multi{Environment.NewLine}line{Environment.NewLine}error");
            success = GenerateBuildManifestFileListResult.TryParse(expected.Render(), out actual);
            XAssert.IsTrue(success);
            XAssert.IsTrue(resultsAreEqual(expected, actual));

            static bool resultsAreEqual(GenerateBuildManifestFileListResult l, GenerateBuildManifestFileListResult r)
            {
                return l.Status == r.Status
                    && l.Error == r.Error
                    && (l.FileList != null && r.FileList != null && l.FileList.SequenceEqual(r.FileList)
                        || l.FileList == null && r.FileList == null);
            }
        }

        [Fact]
        public void TestGenerateBuildManifestFileListCommandParsingRejectsMalformedString()
        {
            GenerateBuildManifestFileListCommand cmd = new GenerateBuildManifestFileListCommand("dropName");

            XAssert.IsFalse(cmd.TryParseResult("NaN", out _));

            using var stringBuilderPoolInstance = Pools.StringBuilderPool.GetInstance();
            var sb = stringBuilderPoolInstance.Instance;

            sb.AppendLine($"255"); // a value that is a not a valid OperationStatus
            sb.AppendLine($"0");
            XAssert.IsFalse(cmd.TryParseResult(sb.ToString(), out _));

            sb.Clear();
            sb.AppendLine($"{(byte)GenerateBuildManifestFileListResult.OperationStatus.Success}");
            sb.AppendLine($"1");
            sb.AppendLine($"invalid|count");
            XAssert.IsFalse(cmd.TryParseResult(sb.ToString(), out _));
        }

        private bool ThrowsException(Action action)
        {
            try
            {
                action();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return true;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            return false;
        }

        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { 0, 1, 10 },
            new object[] { true, false })]
        public async Task TestReportStatisticsAsync(int numStats, bool expectedResult)
        {
            var stats = Enumerable
                .Range(1, numStats)
                .ToDictionary(i => $"key{i}", i => (long)new Random().Next());
            using var apiClient = CreateApiClient(ipcOperation =>
            {
                var cmd = (ReportStatisticsCommand)Command.Deserialize(ipcOperation.Payload);
                XAssert.SetEqual(stats, cmd.Stats);
                return IpcResult.Success(cmd.RenderResult(expectedResult));
            });
            var maybeResult = await apiClient.ReportStatistics(stats);
            XAssert.PossiblySucceeded(maybeResult);
            XAssert.AreEqual(expectedResult, maybeResult.Result);
        }

        [Fact]
        public void TestDeserializationOfUnknownCommand()
        {
            var cmd = new UnknownCommand();
            string str = Command.Serialize(cmd);
            var exception = Assert.Throws<ArgumentException>(() => Command.Deserialize(str));
            XAssert.Contains(exception.Message, cmd.TypeName);
        }

        [Fact]
        public async Task TestClientWhenCommandFailsAsync()
        {
            var errorPayload = "operation failed";
            using var apiClient = CreateApiClient(ipcOperation =>
            {
                return new IpcResult(IpcResultStatus.GenericError, errorPayload);
            });
            var maybeResult = await apiClient.LogMessage("hi");
            XAssert.IsFalse(maybeResult.Succeeded);
            XAssert.Contains(maybeResult.Failure.DescribeIncludingInnerFailures(), errorPayload);
        }

        [Fact]
        public async Task TestClientWhenCommandThrowsAsync()
        {
            var exceptionMessage = "invalid operation";
            using var apiClient = CreateApiClient(ipcOperation =>
            {
                throw new InvalidOperationException(exceptionMessage);
            });
            var maybeResult = await apiClient.LogMessage("hi");
            XAssert.IsFalse(maybeResult.Succeeded);
            XAssert.Contains(maybeResult.Failure.DescribeIncludingInnerFailures(), exceptionMessage);
        }

        [Fact]
        public async Task TestClientWhenCommandReturnsBogusValueAsync()
        {
            var bogusPayload = "bogus payload";
            using var apiClient = CreateApiClient(ipcOperation =>
            {
                return new IpcResult(IpcResultStatus.Success, bogusPayload);
            });
            var maybeResult = await apiClient.LogMessage("hi");
            XAssert.IsFalse(maybeResult.Succeeded);
            XAssert.Contains(maybeResult.Failure.DescribeIncludingInnerFailures(), bogusPayload, Client.ErrorCannotParseIpcResultMessage);
        }

        [Fact]
        public void TestClientFactory()
        {
            var provider = IpcFactory.GetProvider();
            var moniker = IpcMoniker.CreateNew();
            using var apiClient = Client.Create(provider, provider.RenderConnectionString(moniker));
        }

        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { -1, 0, 1 },
            new object[] { 0, 1, 10 })]
        public void TestValidFileId(int pathId, int rewriteCount)
        {
            var file = new FileArtifact(new AbsolutePath(pathId), rewriteCount);
            var file2 = FileId.Parse(FileId.ToString(file));
            XAssert.AreEqual(file, file2);
        }

        // FileId :: <path-id>:<rewrite-count>
        [Theory]
        [InlineData("")]             // too few separators
        [InlineData(":::")]          // too many separators
        [InlineData("not-an-int:1")] // first field not an int
        [InlineData("1:not-an-int")] // second field not an int
        public void TestInvalidFileId(string fileIdStr)
        {
            XAssert.IsFalse(FileId.TryParse(fileIdStr, out _));
            Assert.Throws<ArgumentException>(() => FileId.Parse(fileIdStr));
        }

        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { -1, 0, 1 },
            new object[] { 0, 1, 10 },
            new object[] { true, false })]
        public void TestValidDirectoryId(int pathId, uint partialSealId, bool isSharedOpaque)
        {
            // skip invalid input
            if (partialSealId == 0 && isSharedOpaque)
            {
                return;
            }

            var dir = new DirectoryArtifact(new AbsolutePath(pathId), partialSealId, isSharedOpaque);
            var dir2 = DirectoryId.Parse(DirectoryId.ToString(dir));
            XAssert.AreEqual(dir, dir2);
        }

        // DirectoryId :: <path-id>:<is-shared-opaque>:<partial-sealed-id>
        [Theory]
        [InlineData("")]               // too few separators
        [InlineData(":::")]            // too many separators
        [InlineData("not-an-int:1:1")] // first field not an int
        [InlineData("1:not-an-int:1")] // second field not an int
        [InlineData("1:1:not-an-int")] // third field not an int
        public void TestInvalidDirectoryId(string dirIdStr)
        {
            XAssert.IsFalse(DirectoryId.TryParse(dirIdStr, out _));
            Assert.Throws<ArgumentException>(() => DirectoryId.Parse(dirIdStr));
        }

        // SealedDirectoryFile :: <path>|<fileId>|<file-content-info>
        [Theory]
        [InlineData("")]             // too few separators
        [InlineData("|||||")]        // too many separators
        [InlineData("|1:1|8457")]    // path is empty
        [InlineData("a||8457")]      // fileId is empty
        [InlineData("a|bogus|8457")] // bogus file id
        [InlineData("a|1:1|")]       // file content info is empty
        [InlineData("a|1:1|bogus")]  // bogus file content info
        public void TestInvalidSealedDirectoryFile(string str)
        {
            XAssert.IsFalse(SealedDirectoryFile.TryParse(str, out _));
        }

        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { "", @"payload 123123 #$%^[]{}<>/\;:""'?,." + "\0\r\n\t" },
            new object[] { true, false })]
        public async Task TestIpcOperationSerializeAsync(string payload, bool waitForAck)
        {
            var ipcOp = new IpcOperation(payload, waitForAck);

            // serialize
            using var stream = new MemoryStream();
            await ipcOp.SerializeAsync(stream, CancellationToken.None);

            // reset stream position and deserialize
            stream.Position = 0;
            var ipcOpClone = await IpcOperation.DeserializeAsync(stream, CancellationToken.None);

            // compare
            var errMessage = $"Cloning failed:\n - original: {ipcOp}\n - clone: {ipcOpClone}";
            XAssert.AreEqual(payload, ipcOpClone.Payload, errMessage);
            XAssert.AreEqual(waitForAck, ipcOpClone.ShouldWaitForServerAck, errMessage);
        }

        [Theory]
        [MemberData(nameof(CrossProduct),
            new object[] { IpcResultStatus.Success, IpcResultStatus.ConnectionError, IpcResultStatus.ExecutionError },
            new object[] { "", @"payload 123123 #$%^[]{}<>/\;:""'?,." + "\0\r\n\t" })]
        public async Task TestIpcResultSerializationAsync(IpcResultStatus status, string payload)
        {
            var duration = TimeSpan.FromMilliseconds(1234);
            var ipcResult = new IpcResult(status, payload, duration);

            // serialize
            using var stream = new MemoryStream();
            await ipcResult.SerializeAsync(stream, CancellationToken.None);

            // reset stream position and deserialize
            stream.Position = 0;
            var ipcResultClone = await IpcResult.DeserializeAsync(stream, CancellationToken.None);

            // compare
            var errMessage = $"Cloning failed:\n - original: {ipcResult}\n - clone: {ipcResultClone}";
            XAssert.AreEqual(status, ipcResultClone.ExitCode);
            XAssert.AreEqual(payload, ipcResultClone.Payload, errMessage);
            XAssert.AreEqual(duration, ipcResultClone.ActionDuration, errMessage);
            XAssert.AreNotEqual(ipcResult.Succeeded, ipcResult.Failed);
        }

        [Theory]
        // success + success --> success
        [InlineData(IpcResultStatus.Success, IpcResultStatus.Success, IpcResultStatus.Success)]
        // success + error --> error
        [InlineData(IpcResultStatus.Success, IpcResultStatus.ConnectionError, IpcResultStatus.ConnectionError)]
        // error + success --> error
        [InlineData(IpcResultStatus.InvalidInput, IpcResultStatus.Success, IpcResultStatus.InvalidInput)]
        // error + error --> GenericError
        [InlineData(IpcResultStatus.TransmissionError, IpcResultStatus.ExecutionError, IpcResultStatus.GenericError)]
        // two errors of the same kind --> error
        [InlineData(IpcResultStatus.InvalidInput, IpcResultStatus.InvalidInput, IpcResultStatus.InvalidInput)]
        public void TestIpcResultMerge(IpcResultStatus lhsStatus, IpcResultStatus rhsStatus, IpcResultStatus mergeStatus)
        {
            var lhs = new IpcResult(lhsStatus, "lhs");
            var rhs = new IpcResult(rhsStatus, "rhs");
            var merged = IpcResult.Merge(lhs, rhs);
            // contains both payloads
            XAssert.Contains(merged.Payload, lhs.Payload, rhs.Payload);
            // has correct status
            XAssert.AreEqual(merged.ExitCode, mergeStatus);
        }

        private class UnknownCommand : Command
        {
            internal string TypeName => nameof(UnknownCommand);

            internal override void InternalSerialize(BinaryWriter writer)
            {
                writer.Write(TypeName);
            }
        }

        private class MockClient : IClient
        {
            public Task Completion => Task.CompletedTask;

            public IClientConfig Config { get; set; } = new ClientConfig();
            public Func<IIpcOperation, IIpcResult> SendFn { get; set; }

            public void Dispose() { }

            public void RequestStop() { }

            Task<IIpcResult> IClient.Send(IIpcOperation operation)
            {
                Contract.Requires(operation != null);
                return Task.FromResult(SendFn(operation));
            }
        }

        private Client CreateApiClient(Func<IIpcOperation, IIpcResult> handler)
        {
            return new Client(new MockClient
            {
                SendFn = handler
            });
        }

        private AbsolutePath AbsPath(string path) => AbsolutePath.Create(PathTable, path);
        private FileArtifact SourceFile(string path) => FileArtifact.CreateSourceFile(AbsolutePath.Create(PathTable, path));
        private FileArtifact OutputFile(string path) => FileArtifact.CreateOutputFile(AbsolutePath.Create(PathTable, path));
        private static FileContentInfo RandomFileContentInfo()
        {
            var contentHash = ContentHash.Random();
            return new FileContentInfo(contentHash, contentHash.Length);
        }
    }
}
