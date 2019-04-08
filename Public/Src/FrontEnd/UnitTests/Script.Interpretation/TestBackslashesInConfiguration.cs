// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestBackslashesInConfiguration : SemanticBasedTests
    {
        public TestBackslashesInConfiguration(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("foo/boo/bar/")]
        [InlineData(@"foo\boo/bar\")]
        [InlineData(@"foo\boo\bar\")]
        public void BacksSlashesAreCorrectlyInterpretedInPrimaryConfig(string path)
        {
            string config =
$@"config({{
    projects: [f`{path}build.dsc`]
}});";

            var result = BuildWithPrelude(config)
                .AddSpec("foo/boo/bar/build.dsc", "export const r = 42;")
                .RootSpec("foo/boo/bar/build.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }

        [Theory]
        [InlineData("foo/boo/bar/")]
        [InlineData(@"foo\boo/bar\")]
        [InlineData(@"foo\boo\bar\")]
        public void BacksSlashesAreCorrectlyInterpretedInModuleConfig(string path)
        {
            string module =
$@"module({{
    name: ""MyModule"",
    projects: [f`{path}build.dsc`]
}});";

            var result = BuildWithPrelude()
                .AddSpec("package.config.dsc", module)
                .AddSpec("foo/boo/bar/build.dsc", "export const r = 42;")
                .AddSpec("package.dsc", "//empty default main file")
                .RootSpec("foo/boo/bar/build.dsc")
                .EvaluateExpressionWithNoErrors("r");

            Assert.Equal(42, result);
        }
    }
}
