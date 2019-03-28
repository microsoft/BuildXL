// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Results
{
    public class GetSelectorResultTests : ResultTests<GetSelectorResult>
    {
        protected override GetSelectorResult CreateFrom(Exception exception)
        {
            return new GetSelectorResult(exception);
        }

        protected override GetSelectorResult CreateFrom(string errorMessage)
        {
            return new GetSelectorResult(errorMessage);
        }

        protected override GetSelectorResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new GetSelectorResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new GetSelectorResult("error");
            Assert.False(new GetSelectorResult(other, "message").Succeeded);
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
        public void SelectorProperty()
        {
            var selector = Selector.Random();
            Assert.Equal(selector, new GetSelectorResult(selector).Selector);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var selector = Selector.Random();
            var v1 = new GetSelectorResult(selector);
            var v2 = new GetSelectorResult(selector) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = new GetSelectorResult(Selector.Random());
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var selector = Selector.Random();
            var v1 = new GetSelectorResult(selector);
            var v2 = new GetSelectorResult(selector);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = new GetSelectorResult(Selector.Random());
            var v2 = new GetSelectorResult("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = new GetSelectorResult("error1");
            var v2 = new GetSelectorResult("error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var selector = Selector.Random();
            var v1 = new GetSelectorResult(selector);
            var v2 = new GetSelectorResult(selector);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = new GetSelectorResult(Selector.Random());
            var v2 = new GetSelectorResult(Selector.Random());
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new GetSelectorResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains("ContentHash=", new GetSelectorResult(Selector.Random()).ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
