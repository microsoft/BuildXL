// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            using (var directory = new DisposableDirectory(FileSystem))
            {
                const Capabilities capabilities = Capabilities.Heartbeat;
                var pins = ContentHashes.Select(x => x.Serialize()).ToList();
                var expirationTicks = DateTime.UtcNow.Ticks;
                var sessionInfo = new HibernatedSessionInfo(1, SessionName, ImplicitPin.None, CacheName, pins, expirationTicks, capabilities);
                var sessions1 = new HibernatedSessions(new List<HibernatedSessionInfo> {sessionInfo});
                await sessions1.WriteAsync(FileSystem, directory.Path);
                FileSystem.HibernatedSessionsExists(directory.Path).Should().BeTrue();

                var fileSize = FileSystem.GetFileSize(directory.Path / HibernatedSessionsExtensions.FileName);
                fileSize.Should().BeGreaterThan(0);

                var sessions2 = await FileSystem.ReadHibernatedSessionsAsync(directory.Path);
                sessions2.Sessions.Count.Should().Be(1);
                sessions2.Sessions[0].Pins.Count.Should().Be(ContentHashCount);
                sessions2.Sessions[0].ExpirationUtcTicks.Should().Be(expirationTicks);
                sessions2.Sessions[0].Capabilities.Should().Be(capabilities);
                await FileSystem.DeleteHibernatedSessions(directory.Path);
            }
        }

        [Fact]
        public async Task RoundtripSessionsWithSameName()
        {
            const int count = 3;

            using (var directory = new DisposableDirectory(FileSystem))
            {
                var sessionInfoList = new List<HibernatedSessionInfo>();
                foreach (var i in Enumerable.Range(0, count))
                {
                    List<string> pins = ContentHashes.Select(x => x.Serialize()).ToList();
                    sessionInfoList.Add(new HibernatedSessionInfo(i, SessionName, ImplicitPin.None, CacheName, pins, DateTime.UtcNow.Ticks, Capabilities.None));
                }

                var sessions1 = new HibernatedSessions(sessionInfoList);
                await sessions1.WriteAsync(FileSystem, directory.Path);
                FileSystem.HibernatedSessionsExists(directory.Path).Should().BeTrue();

                var sessions2 = await FileSystem.ReadHibernatedSessionsAsync(directory.Path);
                sessions2.Sessions.Count.Should().Be(count);
                sessions2.Sessions.All(s => s.Pins.Count == ContentHashCount).Should().BeTrue();
                sessions2.Sessions.All(s => s.Session == SessionName).Should().BeTrue();
                sessions2.Sessions.All(s => s.Cache == CacheName).Should().BeTrue();
                await FileSystem.DeleteHibernatedSessions(directory.Path);
            }
        }
    }
}
