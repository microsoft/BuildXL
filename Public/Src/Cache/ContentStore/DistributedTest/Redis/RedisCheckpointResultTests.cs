// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using ContentStoreTest.Distributed.ContentLocation;
using StackExchange.Redis;
using Xunit;

namespace ContentStoreTest.Distributed.Redis
{
    public class RedisCheckpointResultTests
    {
        [Fact]
        public void TestParsingLogic()
        {
            var checkpoints = new[]
            {
                new RedisCheckpointInfo(
                    "cid42",
                    32523,
                    DateTime.UtcNow,
                    "machine1",
                    slotNumber: 123),
                new RedisCheckpointInfo(
                    "mycheckpoint",
                    2,
                    DateTime.FromFileTimeUtc(9520329320),
                    "m02",
                    slotNumber: 0)
            };

            List<HashEntry> entries = new List<HashEntry>();
            foreach (var checkpoint in checkpoints)
            {
                AddHashEntries(entries, checkpoint);
            }

            var parsedCheckpoints = RedisCheckpointInfo.ParseCheckpoints(entries.ToArray());

            Assert.Equal(checkpoints.Length, parsedCheckpoints.Length);
            Assert.Equal(checkpoints, parsedCheckpoints, new RedisCheckpointInfoEqualityComparer());
        }

        private void AddHashEntries(List<HashEntry> entries, RedisCheckpointInfo checkpoint)
        {
            Assert.True(checkpoint.SlotNumber >= 0);

            entries.AddRange(new[]
            {
                new HashEntry($"Slot#{checkpoint.SlotNumber}.CheckpointId", checkpoint.CheckpointId),
                new HashEntry($"Slot#{checkpoint.SlotNumber}.SequenceNumber", checkpoint.SequenceNumber),
                new HashEntry($"Slot#{checkpoint.SlotNumber}.CheckpointCreationTime", checkpoint.CheckpointCreationTime.ToFileTimeUtc()),
                new HashEntry($"Slot#{checkpoint.SlotNumber}.MachineName", checkpoint.MachineName),
            });
        }
    }
}
