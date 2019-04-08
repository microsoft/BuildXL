// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class PlaceFileResultTests : ResultTests<PlaceFileResult>
    {
        protected override PlaceFileResult CreateFrom(Exception exception)
        {
            return new PlaceFileResult(exception);
        }

        protected override PlaceFileResult CreateFrom(string errorMessage)
        {
            return new PlaceFileResult(errorMessage);
        }

        protected override PlaceFileResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new PlaceFileResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.Equal(PlaceFileResult.ResultCode.Error, new PlaceFileResult(other, "message").Code);
        }

        [Fact]
        public void ExceptionError()
        {
            Assert.Equal(PlaceFileResult.ResultCode.Error, CreateFrom(new InvalidOperationException()).Code);
        }

        [Fact]
        public void ErrorMessageError()
        {
            Assert.Equal(PlaceFileResult.ResultCode.Error, CreateFrom("error").Code);
        }

        [Fact]
        public void SucceededTrue()
        {
            Assert.True(new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy).Succeeded);
        }

        [Fact]
        public void SucceededFalse()
        {
            Assert.False(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound).Succeeded);
        }

        [Fact]
        public void CodeProperty()
        {
            var code = PlaceFileResult.ResultCode.PlacedWithCopy;
            Assert.Equal(code, new PlaceFileResult(code).Code);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var v1 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            var v2 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var v1 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            var v2 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            var v2 = new PlaceFileResult("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = new PlaceFileResult("error1");
            var v2 = new PlaceFileResult("error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var v1 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            var v2 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithHardLink);
            var v2 = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new PlaceFileResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains(
                PlaceFileResult.ResultCode.PlacedWithHardLink.ToString(),
                new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithHardLink).ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void IsPlacedTrueHardLink()
        {
            Assert.True(new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithHardLink).IsPlaced());
        }

        [Fact]
        public void IsPlacedTrueCopy()
        {
            Assert.True(new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy).IsPlaced());
        }

        [Fact]
        public void IsPlacedTrueMove()
        {
            Assert.True(new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithMove).IsPlaced());
        }

        [Fact]
        public void IsPlacedFalse()
        {
            Assert.False(new PlaceFileResult("error").IsPlaced());
        }
    }
}
