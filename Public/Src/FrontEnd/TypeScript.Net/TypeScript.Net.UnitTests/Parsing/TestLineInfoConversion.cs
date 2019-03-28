// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using TypeScript.Net.Printing;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using TypeScript.Net.Utilities;
using Xunit;

namespace TypeScript.Net.UnitTests.LineInfos
{
    public class TestLineInfoConversion
    {
        [Fact]
        public void ComputePositionBackAndForth()
        {
            string code =
                @"namespace X {
    enum Foo {value = 42}
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);

            var enumMember = NodeWalkerEx.GetDescendantNodes(sourceFile).First(n => n.Node.Kind == SyntaxKind.EnumMember).Node.Cast<IEnumMember>();
            var lineInfo = enumMember.GetLineInfo(sourceFile);

            NodeExtensions.TryConvertLineOffsetToPosition(sourceFile, lineInfo.Line, lineInfo.Position, out var position);
            Assert.Equal(enumMember.GetNodeStartPositionWithoutTrivia(), position);
            Assert.Equal(enumMember.Name, GetNodeAtPosition(sourceFile, position));
        }

        [Fact]
        public void ComputePositionBackAndForthWithWeirdCommentsInside()
        {
            string code =
                @"
const x = 42;
/*
* Some comment
*/
namespace X {
    // simple comment
    enum /*another comment*/ Foo {/*and yet another comment*/value = 42}
}";

            var sourceFile = ParsingHelper.ParseSourceFile(code);

            var enumMember = NodeWalkerEx.GetDescendantNodes(sourceFile).First(n => n.Node.Kind == SyntaxKind.EnumMember).Node.Cast<IEnumMember>();
            var lineInfo = enumMember.GetLineInfo(sourceFile);

            NodeExtensions.TryConvertLineOffsetToPosition(sourceFile, lineInfo.Line, lineInfo.Position, out var position);
            Assert.Equal(enumMember.GetNodeStartPositionWithoutTrivia(), position);
            Assert.Equal(enumMember.Name, GetNodeAtPosition(sourceFile, position));
        }

        private static INode GetNodeAtPosition(ISourceFile sourceFile, int position)
        {
            NodeExtensions.TryGetNodeAtPosition(sourceFile, position, out INode result);
            return result;
        }
    }
}
