// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceConstBindingOnTopLevel : DsTest
    {
        public TestEnforceConstBindingOnTopLevel(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void NoLetBindingOnTopLevel()
        {
            string code =
@"let x = 42;";
            var result = Parse(code).Diagnostics;

            result.ExpectErrorCode(LogEventId.OnlyConstBindingOnNamespaceLevel);
        }

        [Fact]
        public void LetBindingIsAllowedAtFunctionLevel()
        {
            string code =
@"function foo() {let x = 42;}";

            Parse(code).NoErrors();
        }
    }
}
