// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidModifiersOnImportRule : DsTest
    {
        public TestForbidModifiersOnImportRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestExportImportIsNotSupported()
        {
            string code = @"export import * as A from 'Sdk.Prelude';";
            var result = Build()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .ParseWithDiagnostics();

            result.ExpectErrorCode(LogEventId.NotSupportedExportImport);
        }

        [Theory]
        [InlineData("abstract")]
        [InlineData("async")]
        [InlineData("declare")]
        [InlineData("public")]
        [InlineData("private")]
        [InlineData("protected")]
        [InlineData("static")]
        public void TestModifierOnImportIsNotSupported(string modifier)
        {
            string code = $@"{modifier} import * as A from 'Sdk.Prelude';";
            var result = Build()
                .AddSpec("foo.dsc", code)
                .RootSpec("foo.dsc")
                .ParseWithDiagnostics();

            result.ExpectErrorCode(LogEventId.NotSupportedModifiersOnImport);
        }
    }
}
