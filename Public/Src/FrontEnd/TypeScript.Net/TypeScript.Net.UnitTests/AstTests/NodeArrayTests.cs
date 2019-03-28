// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Types;
using Xunit;

namespace Test.DScript.AstTests
{
    public sealed class NodeArrayTests
    {
        [Fact]
        public void AddTest()
        {
            var array = new NodeArray<IExpression>();
            array.Add(new LiteralExpression("original-1"));
            array.Add(new LiteralExpression("original-2"));
            
            Assert.Equal(2, array.Length);
            Assert.Equal("original-1", array[0].Cast<LiteralExpression>().Text);
            Assert.Equal("original-2", array[1].Cast<LiteralExpression>().Text);
        }

        [Fact]
        public void InsertTest()
        {
            var array = new NodeArray<IExpression>();
            array.Add(new LiteralExpression("original-1"));
            array.Add(new LiteralExpression("original-2"));

            array.Insert(1, new LiteralExpression("insert-1"));
            array.Insert(0, new LiteralExpression("insert-0"));

            Assert.Equal(4, array.Length);
            Assert.Equal("insert-0", array[0].Cast<LiteralExpression>().Text);
            Assert.Equal("original-1", array[1].Cast<LiteralExpression>().Text);
            Assert.Equal("insert-1", array[2].Cast<LiteralExpression>().Text);
            Assert.Equal("original-2", array[3].Cast<LiteralExpression>().Text);
        }
    }
}
