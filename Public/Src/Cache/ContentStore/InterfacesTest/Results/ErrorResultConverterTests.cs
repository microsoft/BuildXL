// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ErrorResultConverterTests
    {
        [Fact]
        public void TestConverionFromException()
        {
            var exception = new Exception("My message");
            var error = new ErrorResult(exception).AsResult<BoolResult>();
            Assert.False(error.Succeeded);
            Assert.Contains("My message", error.ErrorMessage);
        }

        [Fact]
        public void AsResultShouldFailWithDescriptiveMessageIfResultDoesNotHaveTheRightConstructor()
        {
            var exception = new Exception("My message");
            var e = Assert.Throws<InvalidOperationException>(() => new ErrorResult(exception).AsResult<CustomError>());
            Assert.Contains("Constructor 'CustomError(ResultBase, string)' is not defined for type", e.Message);
        }

        [Fact]
        public void AsResult_Should_Preserve_IsCancelled_Flag()
        {
            var exception = new TaskCanceledException();
            var boolResult = new BoolResult(exception);
            boolResult.IsCancelled = true;
            Assert.True(boolResult.IsCancelled);

            var pinResult = new ErrorResult(boolResult).AsResult<PinResult>();
            Assert.True(pinResult.IsCancelled);
        }

        [Fact]
        public void AsResult_Should_Preserve_IsCritical_Flag()
        {
            var exception = new NullReferenceException();
            var boolResult = new BoolResult(exception);
            Assert.True(boolResult.IsCriticalFailure);

            var pinResult = new ErrorResult(boolResult).AsResult<PinResult>();
            Assert.True(pinResult.IsCriticalFailure);
        }

        private class CustomError : ResultBase
        {
        }
    }
}
