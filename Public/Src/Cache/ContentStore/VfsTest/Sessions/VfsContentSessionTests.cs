// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.Distributed;
using Test.BuildXL.TestUtilities.Xunit;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using ContentStoreTest.Sessions;
using BuildXL.Cache.ContentStore.Vfs;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Native.IO;
using System.IO;
using FluentAssertions;

namespace ContentStoreTest.Vfs.Sessions
{
    [TestClassIfSupported(TestRequirements.WindowsProjFs)]
    public class VfsContentSessionTests : ContentSessionTestsBase
    {
        private VirtualizedContentStore _vfsStore;

        public VfsContentSessionTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path;
            var configurationModel = new ConfigurationModel(configuration);
            var fsStore = new FileSystemContentStore(FileSystem, SystemClock.Instance, rootPath / "fs", configurationModel);

            _vfsStore = new VirtualizedContentStore(fsStore, Logger, new VfsCasConfiguration.Builder()
            {
                RootPath = rootPath / "vfs",
            }.Build());

            return _vfsStore;
        }

        [Fact]
        public Task TestBasicPlaceFile()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var content = "Hello world";
                var putResult = await session.PutContentAsync(context, content).ShouldBeSuccess();

                using (var placeDirectory = new DisposableDirectory(FileSystem))
                {
                    var path = placeDirectory.Path / "file.txt";
                    path = path.WithIsVirtual();
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token).ShouldBeSuccess();
                    Assert.True(result.IsPlaced());

                    AllowCallbacks(() =>
                    {
                        var actualText = FileSystem.ReadAllText(path);

                        var afterAttributes = FileUtilities.GetFileAttributes(path.Path);
                        afterAttributes.HasFlag(FileAttributes.Offline).Should().BeFalse();
                        Assert.Equal(content, actualText);
                    });
                }
            });
        }

        public void AllowCallbacks(Action action)
        {
            try
            {
                VfsUtilities.AllowCurrentProcessCallbacks = true;
                action();
            }
            finally
            {
                VfsUtilities.AllowCurrentProcessCallbacks = false;
            }
        }
    }
}
