// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Collections.Generic;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities;
using System.Diagnostics;
using System.Linq;
using BuildXL.Native.IO;

#nullable enable

namespace Test.BuildXL.Processes
{
    [TestClassIfSupported(requiresLinuxBasedOperatingSystem: true)]
    public class EBPFSandboxProcessTest : SandboxedProcessTestBase
    {
        // CODESYNC: Public/Src/Sandbox/Linux/ebpf/ebpfcommon.h
        private const int s_fileAccessRingBufferSize = 4096 * 512;

        public EBPFSandboxProcessTest(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        [Fact]
        public void ValidateRingBufferExceedCapacity()
        {
            // For now this test only runs in ADO builds, where we can set the required capabilities without an interactive prompt.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            {
                return;
            }

            // CODESYNC: Public/Src/Engine/UnitTests/Processes/Test.BuildXL.Processes.dsc
            string ringBufferTest = SandboxedProcessUnix.EnsureDeploymentFile("RingBufferTest/ringbuffer_test");

            // The test needs the ability to retrieve a pinned EBPF map
            var setCapResult = UnixGetCapUtils.TrySetEBPFCapabilitiesIfNeeded(ringBufferTest, interactive: false, out _);
            // This test only runs on ADO, and therefore we should be able to set the capabilities without an interactive prompt.
            XAssert.IsTrue(setCapResult);

            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    ringBufferTest,
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    Arguments = "",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF capacity test",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(isInTestMode: true),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();

            // The test triggers the capacity exceeded callback by writing more than the ring buffer can hold.
            // The test should succeed, which means the capacity exceeded callback was called and all the expected events were written to the queue.
            AssertExitCode(result, 0);
        }

        [Theory]
        [InlineData("/test/this/path", "/test/this/other/path")]
        [InlineData("/test/this/path", "/unrelated/path")]
        public void ValidateIncrementalPaths(string path1, string path2)
        {
            // For now this test only runs in ADO builds, where we can set the required capabilities without an interactive prompt.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            {
                return;
            }

            // CODESYNC: Public/Src/Engine/UnitTests/Processes/Test.BuildXL.Processes.dsc
            string incrementalPathTest = SandboxedProcessUnix.EnsureDeploymentFile("RingBufferTest/incremental_path_test");

            // The test needs the ability to retrieve a pinned EBPF map
            var setCapResult = UnixGetCapUtils.TrySetEBPFCapabilitiesIfNeeded(incrementalPathTest, interactive: false, out _);
            // This test only runs on ADO, and therefore we should be able to set the capabilities without an interactive prompt.
            XAssert.IsTrue(setCapResult);

            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    incrementalPathTest,
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    Arguments = $"{path1} {path2}",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF incremental test",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(isInTestMode: true),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();
            AssertExitCode(result, 0);

            var messages = m_eventListener.GetLogMessagesForEventId((int)LogEventId.LogDetoursDebugMessage);

            // On managed side, paths should roundtrip as is, in the same order.
            var managedSide = messages.Where(s => s.Contains("(( test_synthetic:10 ))")).ToList();
            XAssert.AreEqual(2, managedSide.Count);
            XAssert.Contains(managedSide[0], path1);
            XAssert.Contains(managedSide[1], path2);

            // On the native side, first path should be sent as is. Second path should be incrementally encoded.
            var nativeSide = messages.Where(s => s.Contains("kernel function: test_synthetic")).ToList();
            XAssert.AreEqual(2, nativeSide.Count);
            XAssert.Contains(nativeSide[0], $"path: '{path1}'");
            XAssert.Contains(nativeSide[1], $"path: '{path2.Substring(CommonPrefixLength(path1, path2))}'");
        }

        private static void AssertExitCode(SandboxedProcessResult result, int expected)
        {
            XAssert.AreEqual(expected, result.ExitCode, $"Exit code was {result.ExitCode}.{Environment.NewLine}Standard out:{Environment.NewLine}{result.StandardOutput?.ReadValueAsync().Result}{Environment.NewLine}Standard error:{Environment.NewLine}{result.StandardError?.ReadValueAsync().Result}");
        }

