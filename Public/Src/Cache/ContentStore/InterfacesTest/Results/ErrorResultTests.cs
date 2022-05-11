// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ErrorResultTests
    {
        [Fact]
        public void ResultPropagationShouldPreserveOriginalException()
        {
            var errorResult1 = new ErrorResult(new NullReferenceException()).AsResult<BoolResult>();
            Assert.False(errorResult1.Succeeded);

            var errorResult2 = new ErrorResult(new ResultPropagationException(errorResult1)).AsResult<BoolResult>();
            var errorResult3 = new ErrorResult(new ResultPropagationException(errorResult2)).AsResult<BoolResult>();

            Assert.False(errorResult3.Succeeded);
            // The error message should not be repeated multiple times.
            Assert.Equal(2, errorResult3.ErrorMessage!.Split(new []{"NullReferenceException"}, StringSplitOptions.None).Length);
        }
    }
}
