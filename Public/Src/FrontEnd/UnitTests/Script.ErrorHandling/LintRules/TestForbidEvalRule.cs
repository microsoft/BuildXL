// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidEvalRule : DsTest
    {
        public TestForbidEvalRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestEvalIsNotAllowed()
        {
            string code = @"
function f() {
   return eval(""f()"");
}";
            ParseWithDiagnosticId(code, LogEventId.EvalIsNotAllowed);
        }
    }
}
