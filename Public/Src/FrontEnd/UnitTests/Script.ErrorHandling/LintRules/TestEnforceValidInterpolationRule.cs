// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
