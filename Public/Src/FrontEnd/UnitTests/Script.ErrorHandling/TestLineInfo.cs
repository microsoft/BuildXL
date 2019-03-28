// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public sealed class TestLineInfo : SemanticBasedTests
    {
        public TestLineInfo(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void LineInfoForCachedIdentifiers()
        {
            string spec =
@"export const ff = p`foo.bar`;
const r: number = 1 + <any>p`aaa`;
";

            var result = Build()
                .AddSpec("spec1.dsc", spec)
                .RootSpec("spec1.dsc")
                .EvaluateWithFirstError();

            Assert.NotNull(result.Location);
            Assert.Equal(2, result.Location.Value.Line);
            Assert.Equal(23, result.Location.Value.Position);
        }

        [Fact]
        public void LineInfoForErrorInTaggedTemplateExpression()
        {
            string spec =
@"export const ff = p`foo.bar`;
const o = {};
const r = p`${1 + o}/aaa`;
";

            var result = Build()
                .AddSpec("spec1.dsc", spec)
                .RootSpec("spec1.dsc")
                .EvaluateWithFirstError();

            Assert.Contains("Operator '+' cannot be applied to types 'number' and '{}'", result.FullMessage);
            Assert.NotNull(result.Location);
            Assert.Equal(3, result.Location.Value.Line);
            Assert.Equal(15, result.Location.Value.Position);
        }
        
        [Fact]
        public void LineInfoOnMissingFeature()
        {
            string code = 
@"namespace X {
    enum Foo {value = 42}
}";
            var result = ParseWithDiagnosticId(code, LogEventId.NotSupportedNonConstEnums);

            Assert.NotNull(result.Location);
            Assert.Equal(2, result.Location.Value.Line);
            Assert.Equal(5, result.Location.Value.Position);
        }

        [Fact]
        public void LineInfoOnSyntaxErrorFeature()
        {
            string code = 
@"namespace X {
    enum1 Foo {value = 42}
}";
            var result = ParseWithDiagnosticId(code, LogEventId.TypeScriptSyntaxError);

            Assert.NotNull(result.Location);
            Assert.Equal(2, result.Location.Value.Line);
            Assert.Equal(11, result.Location.Value.Position);
        }
    }
}
