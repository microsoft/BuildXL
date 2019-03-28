// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidClassRule : DsTest
    {
        public TestForbidClassRule(ITestOutputHelper output)
            : base(output)
        {}

        [Fact]
        public void FailOnClassDeclaration()
        {
            string code =
@"class Foo {
    constructor(public x: number) {
    }
}";
            var result = Parse(code).Diagnostics;
            result.ExpectErrorCode(LogEventId.NotSupportedClassDeclaration);
        }

        [Fact]
        public void FailOnClassExpression()
        {
            string code =
@"const x = class {
  y: number;
};";
            var result = Parse(code).Diagnostics;
            result.ExpectErrorCode(LogEventId.NotSupportedClassExpression);
        }

        [Fact]
        public void FailOnNewExpression()
        {
            string code =
@"class Foo {
    constructor(public x: number) {
    }
}
let f = new Foo(42);";
            var result = Parse(code).Diagnostics;

            result.ExpectErrorCode(LogEventId.NotSupportedNewExpression);
        }
    }
}
