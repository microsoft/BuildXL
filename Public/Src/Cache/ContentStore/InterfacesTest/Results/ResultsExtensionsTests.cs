// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ResultsExtensionsTests
    {
        [Fact]
        public async Task ThenAsyncIsCalledOnSuccessCase()
        {
            var success = Task.FromResult(Result.Success(1));
            bool thenAsyncCalled = false;

            var result = await success.ThenAsync(
                async r =>
                {
                    thenAsyncCalled = true;
                    await Task.Yield();
                    return Result.Success(2);
                });

            Assert.True(thenAsyncCalled);
            Assert.Equal(2, result.Value);
        }

        [Fact]
        public async Task ThenAsyncIsNotCalledOnCanceled()
        {
            var canceledResult = Result.Success(1);
            canceledResult.MarkCancelled();

            var canceled = Task.FromResult(canceledResult);
            
            bool thenAsyncCalled = false;

            var result = await canceled.ThenAsync(
                async r =>
                {
                    thenAsyncCalled = true;
                    await Task.Yield();
                    return Result.Success(2);
                });

            Assert.False(thenAsyncCalled);
            Assert.True(result.IsCancelled);
        }

        [Fact]
        public async Task ThenAsyncIsNotCalledOnError()
        {
            var originalResult = Task.FromResult(Result.FromErrorMessage<int>("Error"));
            bool thenAsyncCalled = false;

            var result = await originalResult.ThenAsync(
                async r =>
                {
                    thenAsyncCalled = true;
                    await Task.Yield();
                    return Result.Success(2);
                });

            Assert.False(thenAsyncCalled);
            Assert.False(result.Succeeded);
            Assert.Equal("Error", result.ErrorMessage);
        }
    }
}
