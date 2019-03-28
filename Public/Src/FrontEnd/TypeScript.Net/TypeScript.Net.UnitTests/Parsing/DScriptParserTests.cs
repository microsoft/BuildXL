// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class DScriptParserTests
    {
        private readonly PathTable m_pathTable;

        public DScriptParserTests()
        {
            m_pathTable = new PathTable();
        }

        [Fact]
        public void TemplateLiteralTextShouldNotHaveBackTicks()
        {
            string code =
@"{
    const f1 = f`a.txt`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            var literal = FindNode<PathLikeLiteral>(node);

            // Text property should not have quotes, but FormattedText should have.
            Assert.Equal("a.txt", literal.Text);
            Assert.Equal("`a.txt`", literal.GetFormattedText());
        }

        [Fact]
        public void ConvertFileLiteral()
        {
            string code =
@"{
    const f1 = f`bar/a.txt`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            var text = node.GetFormattedText();
            Assert.Equal(code, text);

            var literal = FindNode<RelativePathLiteralExpression>(node);
            Assert.NotNull(literal);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ConvertPackageRelativeFileLiteral()
        {
            string code =
@"{
    const f1 = f`/a.txt`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);

            var literal = FindNode<PackageRelativePathLiteralExpression>(node);
            Assert.NotNull(literal);

            var text = node.GetFormattedText();
            Assert.Equal(code, text);
        }

        [Fact]
        public void FileWithFolder()
        {
            string code =
@"{
    const f1 = f`${root}/b/${base}/a.txt`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
            Assert.NotNull(FindNode<ILiteralLikeNode>(node));
        }

        [Fact]
        public void UseRegularIdentifiers()
        {
            // DScriptParser should create Identifier nodes not a special optimized one,
            // because optimized identifiers are less performant today.
            string code =
@"{
    const n = bar;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
            Assert.NotNull(FindNode<Identifier>(node));
        }

        [Fact]
        public void PathAtomTest()
        {
            string code =
@"{
    const f1 = a`foo`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
            Assert.NotNull(FindNode<ILiteralLikeNode>(node));
        }

        [Fact]
        public void FileInDirectory()
        {
            string code =
@"{
    const f1 = f`folder/a.txt`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
            Assert.NotNull(FindNode<ILiteralLikeNode>(node));
        }

        [Fact]
        public void Directory()
        {
            string code =
@"{
    const f1 = d`myDirectory`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
            Assert.NotNull(FindNode<ILiteralLikeNode>(node));
        }

        [Fact]
        public void DirectoryInDirectory()
        {
            string code =
@"{
    const f1 = d`other/myDirectory`;
}";

            var node = ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
            Assert.NotNull(FindNode<ILiteralLikeNode>(node));
        }

        private TNode ParseFirstStatementFrom<TNode>(string code) where TNode : INode
        {
            var sourceFile = ParsingHelper.ParseSourceFile(code, "fakeFileName.ts", null, new DScriptParser(m_pathTable));
            return (TNode)sourceFile.Statements.First();
        }

        private TNode FindNode<TNode>(INode root) where TNode : INode
        {
            return NodeWalker.TraverseBreadthFirstAndSelf(root).OfType<TNode>().FirstOrDefault();
        }
    }
}
