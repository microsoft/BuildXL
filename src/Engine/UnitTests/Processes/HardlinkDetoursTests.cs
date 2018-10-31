// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Tests for reporting of hardlink creation from Detours
    /// </summary>
    public class HardlinkDetoursTests : RemoteApiDetoursTestBase
    {
        [Fact]
        public async Task HardlinkCreationDeniedWithoutAllowedWrite()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, "D");
            AbsolutePath srcPath = WriteEmptyFile(pathTable, @"D\Origin");
            AbsolutePath targetPath = GetFullPath(pathTable, @"D\Target");

            AssertFileExists(@"D\Origin");
            AssertFileDoesNotExist(@"D\Target");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadAlways);
                },
                CreateHardlink(@"D\Origin", @"D\Target"));

            VerifyReportedAccesses(
                pathTable,
                result.AllUnexpectedFileAccesses,
                allowExtraEnumerations: false,
                expected: ExpectReport(ReportedFileOperation.CreateHardLinkDestination, RequestedAccess.Write, targetPath, FileAccessStatus.Denied));

            AssertFileExists(@"D\Origin");
            AssertFileExists(@"D\Target");
        }

        [Fact]
        public async Task HardlinkCreationIsAllowedWithAllowedWriteAndRead()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, "D");
            AbsolutePath srcPath = WriteEmptyFile(pathTable, @"D\Origin");
            AbsolutePath targetPath = GetFullPath(pathTable, @"D\Target");

            AssertFileExists(@"D\Origin");
            AssertFileDoesNotExist(@"D\Target");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);
                    manifest.AddPath(srcPath, values: FileAccessPolicy.AllowRead, mask: FileAccessPolicy.MaskNothing);
                    manifest.AddScope(targetPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowWrite);
                },
                CreateHardlink(@"D\Origin", @"D\Target"));

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: new[]
                          {
                             ExpectReport(ReportedFileOperation.CreateHardLinkSource, RequestedAccess.Read, srcPath, FileAccessStatus.Allowed),
                             ExpectReport(ReportedFileOperation.CreateHardLinkDestination, RequestedAccess.Write, targetPath, FileAccessStatus.Allowed)
                          });

            AssertFileExists(@"D\Origin");
            AssertFileExists(@"D\Target");
        }
    }
}
