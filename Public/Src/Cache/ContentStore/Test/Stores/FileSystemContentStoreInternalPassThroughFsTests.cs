// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Interop.Unix;
using BuildXL.Native.IO;
using ContentStoreTest.Stores;
using ContentStoreTest.Test;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using SystemProcess = System.Diagnostics.Process;

namespace BuildXL.Cache.ContentStore.Test.Stores
{
    [Trait("Category", "Integration")]
    public class FileSystemContentStoreInternalPassThroughFsTests : FileSystemContentStoreInternalTestBase
    {
        private static readonly MemoryClock Clock = new MemoryClock();
        private static readonly ContentStoreConfiguration Config = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(5);

        public FileSystemContentStoreInternalPassThroughFsTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(), TestGlobal.Logger, output)
        {
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true, requiresAdmin: true)]
        public async Task PutAttemptHardLinkCacheInDifferentMountFallBackToCopy()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);

                var virtualDiskPath = testDirectory.Path / "vh.img";
                using var virtualDiskMountPoint = new DisposableDirectory(FileSystem);

                // Create a virtual disk and mount it
                RunBashCommand($"dd if=/dev/zero of={virtualDiskPath} bs=1M count=3");
                RunBashCommand($"mkfs.ext4 {virtualDiskPath}");
                RunBashCommand($"sudo mount {virtualDiskPath} {virtualDiskMountPoint.Path}");

                // Use mounted virtual disk as cache root to create test store
                await TestStore(context, Clock, virtualDiskMountPoint, async store =>
                {
                    byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                    ContentHash contentHash;

                    // Put content into store
                    using (var memoryStream = new MemoryStream(bytes))
                    {
                        var putStreamResult = await store.PutStreamAsync(context, memoryStream, ContentHashType);
                        contentHash = putStreamResult.ContentHash;
                        Assert.Equal(bytes.Length, putStreamResult.ContentSize);
                    }

                    // Create HardLink failed at FailedSourceAndDestinationOnDifferentVolumes
                    AbsolutePath testDesFile = testDirectory.CreateRandomFileName();
                    AbsolutePath testSourceFile = virtualDiskMountPoint.CreateRandomFileName();
                    FileSystem.CreateEmptyFile(testSourceFile);

                    var hardlinkCreationResult = FileSystem.CreateHardLink(testSourceFile, testDesFile, true);
                    Assert.True(hardlinkCreationResult == CreateHardLinkResult.FailedSourceAndDestinationOnDifferentVolumes, hardlinkCreationResult.ToString());

                    // Create a new file with the same content
                    AbsolutePath newPathWithSameContent = testDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(newPathWithSameContent, bytes);

                    // Call PutFileAsync with FileRealizationMode.Any
                    // PutFile try HardLink first and fall back to copy
                    var result = await store.PutFileAsync(context, newPathWithSameContent, FileRealizationMode.Any, ContentHashType).ShouldBeSuccess();
                    result.ContentAlreadyExistsInCache.Should().BeTrue();

                    // After PutFileAsync, the file should not be deleted due to failed hardlink creation
                    Assert.True(FileSystem.FileExists(newPathWithSameContent));
                });
            }
        }

        private void RunBashCommand(string bashScriptCommand)
        {
            _ = FileUtilities.SetExecutePermissionIfNeeded(UnixPaths.BinBash).ThrowIfFailure();

            var startInfo = new ProcessStartInfo
            {
                FileName = UnixPaths.BinBash,
                Arguments = $"-c \"{bashScriptCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var process = new SystemProcess
            {
                StartInfo = startInfo
            };

            process.OutputDataReceived += (sender, data) => Logger.Info(data.Data);
            process.ErrorDataReceived += (sender, data) => Logger.Error(data.Data);

            Logger.Info($"Running {process.StartInfo.FileName} {process.StartInfo.Arguments}");

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            process.WaitForExit();

            Assert.Equal(process.ExitCode, 0);
        }
    }
}
