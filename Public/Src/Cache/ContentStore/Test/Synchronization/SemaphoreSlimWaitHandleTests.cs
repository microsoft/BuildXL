// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
                using (await sempahore.WaitToken())
                {
                }
            }
        }

        [Fact]
        public async Task GetHashCodeThrows()
        {
            using (var sempahore = new SemaphoreSlim(2, 2))
            {
                using (SemaphoreSlimToken waitToken = await sempahore.WaitToken())
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
                using (SemaphoreSlimToken waitToken1 = await sempahore.WaitToken(),
                    waitToken2 = await sempahore.WaitToken())
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
                using (SemaphoreSlimToken waitToken1 = await sempahore.WaitToken(),
                    waitToken2 = await sempahore.WaitToken())
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
                using (SemaphoreSlimToken waitToken1 = await sempahore.WaitToken(),
                    waitToken2 = await sempahore.WaitToken())
                {
                    Action a = () => (waitToken1 != waitToken2).Should().BeTrue();
                    a.Should().Throw<InvalidOperationException>();
                }
            }
        }
    }
}
