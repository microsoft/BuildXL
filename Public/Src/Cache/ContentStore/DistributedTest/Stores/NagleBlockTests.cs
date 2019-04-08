// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Stores
{
    [Obsolete("NagleQueue should be used instead.")]
    public class NagleBlockTests : TestBase
    {
        public NagleBlockTests()
            : base(TestGlobal.Logger)
        {
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        public async Task PassItems(int itemCount)
        {
            var items = Enumerable.Range(0, itemCount).Select(i => ContentHash.Random()).ToList();
            var results = new List<ContentHash>();

            var testActionBlock = new ActionBlock<ContentHash[]>(contentHashes =>
            {
                results.AddRange(contentHashes);
            });

            var nagleBlock = new NagleBlock<ContentHash>();
            await SendAndCompleteAsync(items, nagleBlock, testActionBlock);

            new HashSet<ContentHash>(items).SetEquals(results).Should().BeTrue();
        }

        [Fact]
        public async Task ItemsPassedInBatches()
        {
            const int itemCount = 2;
            var results = new List<List<ContentHash>>();
            var randomContentHashes = Enumerable.Range(0, itemCount).Select(i => ContentHash.Random()).ToList();
            var testActionBlock = new ActionBlock<ContentHash[]>(contentHashes =>
            {
                results.Add(contentHashes.ToList());
            });

            var nagleBlock = new NagleBlock<ContentHash>(1);
            await SendAndCompleteAsync(randomContentHashes, nagleBlock, testActionBlock);

            results.Count.Should().Be(2);
            var mergedResults = results.SelectMany(x => x).ToList();
            mergedResults.Count.Should().Be(itemCount);
            new HashSet<ContentHash>(randomContentHashes).SetEquals(mergedResults).Should().BeTrue();
        }

        private async Task SendAndCompleteAsync(
            IList<ContentHash> items, NagleBlock<ContentHash> nagleBlock, ActionBlock<ContentHash[]> actionBlock)
        {
            nagleBlock.LinkTo(actionBlock);
            foreach (var item in items)
            {
                await nagleBlock.SendAsync(item);
            }

            nagleBlock.Complete();
            await nagleBlock.Completion;

            actionBlock.Complete();
            await actionBlock.Completion;
        }

        [Fact]
        public Task ConstructAndComplete()
        {
            var nagleBlock = new NagleBlock<ContentHash>();
            nagleBlock.Complete();
            return nagleBlock.Completion;
        }
    }
}
