// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable AsyncFixer02

namespace Test.BuildXL.Processes
{
    [Trait("Category", "FileAccessExplicitReportingTest")]
    public sealed class FileAccessExplicitReportingTest : SandboxedProcessTestBase
    {
        public FileAccessExplicitReportingTest(ITestOutputHelper output) : base(output)
        {
        }

        public static IEnumerable<object[]> AccessTypes()
        {
            yield return new object[] { AccessType.Read };
            yield return new object[] { AccessType.Probe };
        }

        [Theory]
        [MemberData(nameof(AccessTypes))]
        public Task DirectlyReported(AccessType accessType)
        {
            var file = CreateSourceFile();
            return VerifyReporting(
                accessType,
                (manifest) => manifest.AddPath(file, values: FileAccessPolicy.AllowRead | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing),
                ExpectAccess(file));
        }

        [Theory]
        [MemberData(nameof(AccessTypes))]
        [Trait("Category", "WindowsOSOnly")]
        public Task DirectlyReportedCaseInsensitive(AccessType accessType)
        {
            var file = CreateSourceFile();
            return VerifyReporting(
                accessType,
                (manifest) => manifest.AddPath(ToUpper(file), values: FileAccessPolicy.AllowRead | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing),
                ExpectAccess(file));
        }

        [Theory]
        [MemberData(nameof(AccessTypes))]
        [Trait("Category", "WindowsOSOnly")]
        public Task DirectlyReportedUnicodeCaseInsensitive(AccessType accessType)
        {
            // everything should work the same for files with Unicode characters in their name
            // (this test runs on Windows only (because of ToUpper);
            var file = CreateSourceFileWithPrefix(prefix: "àßγıïϓϔẛ");
            return VerifyReporting(
                accessType,
                (manifest) => manifest.AddPath(ToUpper(file), values: FileAccessPolicy.AllowRead | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing),
                ExpectAccess(file));
        }

        [Theory]
        [MemberData(nameof(AccessTypes))]
        public Task DirectlyReportedExistentFileFailsWithAllowReadIfNonExistent(AccessType accessType)
        {
            var file = CreateSourceFile();
            return VerifyReporting(
                accessType,
                (manifest) => manifest.AddPath(file, values: FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing),
                ExpectDeniedAccess(file));
        }

        [Theory]
        [MemberData(nameof(AccessTypes))]
        public Task DirectlyReportedNonexistent(AccessType accessType)
        {
            var file = AbsentFile(CreateUniqueSourcePath());
            return VerifyReporting(
                accessType,
                (manifest) => manifest.AddPath(file, values: FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing),
                ExpectAccess(file, exists: false));
        }

        // TODO 1519677: Fix this bug on Mojave macOS
        [Theory]
        [MemberData(nameof(AccessTypes))]
        public Task DirectlyReportedNonexistentFailsWithAllowRead(AccessType accessType)
        {
            var file = FileArtifact.CreateSourceFile(CreateUniqueSourcePath());
            return VerifyReporting(
                accessType,
                (manifest) => manifest.AddPath(file, values: FileAccessPolicy.AllowRead | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing),
                ExpectDeniedAccess(file, exists: false));
        }

        // TODO: fix this bug on Mojave macOS
        [Theory]
        [MemberData(nameof(AccessTypes))]
        public Task ProbesWithinScope(AccessType accessType)
        {
            var pt = Context.PathTable;
            var xDir = CreateUniqueDirectory(prefix: "x");
            var yDir = CreateUniqueDirectory(root: xDir, prefix: "y");
            var readFile = CreateSourceFile(root: yDir, prefix: "read");

            var tarpit = Combine(yDir, "tarpit");

            return VerifyReporting(
                accessType,
                (manifest) =>
                {
                    manifest.AddScope(yDir, FileAccessPolicy.MaskNothing, FileAccessPolicy.AllowReadIfNonexistent | FileAccessPolicy.ReportAccess); // Report and allow failed probes
                    manifest.AddScope(tarpit, FileAccessPolicy.ReportAccess, FileAccessPolicy.ReportAccess); // Nested under x\y; stop allowing failed probes.
                    manifest.AddScope(xDir, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess); // Report, but don't allow failed probes.
                    manifest.AddPath(readFile, values: FileAccessPolicy.AllowRead | FileAccessPolicy.ReportAccess, mask: FileAccessPolicy.MaskNothing); // Under x\y\, so a failed probe is already allowed - but it exists.
                },
                ExpectAccess(readFile),
                ExpectAccess(AbsentFile(yDir, "probe1"), exists: false),
                ExpectAccess(AbsentFile(yDir, "probe2"), exists: false),
                ExpectDeniedAccess(AbsentFile(xDir, "abc"), exists: false));
        }

