// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestEnforceValidInterpolationRule : DsTest
    {
        public TestEnforceValidInterpolationRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestInvalidStringInterpolation()
        {
            string spec =
@"const x = q`this is a {weird} interpolation`;";

            ParseWithDiagnosticId(spec, LogEventId.NotSupportedInterpolation);
        }
    }
}
