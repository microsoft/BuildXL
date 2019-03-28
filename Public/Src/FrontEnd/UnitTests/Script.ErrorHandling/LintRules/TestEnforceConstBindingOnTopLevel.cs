// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
