// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using BuildXL.Cache.Monitor.Library.Rules.Autoscaling;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Monitor.Test
{
    public class RedisTypesObservationalEqualityTests : TestBase
    {
        public RedisTypesObservationalEqualityTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void RedisTierTest()
        {
            foreach (var size1 in RedisTier.Instances.AsIndexed())
            {
                foreach (var size2 in RedisTier.Instances.AsIndexed())
                {
                    if (size1.Index == size2.Index)
                    {
                        size1.Item.Should().Be(size2.Item);
                    }
                    else
                    {
                        size1.Item.Should().NotBe(size2.Item);
                    }
                }
            }
        }

        [Fact]
        public void RedisClusterSizeTest()
        {
            foreach (var size1 in RedisClusterSize.Instances.AsIndexed())
            {
                foreach (var size2 in RedisClusterSize.Instances.AsIndexed())
                {
                    if (size1.Index == size2.Index)
                    {
                        size1.Item.Should().Be(size2.Item);
                    }
                    else
                    {
                        size1.Item.Should().NotBe(size2.Item);
                    }
                }
            }
        }
    }
}
