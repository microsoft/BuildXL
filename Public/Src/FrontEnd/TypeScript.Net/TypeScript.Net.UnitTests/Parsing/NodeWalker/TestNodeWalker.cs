// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class TestNodeWalker
    {
        [Fact]
        public void GetShortHandPropertyAssignment()
        {
            // Short-hand property assignment
            // c: {a: any; b: number};
            string code =
@"{
    let a, b = 1;
    let c = {
        a,
        b,
    };
}";

            var node = ParsingHelper.ParseSourceFile(code);
            IShorthandPropertyAssignment propertyAssignment = FindFirstNode<IShorthandPropertyAssignment>(node);
            Assert.NotNull(propertyAssignment);
            Assert.Equal("a", propertyAssignment.Name.Text);
        }

        [Fact]
        public void GetVariableDeclarationFromSwitchBlock()
        {
            string code =
@"
function getResult() {return 1;}
switch(getResult())
{
    case 0:
    break;
    
    case 1:
        let x = 42;
    break;
}";

            var node = ParsingHelper.ParseSourceFile(code);

            IVariableDeclaration varDeclaration = FindFirstNode<IVariableDeclaration>(node);
            Assert.NotNull(varDeclaration);
            Assert.Equal("x", varDeclaration.Name.GetName());
        }

        private TNode FindFirstNode<TNode>(INode root) where TNode : class, INode
        {
            var allNodes = NodeWalker.TraverseBreadthFirstAndSelf(root).ToList();

            return allNodes.Select(n => n.As<TNode>()).First(n => n != null);
        }
    }
}
