// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidDefaultArgumentRule : DsTest
    {
        public TestForbidDefaultArgumentRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestParametersDontSupportDefaults()
        {
            string code = @"
namespace M {
    function fun(a: int = 3) {
        return a;
    }
}";
            ParseWithDiagnosticId(code, LogEventId.NotSupportedDefaultArguments);
        }
    }
}
