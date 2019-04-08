// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.DScript;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using Xunit;

namespace Test.DScript.DScriptSpecific
{
    public class TestSyntaxFactory
    {
        [Theory]
        [InlineData(InterpolationKind.PathInterpolation, "const foo = p`someLiteral`")]
        [InlineData(InterpolationKind.DirectoryInterpolation, "const foo = d`someLiteral`")]
        [InlineData(InterpolationKind.FileInterpolation, "const foo = f`someLiteral`")]
        [InlineData(InterpolationKind.PathAtomInterpolation, "const foo = a`someLiteral`")]
        [InlineData(InterpolationKind.RelativePathInterpolation, "const foo = r`someLiteral`")]
        [InlineData(InterpolationKind.StringInterpolation, "const foo = `someLiteral`")]
        public void TestPathLikeVariableDeclaration(InterpolationKind kind, string expected)
        {
            var statement = SyntaxFactory.PathLikeConstVariableDeclaration("foo", kind, "someLiteral");
            var text = statement.ToDisplayString();
            Assert.Equal(expected, text);
        }

        [Fact]
        public void CreateExportedVariable()
        {
            var statement = SyntaxFactory.PathLikeConstVariableDeclaration("foo", InterpolationKind.PathInterpolation, "bar", Visibility.Export);
            var text = statement.ToDisplayString();
            Assert.Equal("export const foo = p`bar`", text);
        }

        [Fact]
        public void CreatePublicVariable()
        {
            var statement = SyntaxFactory.PathLikeConstVariableDeclaration("foo", InterpolationKind.PathInterpolation, "bar", Visibility.Public);
            // TODO: GetFormattedText prints the decorators, but ToDisplayString() on variable statement - does not.
            var text = statement.GetFormattedText();
            Assert.Equal(@"@@public
export const foo = p`bar`", text);
        }

        [Fact]
        public void PublicVariableWithSemicolonAndNewLineAtTheEnd()
        {
            var sourceFile = 
                new SourceFileBuilder()
                .Statement(
                    SyntaxFactory.PathLikeConstVariableDeclaration("foo", InterpolationKind.PathInterpolation, "bar", Visibility.Export))
                .SemicolonAndBlankLine()
                .Build();

            var text = sourceFile.ToDisplayStringV2();
            Assert.Equal(
                @"export const foo = p`bar`;

",
                text);
        }
    }
}
