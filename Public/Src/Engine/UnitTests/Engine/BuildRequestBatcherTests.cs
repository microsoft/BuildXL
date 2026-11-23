// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Engine.Distribution;
using Xunit;

namespace Test.BuildXL.Engine
{
    public sealed class BuildRequestBatcherTests
    {
        [Theory]
        // Admission stops once the admitted size reaches the target.
        [InlineData(new[] { 100, 100, 100, 100 }, new int[0], 250, new[] { 0, 1, 2 })]
        // The first item is always admitted, even if it exceeds the target on its own.
        [InlineData(new[] { 1000, 10, 10 }, new int[0], 100, new[] { 0 })]
        // Items that cannot be deferred (e.g., pips that carry no hashes) are always admitted.
        [InlineData(new[] { 100, 100, 1, 100, 1 }, new[] { 2, 4 }, 100, new[] { 0, 2, 4 })]
        // A non-positive target admits all items.
        [InlineData(new[] { 100, 100, 100 }, new int[0], 0, new[] { 0, 1, 2 })]
        public void SelectBySizeAdmitsItemsUpToTarget(int[] sizes, int[] nonDeferrable, long targetBytes, int[] expectedAdmitted)
        {
            var batcher = new BuildRequestBatcher<int>();
            var prepared = new List<int>();

            batcher.StartBatch(maxCount: 1000);
            batcher.Batch.AddRange(Enumerable.Range(0, sizes.Length));

            int numDeferred = SelectBySize(batcher, sizes, nonDeferrable, targetBytes, prepared);

            Assert.Equal(expectedAdmitted, batcher.Batch);
            // Only the admitted items are prepared: preparing an item extracts its hashes, which must be sent in the same message.
            Assert.Equal(expectedAdmitted, prepared);
            Assert.Equal(sizes.Length - expectedAdmitted.Length, numDeferred);
            Assert.Equal(numDeferred, batcher.DeferredCount);
        }

        [Fact]
        public void DeferredItemsKeepQueueOrder()
        {
            var sizes = Enumerable.Repeat(100, 6).ToArray();
            var batcher = new BuildRequestBatcher<int>();

            // Round 1: items 0-4 are queued, only 0 and 1 fit.
            batcher.StartBatch(maxCount: 10);
            Assert.Empty(batcher.Batch);
            batcher.Batch.AddRange(new[] { 0, 1, 2, 3, 4 });
            SelectBySize(batcher, sizes, new int[0], 150, new List<int>());
            Assert.Equal(new[] { 0, 1 }, batcher.Batch);
            Assert.Equal(3, batcher.DeferredCount);

            // Round 2: the batch is limited by count, so only 2 and 3 are taken from the deferred items (4 stays deferred).
            // Item 5 is queued afterwards. Only 2 fits, so 3 must go back ahead of 4, and 5 behind it.
            batcher.StartBatch(maxCount: 2);
            Assert.Equal(new[] { 2, 3 }, batcher.Batch);
            batcher.Batch.Add(5);
            SelectBySize(batcher, sizes, new int[0], 50, new List<int>());
            Assert.Equal(new[] { 2 }, batcher.Batch);
            Assert.Equal(3, batcher.DeferredCount);

            // Round 3: the remaining items are sent in queue order.
            batcher.StartBatch(maxCount: 10);
            SelectBySize(batcher, sizes, new int[0], 0, new List<int>());
            Assert.Equal(new[] { 3, 4, 5 }, batcher.Batch);
            Assert.False(batcher.HasDeferred);
            Assert.Equal(0, batcher.DeferredCount);
        }

        private static int SelectBySize(BuildRequestBatcher<int> batcher, int[] sizes, int[] nonDeferrable, long targetBytes, List<int> prepared)
        {
            return batcher.SelectBySize(
                canDefer: item => !nonDeferrable.Contains(item),
                prepare: item =>
                {
                    prepared.Add(item);
                    return sizes[item];
                },
                targetBytes: targetBytes,
                // Prepare the items sequentially so that the admitted items are deterministic.
                maxDegreeOfParallelism: 1);
        }
    }
}
