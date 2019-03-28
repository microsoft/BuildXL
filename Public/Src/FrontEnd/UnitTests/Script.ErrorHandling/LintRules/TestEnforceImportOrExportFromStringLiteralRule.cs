// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using TypeScript.Net.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceImportOrExportFromStringLiteralRule : DsTest
    {
        public TestEnforceImportOrExportFromStringLiteralRule(ITestOutputHelper output)
            : base(output)
        {}

        [Theory]
        [InlineData("import * as M")]
        [InlineData("export *")]
        public void TestImportOrExportWithInvalidNumericLiteral(string importOrExportClause)
        {
            string code = I($"{importOrExportClause} from 42;");

            ParseWithDiagnosticId(code, LogEventId.ImportModuleSpecifierIsNotAStringLiteral);
        }

        [Theory]
        [InlineData("import * as M")]
        [InlineData("export *")]
        public void TestImportOrExportWithInvalidFunctionExpression(string importOrExportClause)
        {
            string code = I($"{importOrExportClause} from f(42);");

            ParseWithDiagnosticId(code, LogEventId.ImportModuleSpecifierIsNotAStringLiteral);
        }

        [Fact]
        public void TestValidExportWithNoPathSpecifier()
        {
            string spec1 = @"
const x = 42;
export {x};
";
            Build().AddSpec("spec1.dsc", spec1).ParseWithNoErrors();
        }
    }
}
