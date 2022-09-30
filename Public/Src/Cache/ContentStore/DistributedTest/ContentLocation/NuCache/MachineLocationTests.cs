// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class MachineLocationTests
    {
        [Theory]
        [InlineData(null, null)]
        [InlineData(@"\\DS4PNPF000066FA\D$\DBS\CACHE\CONTENTADDRESSABLESTORE\SHARED", @"\\DS4PNPF000066FA\D$\dbs\CACHE\CONTENTADDRESSABLESTORE\SHARED")]
        [InlineData("grpc://a.com:123", "grpc://A.CoM:123")]
        public void EqualityTests(string lhs, string rhs)
        {
            var left = lhs is null ? new MachineLocation() : new MachineLocation(lhs);
            var right = rhs is null ? new MachineLocation() : new MachineLocation(rhs);

            left.Equals(left).Should().BeTrue();
#pragma warning disable CS1718 // Comparison made to same variable
            (left == left).Should().BeTrue();
#pragma warning restore CS1718 // Comparison made to same variable

            right.Equals(right).Should().BeTrue();
#pragma warning disable CS1718 // Comparison made to same variable
            (right == right).Should().BeTrue();
#pragma warning restore CS1718 // Comparison made to same variable

            left.Equals(right).Should().BeTrue();
            right.Equals(left).Should().BeTrue();
            (left == right).Should().BeTrue();
            (right == left).Should().BeTrue();
        }

        [Theory]
        [InlineData("grpc://a.com:123", "a.com", 123)]
        [InlineData(@"\\DS4PNPF000066FA\D$\DBS\CACHE\CONTENTADDRESSABLESTORE\SHARED", "DS4PNPF000066FA", null)]
        public void MachineLocationGrpcExtractionWorks(string location, string host, int? port)
        {
            var machine = new MachineLocation(location);
            var (extractedHost, extractedPort) = machine.ExtractHostInfo();
            extractedHost.Should().BeEquivalentTo(host);
            extractedPort.Should().Be(port);
        }
    }
}
