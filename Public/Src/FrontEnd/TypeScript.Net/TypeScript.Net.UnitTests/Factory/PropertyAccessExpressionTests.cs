// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using Xunit;

namespace TypeScript.Net.UnitTests.Factory
{
    public class PropertyAccessExpressionTests
    {
        [Fact]
        public void TwoStringParams()
        {
            var node = new PropertyAccessExpression("first", "second");
            Assert.Equal("first.second", node.GetFormattedText());
        }

        [Fact]
        public void FourStringParms()
        {
            var node = new PropertyAccessExpression("first", "second", "third", "fourth");
            Assert.Equal("first.second.third.fourth", node.GetFormattedText());
        }

        [Fact]
        public void TwoStringList()
        {
            var node = new PropertyAccessExpression(new List<string> { "first", "second" });
            Assert.Equal("first.second", node.GetFormattedText());
        }

        [Fact]
        public void FourStringList()
        {
            var node = new PropertyAccessExpression(new List<string> { "first", "second", "third", "fourth" });
            Assert.Equal("first.second.third.fourth", node.GetFormattedText());
        }
    }
}
