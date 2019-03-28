// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Results
{
    public class GetContentHashListResultTests : ResultTests<GetContentHashListResult>
    {
        protected override GetContentHashListResult CreateFrom(Exception exception)
        {
            return new GetContentHashListResult(exception);
        }

        protected override GetContentHashListResult CreateFrom(string errorMessage)
        {
            return new GetContentHashListResult(errorMessage);
        }

        protected override GetContentHashListResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new GetContentHashListResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new GetContentHashListResult("error");
            Assert.False(new GetContentHashListResult(other, "message").Succeeded);
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
        public void Success()
        {
            Assert.True(
                new GetContentHashListResult(new ContentHashListWithDeterminism(
                    ContentHashList.Random(), CacheDeterminism.None)).Succeeded);
        }

        [Fact]
        public void ContentHashListWithDeterminismProperty()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires));
            Assert.Equal(contentHashListWithDeterminism, new GetContentHashListResult(contentHashListWithDeterminism).ContentHashListWithDeterminism);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var v1 = new GetContentHashListResult(contentHashListWithDeterminism);
            var v2 = new GetContentHashListResult(contentHashListWithDeterminism) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = new GetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var v1 = new GetContentHashListResult(contentHashListWithDeterminism);
            var v2 = new GetContentHashListResult(contentHashListWithDeterminism);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrueNotReferenceEqualContentHashList()
        {
            var contentHashList = ContentHashList.Random();
            var determinism = CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires);
            var contentHashListWithDeterminism1 = new ContentHashListWithDeterminism(contentHashList, determinism);
            var contentHashListWithDeterminism2 = new ContentHashListWithDeterminism(contentHashList, determinism);
            var v1 = new GetContentHashListResult(contentHashListWithDeterminism1);
            var v2 = new GetContentHashListResult(contentHashListWithDeterminism2);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = new GetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            var v2 = new GetContentHashListResult("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseNullLeftContentHashList()
        {
            var v1 = new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
            var v2 = new GetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseNullRightContentHashList()
        {
            var v1 = new GetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            var v2 = new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseContentHashListMismatch()
        {
            var determinism = CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires);
            var v1 = new GetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), determinism));
            var v2 = new GetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), determinism));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseDeterminismMismatch()
        {
            var contentHashList = ContentHashList.Random();
            var v1 = new GetContentHashListResult(new ContentHashListWithDeterminism(
                contentHashList, CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.MaxValue)));
            var v2 = new GetContentHashListResult(new ContentHashListWithDeterminism(
                contentHashList, CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.MaxValue)));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires));
            var v1 = new GetContentHashListResult(contentHashListWithDeterminism);
            var v2 = new GetContentHashListResult(contentHashListWithDeterminism);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = new GetContentHashListResult(new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires)));
            var v2 = new GetContentHashListResult(new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires)));
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new GetContentHashListResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains(
                "Success",
                new GetContentHashListResult(
                    new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None)).ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