        private static int CommonPrefixLength(string s1, string s2)
        {
            int minLength = Math.Min(s1.Length, s2.Length);
            int i = 0;
            for (; i < minLength; i++)
            {
                if (s1[i] != s2[i])
                {
                    break;
                }
            }

            return i;
        }

        [Fact]
        public void ValidateRingBufferMultiplier()
        {
            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            // Pass an explicit multiplier
            int multiplier = 2;
            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    "/usr/bin/echo",
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true,
                    ringBufferSizeMultiplier: multiplier)
                {
                    Arguments = "hi",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF ring buffer multiplier test",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(isInTestMode: true),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            // Verify the ringbuffer size honored the multiplier
            string sandboxMessages = string.Join(Environment.NewLine, m_eventListener.GetLog());
            XAssert.Contains(sandboxMessages, $"Total available space: {s_fileAccessRingBufferSize * multiplier}");
        }

        [Theory]
        [InlineData("/untracked/path", true)]
        [InlineData("/untracked/nested/path", true)]
        // The underlying EBPF trie is capped by 256 (so untracked scopes larger than this are not included). This shouldn't affect accesses longer than 256, so just make sure we handle this well.
        [InlineData("/untracked/nested/path/longer/than/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/256/", true)]
        [InlineData("/untrackedpath", false)]
        [InlineData("/another/untracked/path", false)]
        public void AccessesUnderUntrackedScopesAreNotSent(string access, bool expectUntracked)
        {
            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            // Add a scope that represents an untracked one
            fileAccessManifest.AddScope(AbsolutePath.Create(Context.PathTable, "/untracked"), mask: ~FileAccessPolicy.ReportAccess, values: FileAccessPolicy.AllowAll);

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    "/usr/bin/stat",
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    Arguments = access,
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF ring buffer untracked test",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(isInTestMode: true),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();
            // None of the stated paths exist, so stat returns error code 1
            XAssert.AreEqual(1, result.ExitCode);

            string sandboxMessages = string.Join(Environment.NewLine, m_eventListener.GetLog());
            XAssert.Contains(sandboxMessages, $"Avoided sending to user side {(expectUntracked ? "1" : "0")} untracked");
        }

        [Fact]
        public void UntrackedScopesExceedingTheLimitAreNotAdded()
        {
            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            // Add a scope that represents an untracked one, but with a path that exceeds the 256 size limit. The path should not be considered on kernel side.
            // This test just checks that things still work correctly even with the oversized path.
            string oversizedScope = string.Concat(Enumerable.Repeat("/untracked", 256));
            fileAccessManifest.AddScope(AbsolutePath.Create(Context.PathTable, oversizedScope), mask: ~FileAccessPolicy.ReportAccess, values: FileAccessPolicy.AllowAll);

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    "/usr/bin/stat",
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    // stat under the untracked scope
                    Arguments = $"{oversizedScope}/foo",
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF ring buffer untracked test",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(isInTestMode: true),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();
            // None of the stated paths exist, so stat returns error code 1
            XAssert.AreEqual(1, result.ExitCode);

            string sandboxMessages = string.Join(Environment.NewLine, m_eventListener.GetLog());
            // The scope was not added for the kernel to see
            XAssert.Contains(sandboxMessages, $"Untracked scope path '{oversizedScope}/' is too long");
            // The access was not avoided
            XAssert.Contains(sandboxMessages, $"Avoided sending to user side 0 untracked");
        }

        [Theory]
        [InlineData("/test/this//path", "/test/this/path")]
        [InlineData("/test/this/./path", "/test/this/path")]
        [InlineData("/test/this/path/", "/test/this/path")]
        [InlineData("/test/this/../path", "/test/path")]
        [InlineData("/../test/this/path", "/test/this/path")]
        [InlineData("/go/over/../root/../../../../../test/this/path", "/test/this/path")]
        [InlineData("/./test/this//////a/../slightly/more/../../complicated/.//../path", "/test/this/path")]
        [InlineData("/test/end/path//", "/test/end/path")]
        [InlineData("/test/end/path/.", "/test/end/path")]
        [InlineData("/test/end/path/..", "/test/end")]
        [InlineData("/test/end/path/../", "/test/end")]
        // Some corner cases
        [InlineData("/../test/../this/path/../../..", "/")]
        [InlineData("/../test/../this/path/../../../.", "/")]
        [InlineData("/..", "/")]
        [InlineData("/.", "/")]
        [InlineData("//", "/")]
        public void ValidatePathCanonicalization(string path, string canonicalizedPath)
        {
            // For now this test only runs in ADO builds, where we can set the required capabilities without an interactive prompt.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            {
                return;
            }