        private FileArtifact ToUpper(FileArtifact file)
        {
            var pt = Context.PathTable;
            return AbsentFile(AbsolutePath.Create(pt, file.Path.ToString(pt).ToUpperInvariant()));
        }

        public enum AccessType
        {
            Read,
            Probe
        }

        private struct ExpectedReportEntry
        {
            public bool Allowed;
            public bool Exists;
            public FileArtifact File;
        }

        private async Task VerifyReporting(AccessType access, Action<FileAccessManifest> populateManifest, params ExpectedReportEntry[] expectedExplicitReports)
        {
            // We need a process which will generate the expected accesses.
            var testProcess = CreateTestProcess(access, expectedExplicitReports);
            var pathTable = Context.PathTable;

            var info = ToProcessInfo(testProcess, "FileAccessExplicitReportingTest");
            info.FileAccessManifest.ReportFileAccesses = false;
            info.FileAccessManifest.ReportUnexpectedFileAccesses = true;
            info.FileAccessManifest.FailUnexpectedFileAccesses = false;

            populateManifest(info.FileAccessManifest);

            using (ISandboxedProcess process = await StartProcessAsync(info))
            {
                SandboxedProcessResult result = await process.GetResultAsync();

                XAssert.AreEqual(0, result.ExitCode,
                    "\r\ncmd: {0} \r\nStandard out: '{1}' \r\nStandard err: '{2}'.",
                    info.Arguments,
                    await result.StandardOutput.ReadValueAsync(),
                    await result.StandardError.ReadValueAsync());
                XAssert.IsNotNull(result.ExplicitlyReportedFileAccesses);

                Dictionary<AbsolutePath, ExpectedReportEntry> pathsToExpectations = expectedExplicitReports.ToDictionary(
                    e => e.File.Path,
                    e => e);

                var verifiedPaths = new HashSet<AbsolutePath>();
                foreach (var actualReport in result.ExplicitlyReportedFileAccesses)
                {
                    string actualReportedPathString = actualReport.GetPath(pathTable);
                    XAssert.AreEqual(
                        FileAccessStatus.Allowed,
                        actualReport.Status,
                        "Incorrect status for path " + actualReportedPathString);

                    if (!TryVerifySingleReport(pathTable, actualReport, access, pathsToExpectations, out var actualReportPath))
                    {
                        if ((actualReport.RequestedAccess & RequestedAccess.Enumerate) != 0)
                        {
                            // To account for 'explicitly reported' enumerations globally, we need to be lenient about unexpected enumerations.
                            // Alternatively instead opt-in to enumeration reports under certain scopes.
                        }
                        else
                        {
                            AbsolutePath actualReportedPath = AbsolutePath.Create(pathTable, actualReportedPathString);
                            XAssert.Fail("No expectations for an explicitly reported path {0}", actualReportedPath.ToString(pathTable));
                        }
                    }
                    else
                    {
                        verifiedPaths.Add(actualReportPath);
                    }
                }

                foreach (var actualReport in result.AllUnexpectedFileAccesses)
                {
                    XAssert.AreEqual(FileAccessStatus.Denied, actualReport.Status);
                    if (TryVerifySingleReport(pathTable, actualReport, access, pathsToExpectations, out var actualReportPath))
                    {
                        verifiedPaths.Add(actualReportPath);
                    }

                    // Note that we allow extra unexpected file accesses for the purposes of these tests.
                }

                var expectedPathsSet = new HashSet<AbsolutePath>(pathsToExpectations.Keys);
                var disagreeingPaths = new HashSet<AbsolutePath>(verifiedPaths);
                disagreeingPaths.SymmetricExceptWith(expectedPathsSet);
                if (disagreeingPaths.Any())
                {
                    var disagreeingReports = "Disagreeing reports:" + string.Join(string.Empty, disagreeingPaths
                        .Select(p => (tag: expectedPathsSet.Contains(p) ? "Missing" : "Unexpected", path: p))
                        .Select(t => $"{Environment.NewLine}  {t.tag} report for path {t.path.ToString(pathTable)}"));
                    var expectedReports = "Expected reports:" + string.Join(string.Empty, pathsToExpectations.Keys
                        .Select(p => $"{Environment.NewLine}  {p.ToString(pathTable)}"));
                    var verifiedReports = "Verified reports:" + string.Join(string.Empty, verifiedPaths
                        .Select(p => $"{Environment.NewLine}  {p.ToString(pathTable)}"));
                    XAssert.Fail(string.Join(Environment.NewLine, disagreeingReports, expectedReports, verifiedReports));
                }
            }
        }

