// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Tests for reporting of directory enumeration (<see cref="RequestedAccess.Enumerate" />) from Detours.
    /// </summary>
    public class DirectoryEnumerationDetoursTests : RemoteApiDetoursTestBase
    {
        #region FindFirstFile

        [Fact]
        public async Task EnumerateEmptyDirectoryWithFindFirstFileAndExplicitReport()
        {
            var pathTable = new PathTable();

            AbsolutePath emptyDirPath = CreateDirectory(pathTable, @"emptyDir");
            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(emptyDirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateWithFindFirstFileEx(@"emptyDir\*"));

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Enumerate, emptyDirPath));
        }

        [Fact]
        public async Task EnumerateEmptyDirectoryWithFindFirstFileAndExplicitReportAndNoMatches()
        {
            var pathTable = new PathTable();

            AbsolutePath emptyDirPath = CreateDirectory(pathTable, @"emptyDir");
            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(emptyDirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateWithFindFirstFileEx(@"emptyDir\xxx*")); // xxx* excludes the magic . and .. entries.

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Enumerate, emptyDirPath));
        }

        [Fact]
        public async Task EnumerateNonexistentDirectory()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath fakeDirPath = GetFullPath(pathTable, @"dir\fake");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateWithFindFirstFileEx(@"dir\fake\*")); // dir\fake doesn't exist so we should expect ERROR_PATH_NOT_FOUND (nonexistent directory to enumerate).

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Enumerate, fakeDirPath, exists: false));
        }

        /// <summary>
        /// Tests that enumerations (but not enumeration probes) are explicitly reported globally.
        /// TODO: This behavior should get more precise in the future; this test is to catch accidental regressions
        /// so long as we expect to see enumerations everywhere.
        /// </summary>
        [Fact]
        public async Task EnumerateEmptyDirectoryWithFindFirstFileAndNoExplicitReport()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath fileAPath = WriteEmptyFile(pathTable, @"dir\fileA");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest => { manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadAlways); },
                EnumerateWithFindFirstFileEx(@"dir\*"));

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,

                // Note that we do not get an EnumerationProbe for fileA since it is not under Report scope.
                expected: ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Enumerate, dirPath));
        }

        [Fact]
        public async Task EnumerateAndProbeWithFindFirstFileAndFindNextFile()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath fileAPath = WriteEmptyFile(pathTable, @"dir\fileA");
            AbsolutePath fileBPath = WriteEmptyFile(pathTable, @"dir\fileB");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateWithFindFirstFileEx(@"dir\*"));

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: new[]
                          {
                              // Note that we assume a deterministic order here; in truth we paper over possible orders in VerifyReportedAccess.
                              ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Enumerate, dirPath),
                              ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.EnumerationProbe, fileAPath),
                              ExpectReport(ReportedFileOperation.FindNextFile, RequestedAccess.EnumerationProbe, fileBPath),
                          });
        }

        [Fact]
        public async Task ProbeWithFindFirstFileSingle()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath fileAPath = WriteEmptyFile(pathTable, @"dir\fileA");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateWithFindFirstFileEx(@"dir\fileA")); // Note no wildcards; should get a probe but not enumeration.

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,

                // Note that there is no directory enumeration reported (this is really a single file probe),
                // and the report for fileA is correspondingly a Probe rather than EnumerationProbe.
                expected: ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Probe, fileAPath));
        }

        [Fact]
        public async Task ProbeWithFindFirstFileWhereSearchPathIsFileAndReadAllowedByScope()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath filePath = WriteEmptyFile(pathTable, @"dir\file");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateWithFindFirstFileEx(@"dir\file\*")); // Note that we are trying to wildcard udner a file.

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,

                // Note that there is no directory enumeration reported (this is really a single file probe).
                // Note that the probe path is dir\file despite querying dir\file\*
                expected: ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Probe, filePath));
        }

        [Fact]
        public async Task ProbeWithFindFirstFileWhereSearchPathIsFileAndReadNotAllow()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath filePath = WriteEmptyFile(pathTable, @"dir\file");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);
                },
                EnumerateWithFindFirstFileEx(@"dir\file\*")); // Note that we are trying to wildcard udner a file.

            VerifyReportedAccesses(
                pathTable,
                result.AllUnexpectedFileAccesses,
                allowExtraEnumerations: false,

                // Note that there is no directory enumeration reported (this is really a single file probe).
                // Note that the probe path is dir\file despite querying dir\file\*
                expected: ExpectReport(ReportedFileOperation.FindFirstFileEx, RequestedAccess.Probe, filePath, status: FileAccessStatus.Denied));
        }

        #endregion

        #region NtQueryDirectoryFile

        [Fact]
        public async Task EnumerateEmptyDirectoryByHandleWithExplicitReport()
        {
            var pathTable = new PathTable();

            AbsolutePath emptyDirPath = CreateDirectory(pathTable, @"emptyDir");
            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(emptyDirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateFileOrDirectoryByHandle(@"emptyDir"));

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: new[]
                          {
                              // No CreateFile report, since emptyDir is a directory.
                              ExpectReport(ReportedFileOperation.NtQueryDirectoryFile, RequestedAccess.Enumerate, emptyDirPath),
                          });

            VerifyReportedAccesses(
                pathTable,
                result.AllUnexpectedFileAccesses,
                allowExtraEnumerations: false);
        }

        [Fact]
        public async Task EnumerateEmptyDirectoryByHandleWithoutExplicitReport()
        {
            var pathTable = new PathTable();

            AbsolutePath emptyDirPath = CreateDirectory(pathTable, @"emptyDir");
            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(emptyDirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadAlways);
                },
                EnumerateFileOrDirectoryByHandle(@"emptyDir"));

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,
                expected: new[]
                          {
                              // No CreateFile report, since emptyDir is a directory.
                              // Note that we get an explicit report for the enumeration, despite not adding ReportAccess (automatic for enumerations).
                              ExpectReport(ReportedFileOperation.NtQueryDirectoryFile, RequestedAccess.Enumerate, emptyDirPath),
                          });

            VerifyReportedAccesses(
                pathTable,
                result.AllUnexpectedFileAccesses,
                allowExtraEnumerations: false);
        }

        // Below we have some tests for opening a *file* and trying to enumerate it.
        [Fact]
        public async Task EnumerateFileByHandleWithReadAllowedByScopeAndExplicitReport()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath filePath = WriteEmptyFile(pathTable, @"dir\file");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess | FileAccessPolicy.AllowReadAlways);
                },
                EnumerateFileOrDirectoryByHandle(@"dir\file")); // Note that we are trying to enumerate a file

            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false,

                // Note that there is no directory enumeration reported (this is really a single file probe).
                expected: ExpectReport(ReportedFileOperation.CreateFile, RequestedAccess.Read, filePath));

            VerifyReportedAccesses(
                pathTable,
                result.AllUnexpectedFileAccesses,
                allowExtraEnumerations: false);
        }

        [Fact]
        public async Task EnumerateFileByHandleWithReadAllowedByScopeWithoutExplicitReport()
        {
            var pathTable = new PathTable();

            AbsolutePath dirPath = CreateDirectory(pathTable, @"dir");
            AbsolutePath filePath = WriteEmptyFile(pathTable, @"dir\file");

            SandboxedProcessResult result = await RunRemoteApiInSandboxAsync(
                pathTable,
                manifest =>
                {
                    manifest.AddScope(dirPath, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadAlways);
                },
                EnumerateFileOrDirectoryByHandle(@"dir\file")); // Note that we are trying to enumerate a file

            // We don't see any reports; the access is just a probe, and we didn't ask for explicit reports.
            // Note that this case is very important for enumerations like dir\file\* where dir\file is a static
            // (precise file) dependency (not part of a sealed directory) since we only expect reports for sealed
            // directory members.
            VerifyReportedAccesses(
                pathTable,
                result.ExplicitlyReportedFileAccesses,
                allowExtraEnumerations: false);

            VerifyReportedAccesses(
                pathTable,
                result.AllUnexpectedFileAccesses,
                allowExtraEnumerations: false);
        }

        #endregion
    }
}
