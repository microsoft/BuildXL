// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Results
{
    public class AddContentHashListResultTests : ResultTests<AddOrGetContentHashListResult>
    {
        protected override AddOrGetContentHashListResult CreateFrom(Exception exception)
        {
            return new AddOrGetContentHashListResult(exception);
        }

        protected override AddOrGetContentHashListResult CreateFrom(string errorMessage)
        {
            return new AddOrGetContentHashListResult(errorMessage);
        }

        protected override AddOrGetContentHashListResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new AddOrGetContentHashListResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new AddOrGetContentHashListResult("error");
            Assert.False(new AddOrGetContentHashListResult(other, "message").Succeeded);
        }

        [Fact]
        public void ExceptionError()
        {
            Assert.Equal(AddOrGetContentHashListResult.ResultCode.Error, CreateFrom(new InvalidOperationException()).Code);
        }

        [Fact]
        public void ErrorMessageError()
        {
            Assert.Equal(AddOrGetContentHashListResult.ResultCode.Error, CreateFrom("error").Code);
        }

        [Fact]
        public void SucceededTrue()
        {
            Assert.True(new AddOrGetContentHashListResult(
                new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None)).Succeeded);
        }

        [Fact]
        public void SucceededFalse()
        {
            Assert.False(new AddOrGetContentHashListResult("error").Succeeded);
        }

        [Fact]
        public void CodePropertySuccess()
        {
            Assert.Equal(
                AddOrGetContentHashListResult.ResultCode.Success,
                new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None)).Code);
        }

        [Fact]
        public void CodePropertyError()
        {
            Assert.Equal(AddOrGetContentHashListResult.ResultCode.Error, new AddOrGetContentHashListResult("error").Code);
        }

        [Fact]
        public void ContentHashListWithDeterminismProperty()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires));
            Assert.Equal(
                contentHashListWithDeterminism,
                new AddOrGetContentHashListResult(contentHashListWithDeterminism).ContentHashListWithDeterminism);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var v1 = new AddOrGetContentHashListResult(contentHashListWithDeterminism);
            var v2 = new AddOrGetContentHashListResult(contentHashListWithDeterminism) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None);
            var v1 = new AddOrGetContentHashListResult(contentHashListWithDeterminism);
            var v2 = new AddOrGetContentHashListResult(contentHashListWithDeterminism);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrueNotReferenceEqualContentHashListWithDeterminism()
        {
            var contentHashList = ContentHashList.Random();
            var determinism = CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires);
            var contentHashListWithDeterminism1 = new ContentHashListWithDeterminism(contentHashList, determinism);
            var contentHashListWithDeterminism2 = new ContentHashListWithDeterminism(contentHashList, determinism);
            var v1 = new AddOrGetContentHashListResult(contentHashListWithDeterminism1);
            var v2 = new AddOrGetContentHashListResult(contentHashListWithDeterminism2);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            var v2 = new AddOrGetContentHashListResult("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseNullLeftContentHashList()
        {
            var v1 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
            var v2 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseNullRightContentHashList()
        {
            var v1 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None));
            var v2 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseContentHashListMismatch()
        {
            var determinism = CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires);
            var v1 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), determinism));
            var v2 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(ContentHashList.Random(), determinism));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseDeterminismMismatch()
        {
            var v1 = new AddOrGetContentHashListResult(
                new ContentHashListWithDeterminism(null, CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.MaxValue)));
            var v2 = new AddOrGetContentHashListResult(
                new ContentHashListWithDeterminism(null, CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), DateTime.MaxValue)));
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var contentHashListWithDeterminism = new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires));
            var v1 = new AddOrGetContentHashListResult(contentHashListWithDeterminism);
            var v2 = new AddOrGetContentHashListResult(contentHashListWithDeterminism);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires)));
            var v2 = new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(
                ContentHashList.Random(), CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires)));
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new AddOrGetContentHashListResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains(
                AddOrGetContentHashListResult.ResultCode.Success.ToString(),
                new AddOrGetContentHashListResult(
                    new ContentHashListWithDeterminism(ContentHashList.Random(), CacheDeterminism.None)).ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
