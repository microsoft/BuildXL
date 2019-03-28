// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidDivisionOperator : DsTest
    {
        public TestForbidDivisionOperator(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void DivisionIsNotSupported()
        {
            string code = @"
export const r = 1/2;
";
            var result = EvaluateWithFirstError(code, "r");

            Assert.Equal((int)LogEventId.DivisionOperatorIsNotSupported, result.ErrorCode);
        }
    }
}
