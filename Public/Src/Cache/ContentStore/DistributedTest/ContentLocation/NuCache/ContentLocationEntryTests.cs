// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ContentLocationEntryTests
    {
        [Fact]
        public void TestRoundtripRedisValue()
        {
            Random r = new Random();
            for (int machineIdIndex = 0; machineIdIndex < 2048; machineIdIndex++)
            {
                long randomSize = (long)Math.Pow(2, 63 * r.NextDouble());
                byte[] entryBytes = ContentLocationEntry.ConvertSizeAndMachineIdToRedisValue(randomSize, new MachineId(machineIdIndex));

                var deserializedEntry = ContentLocationEntry.FromRedisValue(entryBytes, DateTime.UtcNow, missingSizeHandling: true);
                deserializedEntry.ContentSize.Should().Be(randomSize);
                deserializedEntry.Locations[machineIdIndex].Should().BeTrue();
            }
        }
    }
}
