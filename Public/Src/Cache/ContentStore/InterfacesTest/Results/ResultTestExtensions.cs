// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public static class ResultTestExtensions
    {
        public static BoolResult ShouldBeError(this BoolResult result, string expectedMessageFragment = null)
        {
            Assert.NotNull(result);
            Assert.NotNull(result.ErrorMessage);
            if (expectedMessageFragment != null)
            {
                Assert.Contains(expectedMessageFragment, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }

            Assert.False(result.Succeeded, $"The operation should fail but was successful.");

            return result;
        }
        
        public static async Task<T> ShouldBeError<T>(this Task<T> result, string expectedMessageFragment = null) where T: BoolResult
        {
            var r = await result;
            r.ShouldBeError(expectedMessageFragment);
            return r;
        }

        public static OpenStreamResult ShouldBeSuccess(this OpenStreamResult result)
        {
            Assert.NotNull(result);
            Assert.Null(result.ErrorMessage);
            Assert.True(result.Succeeded, $"OpenStream operation should succeed, but it failed. Error: {result.ErrorMessage}. Diagnostics: {result.Diagnostics}");

            return result;
        }

        public static async Task<OpenStreamResult> ShouldBeSuccess(this Task<OpenStreamResult> task)
        {
            var result = await task;
            result.ShouldBeSuccess();
            return result;
        }

        public static async Task<OpenStreamResult> ShouldNotBeError(this Task<OpenStreamResult> task)
        {
            var result = await task;
            Assert.True(result.Code != OpenStreamResult.ResultCode.Error, $"The operation should success, but failed. Error: {result}.");
            return result;
        }

        public static PlaceFileResult ShouldBeSuccess(this PlaceFileResult result)
        {
            Assert.NotNull(result);
            Assert.Null(result.ErrorMessage);
            Assert.True(result.Succeeded, $"Place file operation should succeed, but it failed. Error: {result.ErrorMessage}. Diagnostics: {result.Diagnostics}");

            return result;
        }

        public static async Task<PlaceFileResult> ShouldBeSuccess(this Task<PlaceFileResult> task)
        {
            var result = await task;
            result.ShouldBeSuccess();
            return result;
        }

        public static async Task<PlaceFileResult> ShouldBeError(this Task<PlaceFileResult> task)
        {
            var result = await task;
            
            Assert.False(result.Succeeded);
            return result;
        }

        public static OpenStreamResult ShouldBeNotFound(this OpenStreamResult result)
        {
            Assert.Equal(OpenStreamResult.ResultCode.ContentNotFound, result.Code);
            Assert.Null(result.Stream);
            return result;
        }

        public static OpenStreamResult ShouldBeCancelled(this OpenStreamResult result)
        {
            Assert.Equal(OpenStreamResult.ResultCode.Error, result.Code);
            Assert.Null(result.Stream);
            Assert.Contains("canceled", result.ToString());
            return result;
        }

        public static async Task<OpenStreamResult> ShouldBeNotFound(this Task<OpenStreamResult> result)
        {
            var r = await result;
            r.ShouldBeNotFound();
            return r;
        }

        public static TResult ShouldBeSuccess<TResult>(this TResult result)
            where TResult : ResultBase
        {
            Assert.NotNull(result);
            Assert.True(result.Succeeded, $"{typeof(TResult).Name} operation should succeed, but it failed. Error: {result.ErrorMessage}. Diagnostics: {result.Diagnostics}");
            return result;
        }

        public static async Task<TOut> SelectResult<TIn, TOut>(this Task<TIn> task, Func<TIn, TOut> select)
        {
            var input = await task;
            return select(input);
        }

        public static async Task<TResult> ShouldBeSuccess<TResult>(this Task<TResult> task)
            where TResult : BoolResult
        {
            var result = await task;
            result.ShouldBeSuccess();
            return result;
        }

        public static async Task<PinResult> ShouldBeSuccess(this Task<PinResult> task)
        {
            var result = await task;
            result.ShouldBeSuccess();
            return result;
        }

        public static PinResult ShouldBeSuccess(this PinResult result)
        {
            Assert.True(result.Succeeded, $"Pin operation should succeed, but it failed. {result}");
            return result;
        }

        public static async Task<PinResult> ShouldBeContentNotFound(this Task<PinResult> task)
        {
            var result = await task;
            result.ShouldBeContentNotFound();
            return result;
        }

        public static PinResult ShouldBeContentNotFound(this PinResult result)
        {
            Assert.True(result.Code == PinResult.ResultCode.ContentNotFound, $"Pin operation should not find content, but it had expected result: {result}");
            return result;
        }
    }
}
