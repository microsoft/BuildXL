// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public class PinContextTests : TestBase
    {
        public PinContextTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task DisposeCallsUnpinHashesAction()
        {
            var calledOnDispose = false;
            var context = new Context(Logger);
            using (var taskTracker = new BackgroundTaskTracker(nameof(PinContextTests), context))
            {
                try
                {
                    using (new PinContext(taskTracker, contentHashes => { calledOnDispose = true; }))
                    {
                    }
                }
                finally
                {
                    await taskTracker.ShutdownAsync(context);
                }
            }

            Assert.True(calledOnDispose);
        }

        [Fact]
        public async Task AddPinThrowsOnDisposedPinContext()
        {
            var context = new Context(Logger);
            using (var taskTracker = new BackgroundTaskTracker(nameof(PinContextTests), context))
            {
                try
                {
                    var pinContext = new PinContext(taskTracker, contentHashes => { });
                    pinContext.AddPin(ContentHash.Random());
                    await pinContext.DisposeAsync();
                    Action addPinAction = () => pinContext.AddPin(ContentHash.Random());
                    addPinAction.Should().Throw<ObjectDisposedException>();
                }
                finally
                {
                    await taskTracker.ShutdownAsync(context);
                }
            }
        }

        [Fact]
        public async Task AddPinAccumulatesDuplicates()
        {
            var context = new Context(Logger);
            using (var taskTracker = new BackgroundTaskTracker(nameof(PinContextTests), context))
            {
                try
                {
                    ContentHash hash1 = ContentHash.Random();
                    ContentHash hash2 = ContentHash.Random();
                    var hashCounts = new ConcurrentDictionary<ContentHash, int>
                    {
                        [hash1] = 0,
                        [hash2] = 0
                    };

                    using (var pinContext = new PinContext(taskTracker, pinCounts =>
                    {
                        foreach (var pinCount in pinCounts)
                        {
                            hashCounts[pinCount.Key] = pinCount.Value;
                        }
                    }))
                    {
                        pinContext.AddPin(hash2);
                        pinContext.AddPin(hash1);
                        pinContext.AddPin(hash2);
                    }

                    hashCounts[hash1].Should().Be(1);
                    hashCounts[hash2].Should().Be(2);
                }
                finally
                {
                    await taskTracker.ShutdownAsync(context);
                }
            }
        }
    }
}
