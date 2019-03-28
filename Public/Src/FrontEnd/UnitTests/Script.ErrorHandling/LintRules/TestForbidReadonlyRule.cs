// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
