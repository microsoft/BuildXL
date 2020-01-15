// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidEqualsEqualsRule : DsTest
    {
        public TestForbidEqualsEqualsRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestUseNonStrictEq()
        {
            var code = @"const x = (0 == undefined);";

            ParseWithDiagnosticId(code, LogEventId.NotSupportedNonStrictEquality);
        }
    }
}
