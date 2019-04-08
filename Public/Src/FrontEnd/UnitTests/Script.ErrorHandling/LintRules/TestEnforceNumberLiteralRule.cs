// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceNumberLiteralRule : DsTest
    {
        public TestEnforceNumberLiteralRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void NoFloatingPoints()
        {
            string code =
@"export const r: number = 3.14;
";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedFloatingPoints);
        }

        [Fact]
        public void NoLiteralOverflow()
        {
            string code =
                @"export const r: number = 5E65101;
";
            ParseWithDiagnosticId(code, LogEventId.ReportLiteralOverflows);
        }

        [Fact]
        public void NoNaN()
        {
            string code =
@"export const r: number = NaN;
";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedFloatingPoints);
        }

        [Fact]
        public void AllowNaNFromNestedNamespaces()
        {
            string code =
@"export const r: number = foo.NaN;
";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedFloatingPoints);
        }

        [Fact]
        public void AllowNaNWithToString()
        {
            string code =
@"export const r = NaN.toString();
";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedFloatingPoints);
        }
    }
}
