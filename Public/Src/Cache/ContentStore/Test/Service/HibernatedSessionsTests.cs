// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Service
{
    public class HibernatedSessionsTests : TestBase
    {
        private const string CacheName = "cacheName";
        private const string SessionName = "sessionName";
        private const int ContentHashCount = 1000;
        private static readonly List<ContentHash> ContentHashes =
            Enumerable.Range(0, ContentHashCount).Select(x => ContentHash.Random()).ToList();

        public HibernatedSessionsTests()
            : base(() => new MemoryFileSystem(new TestSystemClock()), TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task Roundtrip()
        {
            var fileName = $"{Guid.NewGuid()}.json";
            using (var directory = new DisposableDirectory(FileSystem))
            {
                const Capabilities capabilities = Capabilities.Heartbeat;
                var pins = ContentHashes.Select(x => x.Serialize()).ToList();
                var expirationTicks = DateTime.UtcNow.Ticks;
                var sessionInfo = new HibernatedContentSessionInfo(1, SessionName, ImplicitPin.None, CacheName, pins, expirationTicks, capabilities);
                var sessions1 = new HibernatedSessions<HibernatedContentSessionInfo>(new List<HibernatedContentSessionInfo> {sessionInfo});
                await sessions1.WriteAsync(FileSystem, directory.Path, fileName);
                FileSystem.HibernatedSessionsExists(directory.Path, fileName).Should().BeTrue();

                var fileSize = FileSystem.GetFileSize(directory.Path / fileName);
                fileSize.Should().BeGreaterThan(0);

                var sessions2 = await FileSystem.ReadHibernatedSessionsAsync<HibernatedContentSessionInfo>(directory.Path, fileName);
                sessions2.Sessions.Count.Should().Be(1);
                sessions2.Sessions[0].Pins.Count.Should().Be(ContentHashCount);
                sessions2.Sessions[0].ExpirationUtcTicks.Should().Be(expirationTicks);
                sessions2.Sessions[0].Capabilities.Should().Be(capabilities);
                await FileSystem.DeleteHibernatedSessions(directory.Path, fileName);
            }
        }

        [Fact]
        public async Task RoundtripSessionsWithSameName()
        {
            const int count = 3;
            var fileName = $"{Guid.NewGuid()}.json";

            using (var directory = new DisposableDirectory(FileSystem))
            {
                var sessionInfoList = new List<HibernatedContentSessionInfo>();
                foreach (var i in Enumerable.Range(0, count))
                {
                    List<string> pins = ContentHashes.Select(x => x.Serialize()).ToList();
                    sessionInfoList.Add(new HibernatedContentSessionInfo(i, SessionName, ImplicitPin.None, CacheName, pins, DateTime.UtcNow.Ticks, Capabilities.None));
                }

                var sessions1 = new HibernatedSessions<HibernatedContentSessionInfo>(sessionInfoList);
                await sessions1.WriteAsync(FileSystem, directory.Path, fileName);
                FileSystem.HibernatedSessionsExists(directory.Path, fileName).Should().BeTrue();

                var sessions2 = await FileSystem.ReadHibernatedSessionsAsync<HibernatedContentSessionInfo>(directory.Path, fileName);
                sessions2.Sessions.Count.Should().Be(count);
                sessions2.Sessions.All(s => s.Pins.Count == ContentHashCount).Should().BeTrue();
                sessions2.Sessions.All(s => s.Session == SessionName).Should().BeTrue();
                sessions2.Sessions.All(s => s.Cache == CacheName).Should().BeTrue();
                await FileSystem.DeleteHibernatedSessions(directory.Path, fileName);
            }
        }
    }
}
