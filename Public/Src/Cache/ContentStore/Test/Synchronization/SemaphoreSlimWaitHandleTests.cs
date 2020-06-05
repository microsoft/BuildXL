// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Synchronization
{
    public class SemaphoreSlimWaitHandleTests : TestBase
    {
        public SemaphoreSlimWaitHandleTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task Wait()
        {
            using (var sempahore = new SemaphoreSlim(1, 1))
            {
                using (await sempahore.WaitTokenAsync())
                {
                }
            }
        }

        [Fact]
        public async Task GetHashCodeThrows()
        {
            using (var sempahore = new SemaphoreSlim(2, 2))
            {
                using (SemaphoreSlimToken waitToken = await sempahore.WaitTokenAsync())
                {
                    Action a = () => waitToken.GetHashCode().Should().BePositive();
                    a.Should().Throw<InvalidOperationException>();
                }
            }
        }

        [Fact]
        public async Task EqualsThrows()
        {
            using (var sempahore = new SemaphoreSlim(2, 2))
            {
                using (SemaphoreSlimToken waitToken1 = await sempahore.WaitTokenAsync(),
                    waitToken2 = await sempahore.WaitTokenAsync())
                {
                    Action a = () => (waitToken1 == waitToken2).Should().BeTrue();
                    a.Should().Throw<InvalidOperationException>();
                }
            }
        }

        [Fact]
        public async Task ObjectEqualsThrows()
        {
            using (var sempahore = new SemaphoreSlim(2, 2))
            {
                using (SemaphoreSlimToken waitToken1 = await sempahore.WaitTokenAsync(),
                    waitToken2 = await sempahore.WaitTokenAsync())
                {
                    Action a = () => waitToken1.Equals(waitToken2).Should().BeTrue();
                    a.Should().Throw<InvalidOperationException>();
                }
            }
        }

        [Fact]
        public async Task NotEqualsThrows()
        {
            using (var sempahore = new SemaphoreSlim(2, 2))
            {
                using (SemaphoreSlimToken waitToken1 = await sempahore.WaitTokenAsync(),
                    waitToken2 = await sempahore.WaitTokenAsync())
                {
                    Action a = () => (waitToken1 != waitToken2).Should().BeTrue();
                    a.Should().Throw<InvalidOperationException>();
                }
            }
        }
    }
}
