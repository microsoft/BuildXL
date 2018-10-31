// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Base class for tests that run a sanboxed <see cref="RemoteApi" /> process and verify reports from Detours.
    /// </summary>
    [Trait("Category", "WindowsOSOnly")]
    public abstract class RemoteApiDetoursTestBase : TemporaryStorageTestBase, ISandboxedProcessFileStorage
    {
        /// <summary>
        /// Creates a command to run in <see cref="RunRemoteApiInSandboxAsync" />
        /// </summary>
        protected static RemoteApi.Command Command(RemoteApi.CommandType commandType, string parameter)
        {
            return new RemoteApi.Command(commandType, parameter);
        }

        /// <summary>
        /// Creates a command to enumerate a path (with <c>FindFirstFileEx</c> and <c>FindNextFile</c>) in
        /// <see cref="RunRemoteApiInSandboxAsync" />.
        /// </summary>
        protected static RemoteApi.Command EnumerateWithFindFirstFileEx(string path)
        {
            return RemoteApi.Command.EnumerateWithFindFirstFileEx(path);
        }

        /// <summary>
        /// Creates a command to enumerate a path (with <c>NtQueryDirectoryFile</c>) in
        /// <see cref="RunRemoteApiInSandboxAsync" />.
        /// </summary>
        protected static RemoteApi.Command EnumerateFileOrDirectoryByHandle(string path)
        {
            return RemoteApi.Command.EnumerateFileOrDirectoryByHandle(path);
        }

        /// <summary>
        /// Creates a command to delete a path with NtCreateFile in
        /// <see cref="RunRemoteApiInSandboxAsync" />. The provided path is relative to <see cref="TemporaryStorageTestBase.TemporaryDirectory" />.
        /// </summary>
        protected RemoteApi.Command DeleteViaNtCreateFile(string relativePath)
        {
            return RemoteApi.Command.DeleteViaNtCreateFile(GetFullPath(relativePath));
        }

        /// <summary>
        /// Creates a command to create a new link <c>CreateHardLinkW</c> in
        /// <see cref="RunRemoteApiInSandboxAsync" />.
        /// </summary>
        protected RemoteApi.Command CreateHardlink(string existingFile, string newLink)
        {
            return RemoteApi.Command.CreateHardlink(existingFile, newLink);
        }

        /// <summary>
        /// Writes an empty file relative to <see cref="TemporaryStorageTestBase.TemporaryDirectory" />.
        /// </summary>
        protected string WriteEmptyFile(string filename)
        {
            WriteFile(filename, string.Empty);
            return GetFullPath(filename);
        }

        /// <summary>
        /// Writes an empty file relative to <see cref="TemporaryStorageTestBase.TemporaryDirectory" />.
        /// </summary>
        protected AbsolutePath WriteEmptyFile(PathTable pathTable, string filename)
        {
            WriteFile(filename, string.Empty);
            return GetFullPath(pathTable, filename);
        }

        /// <summary>
        /// Creates a directory relative to <see cref="TemporaryStorageTestBase.TemporaryDirectory" />.
        /// </summary>
        protected string CreateDirectory(string directoryRelPath)
        {
            string fullPath = GetFullPath(directoryRelPath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        /// <summary>
        /// Creates a directory relative to <see cref="TemporaryStorageTestBase.TemporaryDirectory" />.
        /// </summary>
        protected AbsolutePath CreateDirectory(PathTable pathTable, string directoryRelPath)
        {
            Directory.CreateDirectory(GetFullPath(directoryRelPath));
            return GetFullPath(pathTable, directoryRelPath);
        }

        /// <summary>
        /// Asserts that a file relative to <see cref="TemporaryStorageTestBase.TemporaryDirectory" /> exists.
        /// </summary>
        protected void AssertFileExists(string relPath)
        {
            string path = GetFullPath(relPath);
            XAssert.IsTrue(File.Exists(path), "Expected path {0} to exist as a file", path);
        }

        /// <summary>
        /// Asserts that a file relative to <see cref="TemporaryStorageTestBase.TemporaryDirectory" /> exists.
        /// </summary>
        protected void AssertFileDoesNotExist(string relPath)
        {
            string path = GetFullPath(relPath);
            XAssert.IsFalse(File.Exists(path), "Expected path {0} to be absent", path);
        }

        /// <summary>
        /// Runs a list of remote file APIs (e.g. <see cref="EnumerateWithFindFirstFileEx" />) in a Detours sandbox.
        /// Returns a <see cref="SandboxedProcessResult" /> containing reported accesses.
        /// </summary>
        protected Task<SandboxedProcessResult> RunRemoteApiInSandboxAsync(
            PathTable pathTable,
            Action<FileAccessManifest> populateManifest,
            params RemoteApi.Command[] commands)
        {
            return RemoteApi.RunInSandboxAsync(
                pathTable,
                workingDirectory: TemporaryDirectory,
                sandboxStorage: this,
                populateManifest: populateManifest,
                commands: commands);
        }

        /// <summary>
        /// Expected reported access from <see cref="RemoteApiDetoursTestBase.RunRemoteApiInSandboxAsync" />.
        /// This is a projection of key fields of <see cref="ReportedFileAccess" />.
        /// </summary>
        protected struct ExpectedReport
        {
            public AbsolutePath Path;
            public FileAccessStatus Status;
            public ReportedFileOperation Operation;
            public bool Exists;
            public RequestedAccess RequestedAccess;
        }

        /// <summary>
        /// Creates an expected reported access from <see cref="RemoteApiDetoursTestBase.RunRemoteApiInSandboxAsync" />.
        /// This is a projection of key fields of <see cref="ReportedFileAccess" />.
        /// </summary>
        protected ExpectedReport ExpectReport(
            ReportedFileOperation operation,
            RequestedAccess access,
            AbsolutePath path,
            FileAccessStatus status = FileAccessStatus.Allowed,
            bool exists = true)
        {
            return new ExpectedReport
                   {
                       Path = path,
                       Operation = operation,
                       RequestedAccess = access,
                       Status = status,
                       Exists = exists
                   };
        }

        /// <summary>
        /// Verifies that the given set of reported accesses matches those expected.
        /// This generates a failed assertion if there are extra or missing reports.
        /// </summary>
        protected void VerifyReportedAccesses(
            PathTable pathTable,
            IEnumerable<ReportedFileAccess> accesses,
            bool allowExtraEnumerations,
            params ExpectedReport[] expected)
        {
            var pathsToExpectations = new Dictionary<AbsolutePath, ExpectedReport>();
            foreach (ExpectedReport expectedReport in expected)
            {
                pathsToExpectations.Add(expectedReport.Path, expectedReport);
            }

            var verifiedPaths = new HashSet<AbsolutePath>();

            foreach (ReportedFileAccess actualReport in accesses)
            {
                AbsolutePath actualReportPath;
                if (!TryVerifySingleReport(pathTable, actualReport, pathsToExpectations, out actualReportPath))
                {
                    if (allowExtraEnumerations && (actualReport.RequestedAccess & RequestedAccess.Enumerate) != 0)
                    {
                        // To account for 'explicitly reported' enumerations globally, we need to be lenient about unexpected enumerations.
                        // Alternatively instead opt-in to enumeration reports under certain scopes.
                    }
                    else
                    {
                        string actualReportedPathString = actualReport.GetPath(pathTable);
                        XAssert.Fail("Unexpected report for path {0}: {1}", actualReportedPathString, actualReport.Describe());
                    }
                }
                else
                {
                    verifiedPaths.Add(actualReportPath);
                }
            }

            foreach (AbsolutePath expectedPath in pathsToExpectations.Keys)
            {
                if (!verifiedPaths.Contains(expectedPath))
                {
                    XAssert.Fail("Missing a report for path {0}", expectedPath.ToString(pathTable));
                }
            }
        }

        private static bool TryVerifySingleReport(
            PathTable pathTable,
            ReportedFileAccess actualReport,
            Dictionary<AbsolutePath, ExpectedReport> pathsToExpectations,
            out AbsolutePath actualReportedPath)
        {
            string actualReportedPathString = actualReport.GetPath(pathTable);
            if (!AbsolutePath.TryCreate(pathTable, actualReportedPathString, out actualReportedPath))
            {
                return false;
            }

            ExpectedReport expected;
            if (!pathsToExpectations.TryGetValue(actualReportedPath, out expected))
            {
                return false;
            }

            XAssert.AreEqual(
                expected.Exists,
                !actualReport.IsNonexistent,
                "Bad assumption on file existence of {0}; this can break ACL decisions",
                actualReportedPathString);
            XAssert.AreEqual(expected.Status, actualReport.Status, "Incorrect ACL decision for ", actualReportedPathString);
            XAssert.AreEqual(
                expected.RequestedAccess,
                actualReport.RequestedAccess,
                "Wrong access level for {0}; this can break enumeration handling and violation analysis",
                actualReportedPathString);

            if ((expected.Operation == ReportedFileOperation.FindFirstFileEx || expected.Operation == ReportedFileOperation.FindNextFile) &&
                expected.RequestedAccess == RequestedAccess.EnumerationProbe)
            {
                // We don't want to assume a particular enumeration order from the file system. So we make probes by FindFirstFile / FindNextFile interchangable.
                XAssert.IsTrue(
                    actualReport.Operation == ReportedFileOperation.FindFirstFileEx || actualReport.Operation == ReportedFileOperation.FindNextFile,
                    "Wrong operation ({0:G}) for {1}. Expected FindFirstFile / FindNextFile as a Probe.",
                    actualReport.Operation,
                    actualReportedPathString);
            }
            else
            {
                XAssert.AreEqual(expected.Operation, actualReport.Operation, "Wrong operation for {0}", actualReportedPathString);
            }

            return true;
        }

        string ISandboxedProcessFileStorage.GetFileName(SandboxedProcessFile file)
        {
            Contract.Assume(!string.IsNullOrEmpty(TemporaryDirectory), "TemporaryDirectory should have been set up (GetFileName called too early)");
            return GetFullPath(file.ToString("G"));
        }
    }
}
