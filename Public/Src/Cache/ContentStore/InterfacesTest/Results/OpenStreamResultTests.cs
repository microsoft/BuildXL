// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class OpenStreamResultTests : ResultTests<OpenStreamResult>
    {
        private static readonly Stream NullStream = null;

        protected override OpenStreamResult CreateFrom(Exception exception)
        {
            return new OpenStreamResult(exception);
        }

        protected override OpenStreamResult CreateFrom(string errorMessage)
        {
            return new OpenStreamResult(errorMessage);
        }

        protected override OpenStreamResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new OpenStreamResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.Equal(OpenStreamResult.ResultCode.Error, new OpenStreamResult(other, "message").Code);
        }

        [Fact]
        public void ExceptionError()
        {
            Assert.Equal(OpenStreamResult.ResultCode.Error, CreateFrom(new InvalidOperationException()).Code);
        }

        [Fact]
        public void ErrorMessageError()
        {
            Assert.Equal(OpenStreamResult.ResultCode.Error, CreateFrom("error").Code);
        }

        [Fact]
        public void DurationMsProperty()
        {
            Assert.True(new OpenStreamResult(NullStream).DurationMs >= 0);
        }

        [Fact]
        public void SucceededTrue()
        {
            using (var stream = new MemoryStream())
            {
                Assert.True(new OpenStreamResult(stream).Succeeded);
            }
        }

        [Fact]
        public void SucceededFalse()
        {
            Assert.False(new OpenStreamResult("error").Succeeded);
        }

        [Fact]
        public void CodePropertySuccess()
        {
            using (var stream = new MemoryStream())
            {
                Assert.Equal(OpenStreamResult.ResultCode.Success, new OpenStreamResult(stream).Code);
            }
        }

        [Fact]
        public void CodePropertyError()
        {
            Assert.Equal(OpenStreamResult.ResultCode.Error, new OpenStreamResult("error").Code);
        }

        [Fact]
        public void StreamProperty()
        {
            Assert.Null(new OpenStreamResult(NullStream).Stream);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var v1 = new OpenStreamResult(NullStream) as object;
            var v2 = new OpenStreamResult(NullStream) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = new OpenStreamResult(NullStream);
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var v1 = new OpenStreamResult(NullStream);
            var v2 = new OpenStreamResult(NullStream);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = new OpenStreamResult(NullStream);
            var v2 = new OpenStreamResult("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = new OpenStreamResult("error1");
            var v2 = new OpenStreamResult("error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var v1 = new OpenStreamResult(NullStream);
            var v2 = new OpenStreamResult(NullStream);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = new OpenStreamResult(NullStream);
            var v2 = new OpenStreamResult("error");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new OpenStreamResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            using (var stream = new MemoryStream())
            {
                Assert.Contains(
                    OpenStreamResult.ResultCode.Success.ToString(),
                    new OpenStreamResult(stream).ToString(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
