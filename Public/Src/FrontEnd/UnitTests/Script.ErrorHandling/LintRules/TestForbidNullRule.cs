// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidNullRule : DsTest
    {
        public TestForbidNullRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestNullIsNotAllowed()
        {
            string code = @"const x = null;";
            ParseWithDiagnosticId(code, LogEventId.NullNotAllowed);
        }
    }
}
