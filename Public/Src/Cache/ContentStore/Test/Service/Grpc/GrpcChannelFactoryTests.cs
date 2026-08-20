// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using System.Net;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Service.Grpc;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Service.Grpc
{
    public class GrpcChannelFactoryTests
    {
        private const int Port = 12345;

        [Fact]
        public void UsesLoopbackForCurrentMachine()
        {
            var target = GetTarget(Environment.MachineName);

            target.Host.Should().Be(IPAddress.Loopback.ToString());
            target.Port.Should().Be(Port);
            target.Encrypted.Should().BeFalse();
        }

        [Fact]
        public void CurrentMachineComparisonIsCaseInsensitive()
        {
            var location = MachineLocation.Create("currentmachine", Port);
            const string currentMachineName = "CURRENTMACHINE";

            Assert.NotEqual(currentMachineName, location.ToGrpcHost().Host);

            var target = GrpcChannelFactory.GetGrpcDotNetTarget(location, currentMachineName);

            target.Host.Should().Be(IPAddress.Loopback.ToString());
        }

        [Fact]
        public void PreservesEncryptedSchemeAndPort()
        {
            var location = MachineLocation.Parse($"grpcs://{Environment.MachineName}:{Port}/");

            var target = GetTarget(location);

            target.Host.Should().Be(IPAddress.Loopback.ToString());
            target.Port.Should().Be(Port);
            target.Encrypted.Should().BeTrue();
        }

        [Fact]
        public void LeavesRemoteMachineUnchanged()
        {
            var remoteMachine = $"{Environment.MachineName}-remote";

            var target = GetTarget(remoteMachine);

            target.Host.Should().BeEquivalentTo(remoteMachine);
        }

        private static MachineLocation.HostInfo GetTarget(string machineName)
        {
            return GetTarget(MachineLocation.Create(machineName, Port));
        }

        private static MachineLocation.HostInfo GetTarget(MachineLocation location)
        {
            return GrpcChannelFactory.GetGrpcDotNetTarget(location);
        }
    }
}

#endif
