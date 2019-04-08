// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestErrorMessagesForParseErrors : DsTest
    {
        public TestErrorMessagesForParseErrors(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void ErrorMessageForMissingCommaInObjectLiteralShouldBeReasonable()
        {
            // This was a Bug #469818
            string code =
@"function foo(arg: {out: number, pdb: number, sources: number[], references: number[]}): number {
    return 42;
}

const r = foo({
  out: 1,
  pdb: 2
  sources: [3, 4],
  references: undefined
  });";

            var diagnostic = ParseWithDiagnosticId(code, LogEventId.TypeScriptSyntaxError);
            Assert.True(diagnostic.Message.Contains("',' expected."), I($"Message returned from parser: {diagnostic.Message}"));
            Assert.NotNull(diagnostic.Location);
            Assert.Equal(8, diagnostic.Location.Value.Line);
            Assert.Equal(3, diagnostic.Location.Value.Position);
        }

        [Fact]
        public void ErrorMessageForMissingCommaInParameterListShouldBeReasonable()
        {
            // This was a bug469824
            string code =
@"function foo(arg: {out: number, pdb: number sources: number[], references: number[]}): number {
    return 42;
}";

            var diagnostic = ParseWithDiagnosticId(code, LogEventId.TypeScriptSyntaxError);
            Assert.True(diagnostic.Message.Contains("';' expected."), I($"Message returned from parser: {diagnostic.Message}"));

            Assert.NotNull(diagnostic.Location);
            Assert.Equal(1, diagnostic.Location.Value.Line);
            Assert.Equal(45, diagnostic.Location.Value.Position);
        }

        [Fact]
        public void MistypedImportKeywordShouldGiveAGoodErrorMessage()
        {
            // This was a bug469829
            string code =
@"aimport * from ""DotNet.Sdk"";";

            var diagnostic = ParseWithDiagnosticId(code, LogEventId.TypeScriptSyntaxError);
            Assert.True(diagnostic.Message.Contains("';' expected."), I($"Message returned from parser: {diagnostic.Message}"));

            Assert.NotNull(diagnostic.Location);
            Assert.Equal(1, diagnostic.Location.Value.Line);
            Assert.Equal(16, diagnostic.Location.Value.Position);
        }
    }
}
