// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Distributed.Redis.Credentials;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.DistributedTest.Metadata
{
    [Collection("ConnectionStringProviderTests")] // Serialized so that environment variable use doesn't overlap.
    public class RedisMetadataCacheFactoryTests : TestBase
    {
        private const string KeySpace = "TestKeySpace:";

        private static readonly DistributedCacheSessionTracer _tracer = new DistributedCacheSessionTracer(
            TestGlobal.Logger,
            nameof(RedisMetadataCacheFactoryTests));

        private static readonly TimeSpan CacheKeyBumpTime = RedisMetadataCache.DefaultCacheKeyBumpTime + RedisMetadataCache.DefaultCacheKeyBumpTime;

        public RedisMetadataCacheFactoryTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        [Fact]
        public void NoProviderThrows()
        {
            Action a = () => RedisMetadataCacheFactory.Create(_tracer).Dispose();
            a.Should().Throw<InvalidOperationException>().Where(e =>
                e.Message.Contains(ExecutableConnectionStringProvider.CredentialProviderVariableName) &&
                e.Message.Contains(EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable));
        }

        [Fact]
        public async Task NonexistentExecutableConnectionStringProviderError()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var nonexistentPath = testDirectory.CreateRandomFileName();
                using (new TestEnvironmentVariable(ExecutableConnectionStringProvider.CredentialProviderVariableName, nonexistentPath.Path)
                    )
                {
                    await TestCacheAsync(
                        () => RedisMetadataCacheFactory.Create(_tracer),
                        async cache =>
                        {
                            var connectionStringResult = await cache.ConnectionStringProvider.GetConnectionString().ConfigureAwait(false);
                            connectionStringResult.Succeeded.Should().BeFalse();
                            connectionStringResult.ErrorMessage.Should().Contain("The system cannot find the file specified");
                        }).ConfigureAwait(false);
                }
            }
        }

        [Fact]
        public async Task EnvironmentProviderOverridesExecutableProvider()
        {
            var connectionString = $"ConnectionString{ThreadSafeRandom.Generator.Next()}";
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var nonexistentPath = testDirectory.CreateRandomFileName();
                using (new TestEnvironmentVariable(ExecutableConnectionStringProvider.CredentialProviderVariableName, nonexistentPath.Path)
                    )
                using (new TestEnvironmentVariable(EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable, connectionString))
                {
                    await TestCacheAsync(
                        () => RedisMetadataCacheFactory.Create(_tracer),
                        async cache =>
                        {
                            var connectionStringResult =
                                await cache.ConnectionStringProvider.GetConnectionString().ConfigureAwait(false);
                            connectionStringResult.ConnectionString.Should().Be(connectionString);
                        }).ConfigureAwait(false);
                }
            }
        }

        [Fact]
        public Task CreateDefault()
        {
            return TestWithEnvironmentConnectionStringAsync(
                () => RedisMetadataCacheFactory.Create(_tracer),
                cache =>
                {
                    cache.Keyspace.Should().Be(RedisMetadataCacheFactory.DefaultKeySpace + RedisMetadataCacheFactory.Salt);
                    cache.CacheKeyBumpTime.Should().Be(RedisMetadataCache.DefaultCacheKeyBumpTime);
                });
        }

        [Fact]
        public Task CreateWithKeySpace()
        {
            return TestWithEnvironmentConnectionStringAsync(
                () => RedisMetadataCacheFactory.Create(_tracer, KeySpace),
                cache =>
                {
                    cache.Keyspace.Should().Be(KeySpace + RedisMetadataCacheFactory.Salt);
                });
        }

        [Fact]
        public Task CreateWithCacheKeyBumpTime()
        {
            return TestWithEnvironmentConnectionStringAsync(
                () => RedisMetadataCacheFactory.Create(tracer: _tracer, cacheKeyBumpTime: CacheKeyBumpTime),
                cache =>
                {
                    cache.CacheKeyBumpTime.Should().Be(CacheKeyBumpTime);
                });
        }

        [Fact]
        public Task CreateDefaultWithProvider()
        {
            var connectionString = $"ConnectionString{ThreadSafeRandom.Generator.Next()}";
            return TestCacheAsync(
                () => RedisMetadataCacheFactory.Create(CreateMockProvider(connectionString), _tracer),
                async cache =>
                {
                    var connectionStringResult = await cache.ConnectionStringProvider.GetConnectionString().ConfigureAwait(false);
                    connectionStringResult.ConnectionString.Should().Be(connectionString);
                    cache.Keyspace.Should().Be(RedisMetadataCacheFactory.DefaultKeySpace + RedisMetadataCacheFactory.Salt);
                    cache.CacheKeyBumpTime.Should().Be(RedisMetadataCache.DefaultCacheKeyBumpTime);
                });
        }

        [Fact]
        public Task CreateWithKeySpaceAndProvider()
        {
            var connectionString = $"ConnectionString{ThreadSafeRandom.Generator.Next()}";
            return TestCacheAsync(
                () => RedisMetadataCacheFactory.Create(CreateMockProvider(connectionString), _tracer, KeySpace),
                cache =>
                {
                    cache.Keyspace.Should().Be(KeySpace + RedisMetadataCacheFactory.Salt);
                    return Task.FromResult(0);
                });
        }

        [Fact]
        public Task CreateWithCacheKeyBumpTimeAndProvider()
        {
            var connectionString = $"ConnectionString{ThreadSafeRandom.Generator.Next()}";
            return TestCacheAsync(
                () => RedisMetadataCacheFactory.Create(CreateMockProvider(connectionString), _tracer, cacheKeyBumpTime: CacheKeyBumpTime),
                cache =>
                {
                    cache.CacheKeyBumpTime.Should().Be(CacheKeyBumpTime);
                    return Task.FromResult(0);
                });
        }

        private IConnectionStringProvider CreateMockProvider(string connectionString)
        {
            var mockProvider = new TestConnectionStringProvider(connectionString);
            return mockProvider;
        }

        private async Task TestWithEnvironmentConnectionStringAsync(Func<IMetadataCache> cacheFunc, Action<RedisMetadataCache> testAction)
        {
            var connectionString = $"ConnectionString{ThreadSafeRandom.Generator.Next()}";
            using (new TestEnvironmentVariable(EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable, connectionString))
            {
                await TestCacheAsync(cacheFunc, async cache =>
                {
                    var connectionStringResult = await cache.ConnectionStringProvider.GetConnectionString().ConfigureAwait(false);
                    connectionStringResult.ConnectionString.Should().Be(connectionString);
                    testAction(cache);
                });
            }
        }

        private async Task TestCacheAsync(Func<IMetadataCache> cacheFunc, Func<RedisMetadataCache, Task> testFunc)
        {
            using (var cache = cacheFunc())
            {
                var redisCache = cache as RedisMetadataCache;
                redisCache.Should().NotBeNull();
                await testFunc(redisCache).ConfigureAwait(false);
            }
        }

        private class TestConnectionStringProvider : IConnectionStringProvider
        {
            private readonly string _connectionString;

            public TestConnectionStringProvider(string connectionString)
            {
                _connectionString = connectionString;
            }

            public Task<ConnectionStringResult> GetConnectionString()
            {
                return Task.FromResult(ConnectionStringResult.CreateSuccess(_connectionString));
            }
        }
    }
}
