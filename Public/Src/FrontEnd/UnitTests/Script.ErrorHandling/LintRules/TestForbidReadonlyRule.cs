// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidReadonlyRule : DsTest
    {
        public TestForbidReadonlyRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestExplicitReadonlyIsNotSupported()
        {
            string code = @"
interface T {
    readonly a: number;   
}
";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedReadonlyModifier);
        }
    }
}
