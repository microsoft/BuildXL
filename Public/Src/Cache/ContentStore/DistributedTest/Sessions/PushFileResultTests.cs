// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Service.Grpc;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Sessions
{
    public class PushFileResultTests
    {
        [Fact]
        public void Unavailable()
        {
            var result = PushFileResult.ServerUnavailable();
            result.Succeeded.Should().BeFalse();

            result.ToString().Should().Contain("Unavailable");
        }

        [Fact]
        public void Rejected()
        {
            var result = PushFileResult.Rejected();
            result.Succeeded.Should().BeFalse();

            result.ToString().Should().Contain("Rejected");
        }


        [Fact]
        public void Disabled()
        {
            var result = PushFileResult.Disabled();
            result.Succeeded.Should().BeFalse();

            result.ToString().Should().Contain("Disabled");
        }

        [Fact]
        public void Success()
        {
            var result = PushFileResult.PushSucceeded();
            result.Succeeded.Should().BeTrue();

            result.ToString().Should().Contain("Success");
        }
    }
}
