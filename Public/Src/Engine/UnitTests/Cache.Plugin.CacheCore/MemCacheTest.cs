// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine.Cache.Plugin.CacheCore
{
    /// <summary>
    /// Base class for tests that exercise caches from <see cref="BuildXL.Cache.InMemory.MemCacheFactory"/>.
    /// Temporary storage is available in <see cref="TemporaryDirectory"/>.
    /// </summary>
    public abstract class MemCacheTest : TemporaryStorageTestBase
    {
        protected MemCacheTest(ITestOutputHelper output)
            : base(output)
        {
            // TODO: We should just be able to call a constructor!
            var possibleCache = CacheFactory.InitializeCacheAsync(
                @"
                {
                    ""Assembly"":""BuildXL.Cache.InMemory"",
                    ""Type"":""BuildXL.Cache.InMemory.MemCacheFactory"", 
                    ""CacheId"":""TestCache"",
                    ""StrictMetadataCasCoupling"":true,
                }", default(Guid)).Result;

            XAssert.IsTrue(possibleCache.Succeeded);
            Cache = possibleCache.Result;

            var possibleSession = Cache.CreateSessionAsync().Result;
            XAssert.IsTrue(possibleSession.Succeeded);
            Session = possibleSession.Result;
        }

        public ICache Cache { get; }

        public ICacheSession Session { get; }

        /// <summary>
        /// Adds a string to the CAS and returns it hash.
        /// </summary>
        public async Task<ContentHash> AddContent(string content)
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            using (var memStream = new MemoryStream(contentBytes))
            {
                var possibleCasHash = await Session.AddToCasAsync(memStream);
                XAssert.IsTrue(possibleCasHash.Succeeded);
                return possibleCasHash.Result.ToContentHash();
            }
        }

        /// <summary>
        /// Hashes a string without adding it to the CAS.
        /// </summary>
        public static ContentHash HashContent(string content)
        {
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            return ContentHashingUtilities.HashBytes(contentBytes);
        }
    }
}
