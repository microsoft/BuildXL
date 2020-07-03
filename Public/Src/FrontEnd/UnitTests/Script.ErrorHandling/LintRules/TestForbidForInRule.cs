// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidForInRule : DsTest
    {
        public TestForbidForInRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestForInLoopNotSupported()
        {
            string code = @"
namespace M {
    function fun() {
        for (let x in [1, 2]) {
        }
        return 42;
    }
}";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedForInLoops);
        }
    }
}
