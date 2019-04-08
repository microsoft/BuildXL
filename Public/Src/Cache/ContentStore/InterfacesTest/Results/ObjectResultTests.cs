// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ObjectResultTests : ResultTests<ObjectResult<AbsolutePath>>
    {
        private static readonly AbsolutePath Path1 = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "path1"));
        private static readonly AbsolutePath Path2 = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "path2"));
        private static readonly ObjectResult<AbsolutePath> Result1 = new ObjectResult<AbsolutePath>(Path1);
        private static readonly ObjectResult<AbsolutePath> Result2 = new ObjectResult<AbsolutePath>(Path2);

        protected override ObjectResult<AbsolutePath> CreateFrom(Exception exception)
        {
            return new ObjectResult<AbsolutePath>(exception);
        }

        protected override ObjectResult<AbsolutePath> CreateFrom(string errorMessage)
        {
            return new ObjectResult<AbsolutePath>(errorMessage);
        }

        protected override ObjectResult<AbsolutePath> CreateFrom(string errorMessage, string diagnostics)
        {
            return new ObjectResult<AbsolutePath>(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new ObjectResult<AbsolutePath>("error");
            Assert.False(new ObjectResult<AbsolutePath>(other, "message").Succeeded);
        }

        [Fact]
        public void DataNullOnError()
        {
            Assert.Null(CreateFrom("error").Data);
        }

        [Fact]
        public void DataNotNull()
        {
            Assert.NotNull(Result1.Data);
        }

        [Fact]
        public void SuccessPropertyTrue()
        {
            Assert.True(Result1.Succeeded);
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
            var v1 = Result1;
            var v2 = Result1 as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = Result1;
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var v1 = Result1;
            var v2 = Result1 as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseDataNull()
        {
            var v1 = new ObjectResult<AbsolutePath>(Path1);
            var v2 = new ObjectResult<AbsolutePath>("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseDataMismatch()
        {
            var v1 = Result1;
            var v2 = Result2;
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseSuccessMismatch()
        {
            var v1 = Result1;
            var v2 = new ObjectResult<AbsolutePath>();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrueErrorMessageMatch()
        {
            var v1 = new ObjectResult<AbsolutePath>("error");
            var v2 = new ObjectResult<AbsolutePath>("error");
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = new ObjectResult<AbsolutePath>("error1");
            var v2 = new ObjectResult<AbsolutePath>("error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var v1 = new ObjectResult<AbsolutePath>("error");
            var v2 = new ObjectResult<AbsolutePath>("error");
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = new ObjectResult<AbsolutePath>("error1");
            var v2 = new ObjectResult<AbsolutePath>("error2");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new ObjectResult<AbsolutePath>("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains("Success", Result1.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
