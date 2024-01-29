// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache;

public class MachineLocationTests
{
    [Fact]
    [Trait("Category", "WindowsOSOnly")]
    public void TestLocalWindowsPath()
    {
        AssertHostNameEquality("localhost", GetHostFromAbsolutePath(new AbsolutePath(@"C:\absolute\path")));
    }

    [Fact]
    [Trait("Category", "WindowsOSOnly")]
    public void TestRemoteWindowsPath()
    {
        AssertHostNameEquality("TestMachineName", GetHostFromAbsolutePath(new AbsolutePath(@"\\TestMachineName\absolute\path")));
    }

    [Fact]
    public void TestLocalLinuxPath()
    {
        if (!OperatingSystemHelper.IsWindowsOS)
        {
            AssertHostNameEquality("localhost", GetHostFromAbsolutePath(new AbsolutePath(@"/localhost/absolute/path")));
        }
    }

    [Fact]
    public void TestRemoteLinuxPath()
    {
        if (!OperatingSystemHelper.IsWindowsOS)
        {
            AssertHostNameEquality("TestMachineName", GetHostFromAbsolutePath(new AbsolutePath(@"/TestMachineName/absolute/path")));
        }
    }

    private void AssertHostNameEquality(string lhs, string rhs)
    {
        var lhsb = lhs.ToLowerInvariant();
        var rhsb = rhs.ToLowerInvariant();
        lhsb.Should().BeEquivalentTo(rhsb, $"`{lhs}` should be equal to `{rhs}` (lowercase)");
    }

    private string GetHostFromAbsolutePath(AbsolutePath path)
    {
        return MachineLocation.Parse(path.Path).ExtractHostPort().host;
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(@"\\DS4PNPF000066FA\D$\DBS\CACHE\CONTENTADDRESSABLESTORE\SHARED", @"\\DS4PNPF000066FA\D$\dbs\CACHE\CONTENTADDRESSABLESTORE\SHARED")]
    [InlineData("grpc://a.com:123", "grpc://A.CoM:123")]
    public void EqualityTests(string lhs, string rhs)
    {
        var left = MachineLocation.Parse(lhs);
        var right = MachineLocation.Parse(rhs);

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
    [InlineData(@"\\DS4PNPF000066FA\D$\DBS\CACHE\CONTENTADDRESSABLESTORE\SHARED", "DS4PNPF000066FA", 7089)]
    [InlineData(@"node1:1234", "node1", 1234)]
    [InlineData(@"node1", "node1", 7089)]
    public void MachineLocationGrpcExtractionWorks(string location, string host, int? port)
    {
        var machine = MachineLocation.Parse(location);
        var (extractedHost, extractedPort) = machine.ExtractHostPort();
        extractedHost.Should().BeEquivalentTo(host, $"expected to extract host {host} from URI {machine.Uri}");
        extractedPort.Should().Be(port);
    }

    [Fact]
    public void GuidIsValidMachineLocation()
    {
        // As long as this doesn't throw, it's a success.
        MachineLocation.Parse(Guid.NewGuid().ToString());
    }
}
