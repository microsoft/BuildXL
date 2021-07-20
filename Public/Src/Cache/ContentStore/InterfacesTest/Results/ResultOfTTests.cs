// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ResultOfTTests
    {
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
