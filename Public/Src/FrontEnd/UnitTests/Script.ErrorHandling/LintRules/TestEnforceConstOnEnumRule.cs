// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
