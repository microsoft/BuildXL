// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceSingleDeclarationInForOfRule : DsTest
    {
        public TestEnforceSingleDeclarationInForOfRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestInvalidForOfInitializer()
        {
            string spec =
@"namespace M
{
    function f() {    
        for (let test, test1 of object) { }
    }
}";
            ParseWithDiagnosticId(spec, LogEventId.InvalidForOfVarDeclarationInitializer);
        }
    }
}
