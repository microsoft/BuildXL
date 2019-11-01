// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class PutResultTests : ResultTests<PutResult>
    {
        private static readonly ContentHash Hash1 = ContentHash.Random();

        protected override PutResult CreateFrom(Exception exception)
        {
            return new PutResult(exception, Hash1);
        }

        protected override PutResult CreateFrom(string errorMessage)
        {
            return new PutResult(Hash1, errorMessage);
        }

        protected override PutResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new PutResult(Hash1, errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.False(new PutResult(other, Hash1, "message").Succeeded);
        }

        [Fact]
        public void ExceptionError()
        {
            Assert.False(CreateFrom(new InvalidOperationException()).Succeeded);
        }

        [Fact]
        public void ErrorMessageError()
        {
            Assert.False(CreateFrom("error").Succeeded);
        }

        [Fact]
        public void ConstructorSetsProperties()
        {
            var contentHash = ContentHash.Random();
            var result = new PutResult(contentHash, 7);
            Assert.Equal(contentHash, result.ContentHash);
            Assert.Equal(7, result.ContentSize);
            Assert.False(result.ContentAlreadyExistsInCache);
        }

        [Fact]
        public void ConstructorWithOptionalParamsSetsProperties()
        {
            var contentHash = ContentHash.Random();
            var result = new PutResult(contentHash, 7, true);
            Assert.Equal(contentHash, result.ContentHash);
            Assert.Equal(7, result.ContentSize);
            Assert.True(result.ContentAlreadyExistsInCache);
        }

        [Fact]
        public void Success()
        {
            Assert.True(new PutResult(ContentHash.Random(), 27).Succeeded);
            Assert.True(new PutResult(ContentHash.Random(), 27, true).Succeeded);
            Assert.True(new PutResult(ContentHash.Random(), 27, false).Succeeded);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var contentHash = ContentHash.Random();
            var v1 = new PutResult(contentHash, 1);
            var v2 = new PutResult(contentHash, 1) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = new PutResult(ContentHash.Random(), 27);
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var contentHash = ContentHash.Random();
            var v1 = new PutResult(contentHash, 1);
            var v2 = new PutResult(contentHash, 1);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = new PutResult(ContentHash.Random(), 27);
            var v2 = new PutResult(Hash1, "error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = new PutResult(Hash1, "error1");
            var v2 = new PutResult(Hash1, "error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var contentHash = ContentHash.Random();
            var v1 = new PutResult(contentHash, 1);
            var v2 = new PutResult(contentHash, 1);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = new PutResult(ContentHash.Random(), 27);
            var v2 = new PutResult(Hash1, "error");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new PutResult(Hash1, "something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains(
                "Success",
                new PutResult(ContentHash.Random(), 1).ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
