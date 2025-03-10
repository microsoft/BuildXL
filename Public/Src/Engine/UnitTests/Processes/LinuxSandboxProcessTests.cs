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
using BuildXL.Interop.Linux;

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
            if (!UsingEBPFSandbox)
            {
                AssertLogContains(GetRegex("readlink", "realDir/nonExistingFile.txt"));
                AssertLogNotContains(GetRegex("readlink", "symlinkDir/nonExistingFile.txt"));
            }
            else
            {
                // With EBPF we get a read on the intermediate symlink
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ReadFile, "symlinkDir"));
                // And a non-existent probe on the symlinked path
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, "symlinkDir/nonExistingFile.txt"));
            }
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
            if (UsingEBPFSandbox)
            {
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ReadFile, "realDir/symlink.txt"));
                AssertLogNotContains(GetAccessReportRegex(ReportedFileOperation.Probe, "symlinkDir/file.txt"));
            }
            else
            {
                AssertLogContains(GetRegex("readlink", "realDir/symlink.txt"));
                AssertLogNotContains(GetRegex("readlink", "symlinkDir/file.txt"));
            }
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void FileDescriptorAccessesFullyResolvesPath()
        {
            var tempFiles = new TempFileStorage(canGetFileNames : true);
            var link = tempFiles.GetDirectory("symlinkDir", skipCreate: true);
            var real = tempFiles.GetDirectory("realDir");
            var realFile = tempFiles.GetFileName(real, "file.txt");

            File.WriteAllText(realFile, "chelivery");
            XAssert.IsTrue(File.Exists(realFile));           

            var createSymlink = FileUtilities.TryCreateSymbolicLink(link, real, isTargetFile: false); 
            if (!createSymlink.Succeeded)
            {
                XAssert.IsTrue(false, createSymlink.Failure.Describe());                
            }

            var fileLink = tempFiles.GetFileName(real, "symlink.txt");
            createSymlink = FileUtilities.TryCreateSymbolicLink(fileLink, realFile, isTargetFile: true); 
            if (!createSymlink.Succeeded)
            {
                XAssert.IsTrue(false, createSymlink.Failure.Describe());                
            }

            RunNativeTest("FileDescriptorAccessesFullyResolvesPath", workingDirectory: tempFiles);

            // The native side does:
            // fd = open(symlinkDir/symlink.txt);
            // __fxstat(fd)
            // For the __fxstat report, we should associate the file descriptor to the real path, with the symlinks resolved
            AssertLogContains(GetAccessReportRegex(ReportedFileOperation.Probe, realFile));

            // At some point (namely, on open) we also should get reports for the intermediate symlinks that got us to the file
            if (UsingEBPFSandbox)
            { 
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ReadFile, link));
                AssertLogContains(GetAccessReportRegex(ReportedFileOperation.ReadFile, fileLink));
            }
            else
            {
                if (Ipc.IsGLibC234OrGreater)
                {
                    AssertLogContains(GetRegex("fstat", realFile));
                }
                else
                {
                    AssertLogContains(GetRegex("__fxstat", realFile));
                }
                AssertLogContains(GetRegex("_readlink", link));
                AssertLogContains(GetRegex("_readlink", fileLink));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExecReportsCorrectExecutableAndArguments(bool succeeds)
        {
            var tempFiles = new TempFileStorage(canGetFileNames : true);
            var testName = "ExecReportsCorrectExecutableAndArguments" + (succeeds ? "Success" : "Failed");

            var result = RunNativeTest(testName, workingDirectory: tempFiles, reportProcessArgs: true);
            var accesses = string.Join("\n", result.result.Processes.Select(p => $"{p.ProcessId}, {p.Path}, {string.Join(" ", p.ProcessArgs)}"));

            var expectedExe = succeeds ? "/usr/bin/echo" : TestProcessExe;
            var exepectedArgs = succeeds ? "/bin/echo hello world" : $"{TestProcessExe} -t ExecReportsCorrectExecutableAndArgumentsFailed";

            var process = result.result.Processes.FirstOrDefault(p => p.Path == expectedExe);

            if (process == null)
            {
                XAssert.IsTrue(false, $"Expected {expectedExe}, got {result.result.Processes[0].Path} with accesses: \n{accesses}");
            }
            
            // TODO: [pgunasekara] args are not checked in the ReportedProcess object for now. We do report args properly, however our logic to update the ReportedProcess object is not correct.
            // XAssert.IsTrue(result.result.Processes[0].ProcessArgs ==  exepectedArgs, $"Expected \"{exepectedArgs}\", got {string.Join(" ", result.result.Processes[0].ProcessArgs)}");
        }

        [Fact]
        public void OpenAtHandlesInvalidFd()
        {
            // This is ptrace specific
            if (!UsingEBPFSandbox)
            {
                RunNativeTest("OpenAtHandlesInvalidFd", unconditionallyEnableLinuxPTraceSandbox: true);
                AssertLogContains(caseSensitive: false, "failed to read or read invalid dirfd ('-1') for syscall 'openat' with path ''");
            }
        }
    }
}
