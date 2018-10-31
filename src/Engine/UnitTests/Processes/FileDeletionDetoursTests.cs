// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Tests for reporting of file deletions from detours.
    /// </summary>
    public class FileDeletionDetoursTests : RemoteApiDetoursTestBase
    {
        [Fact]
        public async Task DeleteViaNtCreateFileIsDeniedWithoutAllowedWrite()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, "D");
            AbsolutePath filePath = WriteEmptyFile(pathTable, @"D\FileToDelete");

            AssertFileExists(@"D\FileToDelete");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadAlways);
                },
                DeleteViaNtCreateFile(@"D\FileToDelete"));

            VerifyReportedAccesses(
                pathTable,
                result.AllUnexpectedFileAccesses,
                allowExtraEnumerations: false,
                expected: ExpectReport(ReportedFileOperation.NtCreateFile, RequestedAccess.Write, filePath, FileAccessStatus.Denied));

            AssertFileDoesNotExist(@"D\FileToDelete");
        }

        [Fact]
        public async Task DeleteViaNtCreateFileIsAllowedWithAllowedWrite()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, "D");
            AbsolutePath filePath = WriteEmptyFile(pathTable, @"D\FileToDelete");

            AssertFileExists(@"D\FileToDelete");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowWrite | FileAccessPolicy.ReportAccess);
                },
                DeleteViaNtCreateFile(@"D\FileToDelete"));

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: ExpectReport(ReportedFileOperation.NtCreateFile, RequestedAccess.Write, filePath, FileAccessStatus.Allowed));

            AssertFileDoesNotExist(@"D\FileToDelete");
        }
    }
}
