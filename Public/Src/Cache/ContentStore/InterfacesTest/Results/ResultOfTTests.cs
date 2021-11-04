// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ResultOfTTests
    {
        [Fact]
        public void NonCriticalWhenNonCriticalIsCalled()
        {
            var r1 = new Result<string>(new NullReferenceException());
            var r2 = new Result<string>(new AggregateException(new NullReferenceException()));
            Assert.True(r1.IsCriticalFailure);
            Assert.True(r2.IsCriticalFailure);

            r1.MakeNonCritical();
            r2.MakeNonCritical();
            Assert.False(r1.IsCriticalFailure);
            Assert.False(r2.IsCriticalFailure);
        }

        [Fact]
        public void CancellationMakesNonContractLikeExceptionsNonCritical()
        {
            var nullRef = new Result<string>(new AggregateException(new NullReferenceException()));
            var invalidOperation = new Result<string>(new AggregateException(new InvalidOperationException()));

            Assert.True(nullRef.IsCriticalFailure);
            Assert.True(invalidOperation.IsCriticalFailure);

            nullRef.MarkCancelled();
            invalidOperation.MarkCancelled();

            Assert.True(nullRef.IsCriticalFailure, "NullReferenceException must stay critical even if cancellation flag is set.");
            Assert.False(invalidOperation.IsCriticalFailure, "InvalidOperationException should not be critical if canceled.");
        }

        [Fact]
        public void GetHashCodeWithNullValueShouldNotThrow()
        {
            var r = new Result<string>(null, isNullAllowed: true);
            // This method should not throw.
            Assert.Equal(r.GetHashCode(), r.GetHashCode());
        }

        [Fact]
        public void EqualsWithNullValueShouldNotThrow()
        {
            var r = new Result<string>(null, isNullAllowed: true);
            var r2 = new Result<string>(null, isNullAllowed: true);
            // This method should not throw.
            Assert.True(r.Equals(r2));
        }

        [Fact]
        public void CanceledResultIsNotSuccessful()
        {
            var canceled = new Result<string>(string.Empty)
            {
                IsCancelled = true
            };

            Assert.False(canceled.Succeeded);
        }
    }
}
