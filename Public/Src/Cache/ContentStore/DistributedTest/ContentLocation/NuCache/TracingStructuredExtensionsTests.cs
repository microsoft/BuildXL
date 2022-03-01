// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class TracingStructuredExtensionsTests
    {
        [Fact]
        public void GetShortHashesTraceStringForInactiveMachinesTests()
        {
            var hash = ContentHash.Random();
            var machines = Enumerable.Range(1, 1000).Select(n => new MachineLocation($"Machine {n}")).ToList();
            GetBulkLocationsResult result = new GetBulkLocationsResult(
                new List<ContentHashWithSizeAndLocations>()
                {
                    new ContentHashWithSizeAndLocations(
                        hash,
                        size: 42,
                        new List<MachineLocation>() {new MachineLocation("Machine1")},
                        filteredOutLocations: machines)
                });

            var s = result.GetShortHashesTraceStringForInactiveMachines();
            s.Should().Contain(hash.ToShortString());
            s.Should().Contain(machines[0].ToString());
        }
    }
}
