// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class HibernatedCacheSessionsTests : TestBase
    {
        public HibernatedCacheSessionsTests()
            : base(() => new MemoryFileSystem(new TestSystemClock()), TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task Roundtrip()
        {
            var fileName = $"{Guid.NewGuid()}.json";
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var sessionId = 42;
                var serializedConfig = "Foo";
                var pat = Guid.NewGuid().ToString();

                var numOperations = 3;
                var operations = Enumerable.Range(0, numOperations)
                    .Select(_ => generateRandomOperation())
                    .ToList();

                var sessionInfo = new HibernatedCacheSessionInfo(sessionId, serializedConfig, pat, operations);
                var sessions1 = new HibernatedSessions<HibernatedCacheSessionInfo>(new List<HibernatedCacheSessionInfo> { sessionInfo });
                sessions1.Write(FileSystem, directory.Path, fileName);
                FileSystem.HibernatedSessionsExists(directory.Path, fileName).Should().BeTrue();

                var fileSize = FileSystem.GetFileSize(directory.Path / fileName);
                fileSize.Should().BeGreaterThan(0);

                var sessions2 = await FileSystem.ReadHibernatedSessionsAsync<HibernatedCacheSessionInfo>(directory.Path, fileName);
                sessions2.Sessions.Count.Should().Be(1);
                sessions2.Sessions[0].Id.Should().Be(sessionId);
                sessions2.Sessions[0].SerializedSessionConfiguration.Should().Be(serializedConfig);
                sessions2.Sessions[0].Pat.Should().Be(pat);
                sessions2.Sessions[0].PendingPublishingOperations.Should().BeEquivalentTo(operations);

                await FileSystem.DeleteHibernatedSessions(directory.Path, fileName);

                PublishingOperation generateRandomOperation()
                {
                    var amountOfHashes = 3;
                    var hashes = Enumerable.Range(0, amountOfHashes).Select(_ => ContentHash.Random()).ToArray();
                    var contentHashList = new ContentHashList(hashes);
                    var determinism = CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.UtcNow.AddMilliseconds(ThreadSafeRandom.Generator.Next()));
                    var contentHashListWithDeterminism = new ContentHashListWithDeterminism(contentHashList, determinism);

                    var fingerprint = new Fingerprint(ContentHash.Random().ToByteArray());
                    var selector = new Selector(ContentHash.Random());
                    var strongFingerprint = new StrongFingerprint(fingerprint, selector);

                    return new PublishingOperation
                    {
                        ContentHashListWithDeterminism = contentHashListWithDeterminism,
                        StrongFingerprint = strongFingerprint
                    };
                }
            }
        }
    }
}
