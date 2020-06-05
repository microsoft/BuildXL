// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Test;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class ResourcePoolTests
    {
        private class Resource : StartupShutdownBase
        {
            protected override Tracer Tracer => new Tracer("Dummy");
        }

        private struct Key
        {
            public int Number;
            public Key(int number) => Number = number;
        }

        [Fact]
        public async Task NoContractExceptionsWhenCleaning()
        {
            var capacity = 2;
            var context = new Context(TestGlobal.Logger);

            for (var i = 0; i < 10_000; i++)
            { 
                var pool = new ResourcePool<Key, Resource>(context, maxResourceCount: capacity, maxAgeMinutes: 1, resourceFactory: _ => new Resource());

                for (var j = 0; j < capacity; j++)
                {
                    using var wrapper = await pool.CreateAsync(new Key(j));
                    var value = wrapper.Value;
                }

                var createTask = pool.CreateAsync(new Key(capacity));
                var accessTask = pool.CreateAsync(new Key(0));

                var results = await Task.WhenAll(createTask, accessTask);
                results[0].Dispose();
                results[1].Dispose();
            }
        }

        [Fact]
        public async Task DuplicateClientsAreTheSameObject()
        {
            var capacity = 2;
            var context = new Context(TestGlobal.Logger);
            var pool = new ResourcePool<Key, Resource>(context, maxResourceCount: capacity, maxAgeMinutes: 1, resourceFactory: _ => new Resource());

            using var obj0 = await pool.CreateAsync(new Key(0));
            using var obj1 = await pool.CreateAsync(new Key(0));
            Assert.Same(obj0.Value, obj1.Value);
        }

        [Fact]
        public async Task ValidateCleanupOfExpired()
        {
            var resourceCount = 10;
            var clock = new MemoryClock();
            var maxAgeMinutes = 1;
            var pool = new ResourcePool<Key, Resource>(new Context(TestGlobal.Logger), resourceCount, maxAgeMinutes, resourceFactory: _ => new Resource(), clock);

            var wrappers = new List<ResourceWrapper<Resource>>();
            for (var i = 0; i < resourceCount; i++)
            {
                using var wrapper = await pool.CreateAsync(new Key(i));
                wrappers.Add(wrapper);
            }
            
            // Expire the resources
            clock.UtcNow += TimeSpan.FromMinutes(maxAgeMinutes);

            ResourceWrapper<Resource> wrapper2;
            using (wrapper2 = await pool.CreateAsync(new Key(-1)))
            {

            }

            Assert.Equal(resourceCount, pool.Counter[ResourcePoolCounters.Cleaned].Value);

            for (var i = 0; i < resourceCount; i++)
            {
                using var wrapper = await pool.CreateAsync(new Key(i));
                Assert.NotSame(wrapper.Value, wrappers[i].Value);
                Assert.Equal(1, wrapper.Uses);
                Assert.True(wrappers[i].Value.ShutdownCompleted);
            }
        }

        [Fact]
        public async Task ValidateSingleCleanup()
        {
            var maxCapacity = 10;
            var clock = new MemoryClock();
            var maxAgeMinutes = maxCapacity + 10; // No resources should expire.
            var pool = new ResourcePool<Key, Resource>(new Context(TestGlobal.Logger), maxResourceCount: maxCapacity, maxAgeMinutes, resourceFactory: _ => new Resource(), clock);

            var resources = new List<Resource>();
            foreach (var num in Enumerable.Range(1, maxCapacity))
            {
                var key = new Key(num);
                using var wrapper = await pool.CreateAsync(key);
                resources.Add((wrapper.Value));
                clock.UtcNow += TimeSpan.FromMinutes(1); // First resource will be the oldest.
            }

            Assert.Equal(0, pool.Counter[ResourcePoolCounters.Cleaned].Value);
            Assert.Equal(maxCapacity, pool.Counter[ResourcePoolCounters.Created].Value);

            using (var wrapper = await pool.CreateAsync(new Key(maxCapacity + 1)))
            {
                resources.Add((wrapper.Value));
            }

            Assert.Equal(1, pool.Counter[ResourcePoolCounters.Cleaned].Value);
            Assert.Equal(maxCapacity + 1, pool.Counter[ResourcePoolCounters.Created].Value);

            foreach (var client in resources.Skip(1))
            {
                Assert.False(client.ShutdownStarted);
            }

            Assert.True(resources.First().ShutdownCompleted);
        }

        [Fact]
        public async Task IssueSameClientManyTimes()
        {
            var clock = new MemoryClock();
            var pool = new ResourcePool<Key, Resource>(new Context(TestGlobal.Logger), maxResourceCount: 1, maxAgeMinutes: 1, resourceFactory: _ => new Resource(), clock);
            var key = new Key(1);

            using var originalWrapper = await pool.CreateAsync(key);
            var originalResource = originalWrapper.Value;

            for (var i = 0; i < 1000; i++)
            {
                using var newWrapper = await pool.CreateAsync(key);
                Assert.Same(newWrapper.Value, originalResource);
            }
        }

        [Fact]
        public async Task FillCacheWithoutRemovingClients()
        {
            var maxCapacity = 10;
            var clock = new MemoryClock();
            var pool = new ResourcePool<Key, Resource>(new Context(TestGlobal.Logger), maxResourceCount: maxCapacity, maxAgeMinutes: 1, resourceFactory: _ => new Resource(), clock);

            for (var i = 0; i < maxCapacity; i++)
            {
                using var wrapper = await pool.CreateAsync(new Key(i));
            }

            // Created all resources
            Assert.Equal(maxCapacity, pool.Counter.GetCounterValue(ResourcePoolCounters.Created));

            // Zero resources were cleaned
            Assert.Equal(0, pool.Counter.GetCounterValue(ResourcePoolCounters.Cleaned));

            // Zero resources were reused
            Assert.Equal(0, pool.Counter.GetCounterValue(ResourcePoolCounters.Reused));
        }

        [Fact]
        public async Task CreateFailsAfterDispose()
        {
            var capacity = 2;
            var context = new Context(TestGlobal.Logger);
            var pool = new ResourcePool<Key, Resource>(context, maxResourceCount: capacity, maxAgeMinutes: 1, resourceFactory: _ => new Resource());

            pool.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                using var obj1 = await pool.CreateAsync(new Key(0));
            });
        }
    }
}
