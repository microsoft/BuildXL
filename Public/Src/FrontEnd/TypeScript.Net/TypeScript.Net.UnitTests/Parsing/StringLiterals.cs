// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class StringLiterals
    {
        [Theory]
        [InlineData("let x = 'string'", LiteralExpressionKind.SingleQuote)]
        [InlineData("let x = \"string\"", LiteralExpressionKind.DoubleQuote)]
        [InlineData("let x = `string`", LiteralExpressionKind.BackTick)]
        [InlineData("let x = 42", LiteralExpressionKind.None)]
        public void TestDifferentLiteralKinds(string code, LiteralExpressionKind expectedKind)
        {
            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            var initializer = node.DeclarationList.Declarations[0].Initializer.As<IStringLiteral>();

            Assert.Equal(expectedKind, initializer.LiteralKind);
        }

        [Theory]
        [InlineData("'\u202F.?'")]
        [InlineData("'singleQuoted'")]
        [InlineData("\"doubleQuoted\"")]
        [InlineData("`backTickQuoted`")]
        [InlineData("f`customBackTickQuoted`")]
        public void QuotedStrings(string expression)
        {
            string code =
@"{
    let x: string = " + expression + @";
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Theory]

        // Single quote
        [InlineData(@""" ' """, null)]
        [InlineData(@""" \' """, @""" ' """)]
        [InlineData(@"' \' '", null)]
        [InlineData(@"` ' `", null)]
        [InlineData(@"` \' `", @"` ' `")]

        // Double quote
        [InlineData(@""" \"" """, null)]
        [InlineData(@"' "" '", null)]
        [InlineData(@"' \"" '", @"' "" '")]
        [InlineData(@"` "" `", null)]
        [InlineData(@"` \"" `", @"` "" `")]

        // Backtick quote
        [InlineData(@""" ` """, null)]
        [InlineData(@""" \` """, @""" ` """)]
        [InlineData(@"' ` '", null)]
        [InlineData(@"' \` '", @"' ` '")]
        [InlineData(@"` \` `", null)]
        public void QuotedStringsWithEncodings(string expression, string expectedString = null)
        {
            string code =
@"{
    let x: string = " + expression + @";
}";

            // This test has to set expected because some unnecesary character encodings
            string expectedCode = code;
            if (expectedString != null)
            {
                expectedCode =
@"{
    let x: string = " + expectedString + @";
}";
            }

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(expectedCode, node.GetFormattedText());
        }

        [Fact]
        public void TaggedLiteralWithNoDollarSign()
        {
            string code = @"{
    function g(format: string, args: any[]) : string {
        return ""hi"";
    }
    let x = g`he{f(""hi"")}llo`;
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void TaggedLiteralWithOneCapturedVariable()
        {
            string code =
@"{
    let y = 42;
    let x: string = `y = ${y}`;
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void TaggedLiteralWithTwoCapturedVariables()
        {
            string code =
@"{
    let y = 42;
    let z = 'blah';
    let x: string = `y = ${y}, z = ${z}`;
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void TaggedLiteralWithMethodCall()
        {
            string code =
@"{
    let y = 42;
    let x: string = `y = ${y.toString()}`;
}";

            // Current printing is far from perfect! Should be enhanced, if needed.
            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            Assert.Equal(code, node.GetFormattedText());
        }

        [Fact]
        public void Emptystring()
        {
            // z is a string
            string code = @"let z = """"";

            var node = ParsingHelper.ParseFirstStatementFrom<IVariableStatement>(code);
            Assert.Equal(code, node.GetFormattedText());
        }
    }
}
