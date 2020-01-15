// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceConstOnEnumRule : DsTest
    {
        public TestEnforceConstOnEnumRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestNonConstEnumsAreNotAllowed()
        {
            string code =
@"enum Test{ 
    value = 1
}
export const r = Test.value;
";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedNonConstEnums);
        }
    }
}
