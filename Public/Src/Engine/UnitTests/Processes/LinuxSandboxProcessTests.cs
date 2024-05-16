// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Unix;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Runs native tests for the Linux Sandbox
    /// </summary>
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public sealed class LinuxSandboxProcessTests : LinuxSandboxProcessTestsBase
    {
        public LinuxSandboxProcessTests(ITestOutputHelper output)
            : base(output)
        {
        }
        
        [Fact]
        public void CallTestAnonymousFile()
        {
            using (var tempFiles = new TempFileStorage(canGetFileNames: true))
            {
                var process = CreateTestProcess(
                    Context.PathTable,
                    tempFiles,
                    GetNativeTestName(MethodBase.GetCurrentMethod().Name),
                    inputFiles: ReadOnlyArray<FileArtifact>.Empty,
                    inputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    outputFiles: ReadOnlyArray<FileArtifactWithAttributes>.Empty,
                    outputDirectories: ReadOnlyArray<DirectoryArtifact>.Empty,
                    untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty);

                var processInfo = ToProcessInfo(process);
                processInfo.FileAccessManifest.ReportFileAccesses = true;
                processInfo.FileAccessManifest.MonitorChildProcesses = true;
                processInfo.FileAccessManifest.FailUnexpectedFileAccesses = false;

                var result = RunProcess(processInfo).Result;

                XAssert.AreEqual(result.ExitCode, 0, $"{Environment.NewLine}Standard Output: '{result.StandardOutput.ReadValueAsync().Result}'{Environment.NewLine}Standard Error: '{result.StandardError.ReadValueAsync().Result}'");

                XAssert.IsFalse(
                    result.FileAccesses.Select(f => f.ManifestPath.ToString(Context.PathTable)).Contains("/memfd:testFile (deleted)"),
                    $"Anonymous file reported by sandbox, file accesses:{Environment.NewLine}{string.Join(Environment.NewLine, result.FileAccesses.Select(f => f.ManifestPath.ToString(Context.PathTable)).ToArray())}");
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void FullPathResolutionOnReports()
        {
            var tempFiles = new TempFileStorage(canGetFileNames : true);
            var link = tempFiles.GetDirectory("symlinkDir", skipCreate: true);
            var real = tempFiles.GetDirectory("realDir");
            var createSymlink = FileUtilities.TryCreateSymbolicLink(link, real, isTargetFile: false); 
            if (!createSymlink.Succeeded)
            {
                XAssert.IsTrue(false, createSymlink.Failure.Describe());                
            }

            // The test calls readlink(symlinkDir/nonExistingFile.txt): we should get a report on the path
            // with the resolved intermediate symlink, and no reports on the symlink'd path.
            RunNativeTest("FullPathResolutionOnReports", workingDirectory: tempFiles);
            AssertLogContains(GetRegex("readlink", "realDir/nonExistingFile.txt"));
            AssertLogNotContains(GetRegex("readlink", "symlinkDir/nonExistingFile.txt"));
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void ReadlinkReportDoesNotResolveFinalComponent()
        {
            var tempFiles = new TempFileStorage(canGetFileNames : true);
            var dir = tempFiles.GetDirectory("realDir");
            var file = tempFiles.GetFileName(dir, "file.txt");
            var link = tempFiles.GetFileName(dir, "symlink.txt");
            
            File.WriteAllText(file, "chelivery");
            XAssert.IsTrue(File.Exists(file));

            var createSymlink = FileUtilities.TryCreateSymbolicLink(link, file, isTargetFile: true); 
            if (!createSymlink.Succeeded)
            {
                XAssert.IsTrue(false, createSymlink.Failure.Describe());                
            }

            // The test calls readlink(realDir/symlink.txt): we should get a report on this path, with no resolution of the final component
            RunNativeTest("ReadlinkReportDoesNotResolveFinalComponent", workingDirectory: tempFiles);
            AssertLogContains(GetRegex("readlink", "realDir/symlink.txt"));
            AssertLogNotContains(GetRegex("readlink", "symlinkDir/file.txt"));
        }
    }
}
