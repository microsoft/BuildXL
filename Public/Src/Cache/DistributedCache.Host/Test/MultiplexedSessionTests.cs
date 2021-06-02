// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.Host.Service.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Host.Test
{
    public class MultiplexedSessionTests : TestBase
    {
        public MultiplexedSessionTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task HardlinkTestAsync()
        {
            var clock = new MemoryClock();

            var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(10);
            var configurationModel = new ConfigurationModel(inProcessConfiguration: configuration, ConfigurationSelection.RequireAndUseInProcessConfiguration);

            var root1 = TestRootDirectoryPath / "Store1";
            var store1 = new FileSystemContentStore(FileSystem, clock, root1, configurationModel);

            var fakeDrive = new AbsolutePath(@"X:\");
            var root2 = fakeDrive / "Store2";
            var redirectedFileSystem = new RedirectionFileSystem(FileSystem, fakeDrive, TestRootDirectoryPath);
            var store2 = new FileSystemContentStore(redirectedFileSystem, clock, fakeDrive, configurationModel);

            var stores = new Dictionary<string, IContentStore>
            {
                { root1.GetPathRoot(), store1 },
                { root2.GetPathRoot(), store2 },
            };

            var multiplexed = new MultiplexedContentStore(stores, preferredCacheDrive: root1.GetPathRoot(), tryAllSessions: true);

            var context = new Context(Logger);
            
            await multiplexed.StartupAsync(context).ShouldBeSuccess();

            var sessionResult = multiplexed.CreateSession(context, "Default", ImplicitPin.None).ShouldBeSuccess();
            var session = sessionResult.Session;

            // Put random content which should go to preferred drive
            var putResult = await session.PutRandomAsync(context, ContentStore.Hashing.HashType.MD5, provideHash: true, size: 1024, CancellationToken.None)
                .ShouldBeSuccess();

            // Should be able to place it with hardlink in primary drive
            var destination1 = TestRootDirectoryPath / "destination1.txt";
            var placeResult1 = await session.PlaceFileAsync(
                context,
                putResult.ContentHash,
                destination1,
                FileAccessMode.ReadOnly,
                FileReplacementMode.FailIfExists,
                FileRealizationMode.HardLink,
                CancellationToken.None)
                .ShouldBeSuccess();
            placeResult1.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);

            // Should be able to place it with hardlink in secondary drive.
            // The cache should copy the contents internally, and then place from the correct drive.
            var destination2 = fakeDrive / "destination2.txt";
            var placeResult2 = await session.PlaceFileAsync(
                context,
                putResult.ContentHash,
                destination2,
                FileAccessMode.ReadOnly,
                FileReplacementMode.FailIfExists,
                FileRealizationMode.HardLink,
                CancellationToken.None)
                .ShouldBeSuccess();
            placeResult2.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
        }
    }
}
