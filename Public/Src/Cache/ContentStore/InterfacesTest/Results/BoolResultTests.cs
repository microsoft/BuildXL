// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class BoolResultTests : ResultTests<BoolResult>
    {
        protected override BoolResult CreateFrom(Exception exception)
        {
            return new BoolResult(exception);
        }

        protected override BoolResult CreateFrom(string errorMessage)
        {
            return new BoolResult(errorMessage);
        }

        protected override BoolResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new BoolResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ObjectDisposedExceptionIsNotCriticalForCancellation()
        {
            var e = new ObjectDisposedException("foo");
            Assert.True(ResultBase.IsCritical(e));
            Assert.False(ResultBase.IsCriticalForCancellation(e));
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.False(new BoolResult(other, "message").Succeeded);
        }

        [Fact]
        public void SuccessPropertyTrue()
        {
            Assert.True(BoolResult.Success.Succeeded);
        }

        [Fact]
        public void SuccessPropertyFalseByErrorMessage()
        {
            Assert.False(CreateFrom("error").Succeeded);
        }

        [Fact]
        public void SuccessPropertyFalseByException()
        {
            Assert.False(CreateFrom(new InvalidOperationException()).Succeeded);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var v1 = BoolResult.Success;
            var v2 = BoolResult.Success as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = BoolResult.Success;
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var v1 = BoolResult.Success;
            var v2 = BoolResult.Success;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseSuccessMismatch()
        {
            var v1 = BoolResult.Success;
            var v2 = new BoolResult("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = new BoolResult("error1");
            var v2 = new BoolResult("error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var v1 = BoolResult.Success;
            var v2 = BoolResult.Success;
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = BoolResult.Success;
            var v2 = new BoolResult("error");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new BoolResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains("Success", BoolResult.Success.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