            // CODESYNC: Public/Src/Engine/UnitTests/Processes/Test.BuildXL.Processes.dsc
            string pathCanonicalizationTest = SandboxedProcessUnix.EnsureDeploymentFile("RingBufferTest/path_canonicalization_test");

            // The test needs the ability to find an EBPF program by name
            var setCapResult = UnixGetCapUtils.TrySetEBPFCapabilitiesIfNeeded(pathCanonicalizationTest, interactive: false, out _);
            // This test only runs on ADO, and therefore we should be able to set the capabilities without an interactive prompt.
            XAssert.IsTrue(setCapResult);

            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    pathCanonicalizationTest,
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    Arguments = path,
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF path canonicalization test",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(isInTestMode: true),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();
            AssertExitCode(result, 0);

            var messages = m_eventListener.GetLogMessagesForEventId((int)LogEventId.LogDetoursDebugMessage);

            // Retrieve the single synthetic probe we sent and check that the path has been canonicalized correctly
            var probe = messages.Where(s => s.Contains("kernel function: test_synthetic")).Single();
            XAssert.Contains(probe, $"path: '{canonicalizedPath}'");
        }

        [Theory]
        [InlineData("/usr/bin/readlink", "dir-link/file-link", "dir/file-link")]
        [InlineData("/usr/bin/mkdir", "dir-link/new-dir", "dir/new-dir")]
        [InlineData("/usr/bin/rmdir", "dir-link/nested", "dir/nested")]
        public void RawStringOperationsResolveSymlinks(string command, string arguments, string expected)
        {
            // Creates the following layout:
            //   dir/
            //      nested/
            //      file.txt
            //      file-link -> file.txt
            //   dir-link/ -> dir

            var dir = Path.Combine(TemporaryDirectory, "dir");
            var file = Path.Combine(dir, "file.txt");
            var dirLink = Path.Combine(TemporaryDirectory, "dir-link");
            var fileLinkViaDirLink = Path.Combine(dirLink, "file-link");

            FileUtilities.CreateDirectory(dir);
            FileUtilities.CreateDirectory(Path.Combine(dir, "nested"));
            File.WriteAllText(file, "hi");

            // Create dirLink -> dir
            var res = FileUtilities.TryCreateSymbolicLink(dirLink, dir, isTargetFile: false);
            XAssert.IsTrue(res.Succeeded);
            // Create dir-link/file-link -> dir/file
            res = FileUtilities.TryCreateSymbolicLink(fileLinkViaDirLink, file, isTargetFile: true);
            XAssert.IsTrue(res.Succeeded);

            var fileAccessManifest = new FileAccessManifest(Context.PathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
                PipId = 1,
                EnableLinuxSandboxLogging = true
            };

            var info =
                new SandboxedProcessInfo(
                    Context.PathTable,
                    new TempFileStorage(canGetFileNames: true, rootPath: TemporaryDirectory),
                    command,
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: LoggingContext,
                    useGentleKill: true)
                {
                    // stat under the untracked scope
                    Arguments = arguments,
                    WorkingDirectory = TemporaryDirectory,
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = $"EBPF symlink resolution test for {command}",
                    SandboxConnection = new SandboxConnectionLinuxEBPF(isInTestMode: true),
                };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();
            var result = process.GetResultAsync().GetAwaiter().GetResult();
            XAssert.AreEqual(0, result.ExitCode);

            var expectedPath = AbsolutePath.Create(Context.PathTable, Path.Combine(TemporaryDirectory, expected));

            var expectedAccess = result.FileAccesses?.Single(access => access.ManifestPath == expectedPath);
            XAssert.IsNotNull(expectedAccess, $"Expected to find an access to {expectedPath} in the unexpected accesses: {string.Join(Environment.NewLine, result.FileAccesses?.Select(a => a.Path) ?? Array.Empty<string>())}");
        }
    }
}
