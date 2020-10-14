// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public void AndOperatorShouldKeepExceptionProperty()
        {
            var exception = new Exception("1");
            var r1 = new BoolResult(exception, "error 1");
            var r2 = new BoolResult("error 2");

            Assert.NotNull((r1 & r2).Exception);

            Assert.NotNull((r1 & BoolResult.Success).Exception);
        }


        [Fact]
        public void AndOperatorReturnsAnotherInstanceIfOneOfThemFailed()
        {
            var exception = new Exception("1");
            var r1 = new BoolResult(exception, "error 1");
            var r2 = BoolResult.Success;

            Assert.True(object.ReferenceEquals((r1 & r2), r1));
            Assert.True(object.ReferenceEquals((r2 & r1), r1));
        }

        [Fact]
        public void OrOperatorShouldKeepExceptionProperty()
        {
            var exception = new Exception("1");
            var r1 = new BoolResult(exception, "error 1");
            var r2 = new BoolResult("error 2");

            var combined = r1 | r2;
            Assert.NotNull(combined.Exception);
        }

        [Fact]
        public void AndShouldPropagateIsCancelledFlag()
        {
            var r1 = new BoolResult("error 1") {IsCancelled = true};
            var r2 = new BoolResult("error 2") {IsCancelled = true};

            var combined = r1 & r2;
            Assert.True(combined.IsCancelled);
        }

        [Fact]
        public void OrShouldPropagateIsCancelledFlag()
        {
            var r1 = new BoolResult("error 1") {IsCancelled = true};
            var r2 = new BoolResult("error 2") {IsCancelled = true};

            // The final result IsCancelled only if both errors are cancelled.
            Assert.True((r1 | r2).IsCancelled);
            Assert.False((r1 | new BoolResult("error 3")).IsCancelled);
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
