// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace Test.DScript.DScriptSpecific
{
    /// <nodoc/>
    public sealed class TestPathInterpolation
    {
        private readonly ParsingOptions m_options = ParsingOptions.DefaultParsingOptions;

        [Theory]
        [InlineData("a")]
        [InlineData("p")]
        [InlineData("d")]
        [InlineData("f")]
        [InlineData("r")]
        public void TestNoBacklashEscapingInPathLikeInterpolation(string factoryName)
        {
            var code = $@"{factoryName}`\\`";
            var node = ParsingHelper.ParseExpressionStatement<ITaggedTemplateExpression>(code, roundTripTesting: false, parsingOptions: m_options);

            Assert.Equal(@"\\", node.TemplateExpression.GetTemplateText());
        }

        [Theory]
        [InlineData("a")]
        [InlineData("p")]
        [InlineData("d")]
        [InlineData("f")]
        [InlineData("r")]
        public void TestBacktickEscapingInPathLikeInterpolation(string factoryName)
        {
            var code = $"{factoryName}`a``b`";
            var node = ParsingHelper.ParseExpressionStatement<ITaggedTemplateExpression>(code, roundTripTesting: false, parsingOptions: m_options);

            Assert.Equal("a`b", node.TemplateExpression.GetTemplateText());
        }

        [Fact]
        public void TestBackslashEscapingInNonPathLikeInterpolation()
        {
            var code = @"anotherFactoryMethod`\\`";
            var node = ParsingHelper.ParseExpressionStatement<ITaggedTemplateExpression>(code, roundTripTesting: false, parsingOptions: m_options);

            Assert.Equal(@"\", node.TemplateExpression.GetTemplateText());
        }

        [Fact]
        public void TestBackslashEscapingInStringInterpolation()
        {
            var code = @"`\\`";
            var node = ParsingHelper.ParseExpressionStatement<ILiteralExpression>(code, roundTripTesting: false, parsingOptions: m_options);

            Assert.Equal(@"\", node.Text);
        }

        [Fact]
        public void TestInterpolatedPathWithBackslashes()
        {
            var code = @"p`a\path\to\a\file`";
            var node = ParsingHelper.ParseExpressionStatement<ITaggedTemplateExpression>(code, roundTripTesting: false, parsingOptions: m_options);

            Assert.Equal(@"a\path\to\a\file", node.TemplateExpression.GetTemplateText());
        }

        [Fact]
        public void TestInterpolatedTemplatedPathWithBackslashes()
        {
            var code = @"p`a\${r`path\to`}\a\file`";
            var node = ParsingHelper.ParseExpressionStatement<ITaggedTemplateExpression>(code, roundTripTesting: false, parsingOptions: m_options);

            var templateExpression = node.TemplateExpression.Cast<ITemplateExpression>();
            var firstSpan = templateExpression.TemplateSpans[0];

            Assert.Equal(@"a\", templateExpression.Head.Text);
            Assert.Equal(@"path\to", firstSpan.Expression.Cast<ITaggedTemplateExpression>().Template.GetTemplateText());
            Assert.Equal(@"\a\file", firstSpan.Literal.Text);
        }

        [Theory]
        [InlineData(@"\\", @"\")]
        [InlineData("``", "``")]
        public void TestRegularEscapingTakesPlaceInsideTemplate(string escape, string escapeResult)
        {
            var code = $@"p`path\to\${{'a{escape}b'}}\file`";
            var node = ParsingHelper.ParseExpressionStatement<ITaggedTemplateExpression>(code, roundTripTesting: false, parsingOptions: m_options);

            var templateExpression = node.TemplateExpression.Cast<ITemplateExpression>();
            var firstSpan = templateExpression.TemplateSpans[0];

            Assert.Equal(@"path\to\", templateExpression.Head.Text);
            Assert.Equal($"a{escapeResult}b", firstSpan.Expression.Cast<IStringLiteral>().Text);
            Assert.Equal(@"\file", firstSpan.Literal.Text);
        }
    }
}
