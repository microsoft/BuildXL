// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestIdentifiersValidity : DsTest
    {
        public TestIdentifiersValidity(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public void InterpreterShouldNotCrashOnUnderscores()
        {
            string code =
@"namespace D__$_ {
    function __bar() {return 42;}
    export const $ = __bar();
}

export const r = D__$_.$;";

            var result = EvaluateExpressionWithNoErrors(code, "r");
            Assert.Equal(42, result);
        }
    }
}
