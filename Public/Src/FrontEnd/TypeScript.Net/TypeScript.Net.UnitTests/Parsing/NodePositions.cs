// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.Printing;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class NodePositions
    {
        [Fact(Skip = "Bug in TypeScript.Net")]
        public void TestNodePositions1()
        {
            var code = @" export const dll = 1;";

            var node = ParsingHelper.ParseSourceFile(code);

            var brokenNodes = node.GetSelfAndDescendantNodes()
                .Where(n => n.GetChildNodes().Any())
                .Where(n => (n.GetChildNodes().First().Pos < n.Pos) || (n.GetChildNodes().Last().End > n.End))
                .Concat(node.GetSelfAndDescendantNodes().Where(n => n.Pos > n.End))
                .ToArray();

            Assert.True(brokenNodes.Length == 0);
        }

        [Fact(Skip = "Bug in TypeScript.Net")]
        public void TestNodePositions2()
        {
            var code = @"
                // comment 1
                while /* inner comment 1 */ ( /* inner comment 2 */ true) /* inner comment 3 */ {
                    
                }
                // comment 2
            ";

            var node = ParsingHelper.ParseSourceFile(code);

            foreach (var n in NodeWalkerEx.GetDescendantNodes(node))
            {
                if (n.Type == NodeOrNodesOrNullType.Nodes)
                {
                    Assert.True(n.Nodes.Pos <= n.Nodes.End);
                }

                if (n.Type == NodeOrNodesOrNullType.Node)
                {
                    Assert.True(n.Node.Pos <= n.Node.End);
                }
            }
        }
    }
}