        private static bool TryVerifySingleReport(PathTable pathTable, ReportedFileAccess actualReport, AccessType access, Dictionary<AbsolutePath, ExpectedReportEntry> pathsToExpectations, out AbsolutePath actualReportedPath)
        {
            string actualReportedPathString = actualReport.GetPath(pathTable);
            if (!AbsolutePath.TryCreate(pathTable, actualReportedPathString, out actualReportedPath))
            {
                return false;
            }

            ExpectedReportEntry expected;
            if (!pathsToExpectations.TryGetValue(actualReportedPath, out expected))
            {
                return false;
            }

            XAssert.AreEqual(expected.Exists, !actualReport.IsNonexistent, "Bad assumption on file existence of {0}; this can break ACL decisions", actualReportedPathString);
            XAssert.AreEqual(
                expected.Allowed ? FileAccessStatus.Allowed : FileAccessStatus.Denied,
                actualReport.Status,
                "Incorrect ACL decision for " + actualReportedPathString);

            // on MacOS we cannot distinguish Read vs. Probe for absent files so we always report Probe in those cases
            var expectedAccess = OperatingSystemHelper.IsUnixOS && actualReport.IsNonexistent
                ? RequestedAccess.Probe
                : access == AccessType.Read ? RequestedAccess.Read : RequestedAccess.Probe;
            XAssert.AreEqual(
                expectedAccess,
                actualReport.RequestedAccess,
                "Incorrect access type for path " + actualReportedPathString);

            if (access == AccessType.Read)
            {
                // on macOS operations have different names, hence Unknown
                var expectedOperation = !OperatingSystemHelper.IsUnixOS
                    ? ReportedFileOperation.CreateFile
                    : expected.Exists ? ReportedFileOperation.KAuthVNodeRead : ReportedFileOperation.MacLookup;
                XAssert.AreEqual(expectedOperation, actualReport.Operation, "Wrong operation for {0}", actualReportedPathString);
            }

            return true;
        }

        private Process CreateTestProcess(AccessType type, IEnumerable<ExpectedReportEntry> expectedExplicitReports)
        {
            XAssert.IsTrue(type == AccessType.Probe || type == AccessType.Read, "Unsupported access type: " + type);

            var operations = expectedExplicitReports
                .Select(report => type == AccessType.Probe
                    ? Operation.Probe(report.File)
                    : Operation.ReadFile(report.File))
                .ToArray();

            return ToProcess(operations);
        }

        private static ExpectedReportEntry ExpectReport(FileArtifact file, bool allowed, bool exists)
            => new ExpectedReportEntry
            {
                Allowed = allowed,
                Exists = exists,
                File = file
            };

        private static ExpectedReportEntry ExpectAccess(FileArtifact file, bool exists = true)
            => ExpectReport(file, allowed: true, exists: exists);

        private static ExpectedReportEntry ExpectDeniedAccess(FileArtifact file, bool exists = true)
            => ExpectReport(file, allowed: false, exists: exists);
    }
}
