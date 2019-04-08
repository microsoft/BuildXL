// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ResultPropagatingExceptionTests
    {
        [Fact]
        public void ResultPropagationShouldPreserveOriginalException()
        {
            var exception = new NullReferenceException();
            var result = new BoolResult(exception);
            Assert.True(result.IsCriticalFailure);
            Assert.False(result.IsCancelled);

            var resultPropagation = new ResultPropagationException(result);
            var result2 = new PinResult(resultPropagation);
            Assert.True(result2.IsCriticalFailure);
            Assert.False(result2.IsCancelled);

            Assert.Equal(exception, result2.Exception);
        }

        [Fact]
        public void ResultPropagationShouldPreserveIsCancelledProperty()
        {
            var exception = new InvalidOperationException();
            var result = new BoolResult(exception);
            result.IsCancelled = true;
            Assert.False(result.IsCriticalFailure);
            Assert.True(result.IsCancelled);

            var resultPropagation = new ResultPropagationException(result);
            var result2 = new PinResult(resultPropagation);
            Assert.False(result2.IsCriticalFailure);
            Assert.True(result2.IsCancelled);

            Assert.Equal(exception, result2.Exception);
        }
    }
}
