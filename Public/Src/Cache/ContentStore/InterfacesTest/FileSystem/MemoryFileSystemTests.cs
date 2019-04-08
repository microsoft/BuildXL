// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.InterfacesTest.FileSystem
{
    public class MemoryFileSystemTests : AbsFileSystemTests
    {
        public MemoryFileSystemTests()
            : base(() => new MemoryFileSystem(TestSystemClock.Instance, new[] {'C', 'D'}))
        {
        }

        [Fact]
        public void InitialStats()
        {
            var fileSystem = (MemoryFileSystem)FileSystem;
            var stats = fileSystem.CurrentStatistics;
            Assert.Equal(0, stats.FileOpensForRead);
            Assert.Equal(0, stats.FileOpensForWrite);
            Assert.Equal(0, stats.FileDataReadOperations);
            Assert.Equal(0, stats.FileDataWriteOperations);
            Assert.Equal(0, stats.FileMoves);
            Assert.Equal(0, stats.FileDeletes);
        }

        [Fact]
        // No hardlinks on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void HardLinkToDifferentVolumeReturnsFalse()
        {
            var pathToFile = new AbsolutePath(@"C:\file.txt");
            var pathToLinkDifferentVolume = new AbsolutePath(@"D:\foo.txt");
            FileSystem.WriteAllBytes(pathToFile, new byte[] {1});

            var result = FileSystem.CreateHardLink(pathToFile, pathToLinkDifferentVolume, false);
            Assert.Equal(CreateHardLinkResult.FailedSourceAndDestinationOnDifferentVolumes, result);
        }

        [Fact]
        // No hardlinks on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void HardLinksAreDisabled()
        {
            using (var fileSystem = new MemoryFileSystem(TestSystemClock.Instance, useHardLinks: false))
            {
                var pathToFile = new AbsolutePath(@"C:\file.txt");
                var pathToLinkDifferentVolume = new AbsolutePath(@"C:\foo.txt");
                fileSystem.WriteAllBytes(pathToFile, new byte[] {1});

                var result = fileSystem.CreateHardLink(pathToFile, pathToLinkDifferentVolume, false);
                Assert.Equal(CreateHardLinkResult.FailedNotSupported, result);
            }
        }

        [Fact]
        public void TempPath()
        {
            Assert.Equal(PathGeneratorUtilities.GetAbsolutePath("C", "temp"), FileSystem.GetTempPath().Path);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // No differentiation between drives on Mac
        public void TempPathNotOnAvailableDrive()
        {
            Action a = () => Assert.Null(new MemoryFileSystem(TestSystemClock.Instance, new[] {'C'}, tempPath: new AbsolutePath(@"E:\temp")));
            ArgumentException e = Assert.Throws<ArgumentException>(a);
            Assert.Contains("not on an available drive", e.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
