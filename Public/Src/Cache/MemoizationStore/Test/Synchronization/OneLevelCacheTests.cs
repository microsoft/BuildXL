// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Synchronization
{
    public class OneLevelCacheTests : TestWithOutput
    {
        /// <nodoc />
        public OneLevelCacheTests(ITestOutputHelper output)
        : base(output)
        {
        }

        [Fact]
        public async Task ShouldNotFailIfCreateAndStartStoresReturnsFailure()
        {
            // Arrange
            var fixture = new OneLevelCacheFixture();
            // CreateAndStartStoreAsync may fail, but this should not cause any contract exceptions.
            var sut = fixture
                .WithCreateAndStartStoreFunc(() => (contentStoreResult: new BoolResult("error 1"), memoizationStoreResult: new BoolResult("error 2")))
                // It is possible that the stores are not initialized if CreateAndStartStoreAsync operation fails.
                .WithContentStore(null)
                .WithMemoizationStore(null)
                .CreateSut();

            // Act
            var startupResult = await sut.StartupAsync(fixture.Context);

            // Assert
            startupResult.ShouldBeError();
            startupResult.ToString().Should().NotContain("ContractException");
            // Both errors must be presented in the output.
            startupResult.ToString().Should().Contain("error 1");
            startupResult.ToString().Should().Contain("error 2");
        }

        [Fact]
        public async Task StoreStartupFailuresShouldPropagateBack()
        {
            // Arrange
            var fixture = new OneLevelCacheFixture();
            var sut = fixture
                .WithContentStoreStartupResult(new BoolResult("error 1"))
                .WithMemoizationStoreStartupResult(new BoolResult("error 2"))
                .CreateSut();

            // Act
            var startupResult = await sut.StartupAsync(fixture.Context);

            // Assert
            startupResult.ShouldBeError();
            // Both errors must be presented in the output.
            startupResult.ToString().Should().Contain("error 1");
            startupResult.ToString().Should().Contain("error 2");
        }

        [Fact]
        public async Task StoreShutdownFailuresShouldPropagateBack()
        {
            // Arrange
            var fixture = new OneLevelCacheFixture();
            var sut = fixture
                .WithContentStoreShutdownResult(new BoolResult("error 1"))
                .WithMemoizationStoreShutdownResult(new BoolResult("error 2"))
                .CreateSut();

            // Act
            var startupResult = await sut.StartupAsync(fixture.Context);
            startupResult.ShouldBeSuccess();
            var shutdownResult = await sut.ShutdownAsync(fixture.Context);

            // Assert
            // Both errors must be presented in the output.
            shutdownResult.ToString().Should().Contain("error 1");
            shutdownResult.ToString().Should().Contain("error 2");
        }

        [Fact]
        public async Task StoreShutdownFailuresShouldPropagateBackWhenStartupFailed()
        {
            // Arrange
            var fixture = new OneLevelCacheFixture();
            var sut = fixture
                // When startup fails for the store, the shutdown operation is not called.
                // Setting up the scenario when the store 1 fails to startup but the store 2 fails to shutdown
                .WithContentStoreStartupResult(new BoolResult("content store startup error 1"))
                .WithMemoizationStoreShutdownResult(new BoolResult("memoization store shutdown error 2"))
                .CreateSut();

            // Act
            var startupResult = await sut.StartupAsync(fixture.Context);
            startupResult.ShouldBeError();

            // Assert
            // Both errors must be presented in the output.
            startupResult.ToString().Should().Contain("content store startup error 1");
            startupResult.ToString().Should().Contain("memoization store shutdown error 2");
        }
    }
}
