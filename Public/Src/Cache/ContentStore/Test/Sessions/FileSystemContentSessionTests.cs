// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Test;
using Xunit;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Hashing;
using System.Threading;
using FluentAssertions;
using System.IO;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using System;
using System.Text;

// ReSharper disable All
namespace ContentStoreTest.Sessions
{
    public class FileSystemContentSessionTests : ContentSessionTests
    {
        private readonly static Random Random = new Random();

        public FileSystemContentSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path;
            var configurationModel = new ConfigurationModel(configuration);
            return new FileSystemContentStore(FileSystem, SystemClock.Instance, rootPath, configurationModel);
        }

        [Fact]
        public async Task PlaceFileWithReplaceExistingToHardlinkSource()
        {
            using var testDirectory = new DisposableDirectory(FileSystem, FileSystem.GetTempPath() / "TestDir");
            await RunTestAsync(ImplicitPin.None, testDirectory, async (context, session) =>
            {
                var originalPath = testDirectory.Path / "original.txt";
                var fileContents = GetRandomFileContents();

                // Create the file and hardlink it into the cache.
                FileSystem.WriteAllText(originalPath, fileContents);
                var result = await session.PutFileAsync(context, HashType.MD5, originalPath, FileRealizationMode.HardLink, CancellationToken.None).ShouldBeSuccess();

                // Hardlink back to original location trying to replace existing.
                var copyResult = await session.PlaceFileAsync(
                    context,
                    result.ContentHash,
                    originalPath,
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    CancellationToken.None).ShouldBeSuccess();

                // The file is intact.
                FileSystem.ReadAllText(originalPath).Should().Be(fileContents);
            });
        }

        [Fact]
        public async Task HardlinkBecomesEmptyIfCallingCreateOnSourceDependingOnOs()
        {
            using var testDirectory = new DisposableDirectory(FileSystem, FileSystem.GetTempPath() / "TestDir");

            var originalPath = testDirectory.Path / "original.txt";
            var hardlinkedPath = testDirectory.Path / "hardlink.txt";
            var fileContents = GetRandomFileContents();

            // Create the file and hardlink
            FileSystem.WriteAllText(originalPath, fileContents);
            FileSystem.CreateHardLink(originalPath, hardlinkedPath, replaceExisting: true);

            using var hardlinkedStream = await FileSystem.OpenAsync(hardlinkedPath, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete);
            hardlinkedStream.Should().NotBeNull();

            // Open the original file with FileMode.Create. This should truncate the file.
            using var originalStream = await FileSystem.OpenAsync(originalPath, FileAccess.Write, FileMode.Create, FileShare.Read);

            // Read the contents of the hardlinked file.
            using var reader = new StreamReader(hardlinkedStream);
            var hardlinkedContent = await reader.ReadToEndAsync();

            // This test is only so that we better understand what is the behavior for this particular set of operations, which closely resemble our copy code.
            var expected = OperatingSystemHelper.IsWindowsOS ? fileContents : string.Empty;
            hardlinkedContent.Should().Be(expected);
        }

        [Fact]
        public void CyclicHardlinkWorks()
        {
            using var testDirectory = new DisposableDirectory(FileSystem, FileSystem.GetTempPath() / "TestDir");

            var originalPath = testDirectory.Path / "original.txt";
            var hardlinkedPath = testDirectory.Path / "hardlink.txt";
            var fileContents = GetRandomFileContents();

            FileSystem.WriteAllText(originalPath, fileContents);
            FileSystem.CreateHardLink(originalPath, hardlinkedPath, replaceExisting: true).Should().Be(CreateHardLinkResult.Success);

            // Create cyclic hardlink with replaceExisting=true
            FileSystem.CreateHardLink(hardlinkedPath, originalPath, replaceExisting: true).Should().Be(CreateHardLinkResult.Success);

            // Files should have the same contents.
            FileSystem.ReadAllText(originalPath).Should().Be(fileContents);
            FileSystem.ReadAllText(hardlinkedPath).Should().Be(fileContents);
        }

        private static string GetRandomFileContents()
        {
            var bytes = new byte[FileSystemDefaults.DefaultFileStreamBufferSize + 1];
            Random.NextBytes(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
