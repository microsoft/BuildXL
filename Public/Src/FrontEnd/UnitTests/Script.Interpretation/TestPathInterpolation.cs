// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestPathInterpolation : SemanticBasedTests
    {
        public TestPathInterpolation(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("foo/boo/bar")]
        [InlineData(@"foo\boo/bar")]
        [InlineData(@"foo\boo\bar")]
        public void SlashesAreCorrectlyInterpreted(string path)
        {
            string code =
$"export const r = p`{path}`;";

            var result = BuildLegacyConfigurationWithPrelude().AddSpec(code).EvaluateExpressionWithNoErrors<IImplicitPath>("r");

            AssertPathEndsWith(@"foo" + Path.DirectorySeparatorChar + "boo" + Path.DirectorySeparatorChar + "bar", result);
        }

        [Theory]
        [InlineData("p")]
        [InlineData("d")]
        [InlineData("f")]
        public void InterpolatedPathAreCorrectlyInterpreted(string factoryName)
        {
            string code = $@"
        const boo = ""boo"";
        export const r = {factoryName}`foo/${{boo}}/bar`;";

            var result = BuildLegacyConfigurationWithPrelude().AddSpec(code).EvaluateExpressionWithNoErrors<IImplicitPath>("r");

            AssertPathEndsWith(@"foo" + Path.DirectorySeparatorChar + "boo" + Path.DirectorySeparatorChar + "bar", result);
        }

        [Fact]
        public void InterpolatedEscapesAreBasedOnSyntacticLocation()
        {
            string code = @"
// Escaping here uses the regular TypeScript escaping
const boo = 'bo\`o';

const atom = a`${boo}`;

// Even if the reference is in the context of a path interpolation, special escaping does not apply
export const r = p`foo/${atom}/bar`;";

            var result = BuildLegacyConfigurationWithPrelude().AddSpec(code).EvaluateExpressionWithNoErrors<IImplicitPath>("r");

            AssertPathEndsWith(@"foo" + Path.DirectorySeparatorChar + "bo`o" + Path.DirectorySeparatorChar + "bar", result);
        }

        [Fact]
        public void InterpolatedPathConversionError()
        {
            string code = @"
export const r = p`${undefined}/foo/bar`;";

            var result = BuildLegacyConfigurationWithPrelude().AddSpec(code).EvaluateWithFirstError("r");
            Assert.Contains(@"Expecting type(s) 'Path' for argument 1, but got 'undefined' of type 'undefined'", result.FullMessage);
        }

        private void AssertPathEndsWith(string expectedRelativePath, IImplicitPath actualPath)
        {
            var result = actualPath.Path.ToString(PathTable).EndsWith(expectedRelativePath);
            Assert.True(result, "Path " + actualPath.Path.ToString(PathTable) + " is expected to end with " + expectedRelativePath);
        }
    }
}
