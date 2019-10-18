// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class PinResultTests : ResultTests<PinResult>
    {
        protected override PinResult CreateFrom(Exception exception)
        {
            return new PinResult(exception);
        }

        protected override PinResult CreateFrom(string errorMessage)
        {
            return new PinResult(errorMessage);
        }

        protected override PinResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new PinResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.Equal(PinResult.ResultCode.Error, new PinResult(other, "message").Code);
        }

        [Fact]
        public void ExceptionError()
        {
            Assert.Equal(PinResult.ResultCode.Error, CreateFrom(new InvalidOperationException()).Code);
        }

        [Fact]
        public void ErrorMessageError()
        {
            Assert.Equal(PinResult.ResultCode.Error, CreateFrom("error").Code);
        }

        [Fact]
        public void ErrorMessageToString()
        {
            Assert.Contains(PinResult.ResultCode.Error.ToString(), new PinResult(PinResult.ResultCode.Error).ToString());
        }

        [Fact]
        public void SucceededTrue()
        {
            Assert.True(PinResult.Success.Succeeded);
        }

        [Fact]
        public void SucceededWhenPinResultIsSuccess()
        {
            Assert.True(new PinResult(PinResult.ResultCode.Success).Succeeded);
            Assert.Null(new PinResult(PinResult.ResultCode.Success).ErrorMessage);

            Assert.True(new PinResult(contentSize: -1L, lastAccessTime: null).Succeeded);
            Assert.Null(new PinResult(contentSize: -1L, lastAccessTime: null).ErrorMessage);

            Assert.True(new PinResult(contentSize: -1L, lastAccessTime: null, PinResult.ResultCode.Success).Succeeded);
            Assert.Null(new PinResult(contentSize: -1L, lastAccessTime: null, PinResult.ResultCode.Success).ErrorMessage);
        }

        [Fact]
        public void ErrorWhenPinResultIsNotSuccess()
        {
            Assert.False(new PinResult(PinResult.ResultCode.ContentNotFound).Succeeded);
            Assert.NotNull(new PinResult(PinResult.ResultCode.ContentNotFound).ErrorMessage);

            Assert.False(new PinResult(PinResult.ResultCode.Error).Succeeded);
            Assert.NotNull(new PinResult(PinResult.ResultCode.Error).ErrorMessage);

            Assert.False(new PinResult(contentSize: -1L, lastAccessTime: null, PinResult.ResultCode.ContentNotFound).Succeeded);
            Assert.NotNull(new PinResult(contentSize: -1L, lastAccessTime: null, PinResult.ResultCode.ContentNotFound).ErrorMessage);

            Assert.False(new PinResult(contentSize: -1L, lastAccessTime: null, PinResult.ResultCode.Error).Succeeded);
            Assert.NotNull(new PinResult(contentSize: -1L, lastAccessTime: null, PinResult.ResultCode.Error).ErrorMessage);
        }

        [Fact]
        public void SucceededFalse()
        {
            Assert.False(new PinResult("error").Succeeded);
        }

        [Fact]
        public void CodePropertySuccess()
        {
            Assert.Equal(PinResult.ResultCode.Success, PinResult.Success.Code);
        }

        [Fact]
        public void CodePropertyError()
        {
            Assert.Equal(PinResult.ResultCode.Error, new PinResult("error").Code);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var v1 = PinResult.Success;
            var v2 = PinResult.Success as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = PinResult.Success;
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var v1 = PinResult.Success;
            var v2 = PinResult.Success;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = PinResult.Success;
            var v2 = new PinResult("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = new PinResult("error1");
            var v2 = new PinResult("error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var v1 = PinResult.Success;
            var v2 = PinResult.Success;
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = PinResult.Success;
            var v2 = new PinResult("error");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new PinResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains(PinResult.ResultCode.Success.ToString(), PinResult.Success.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
