// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class TestRuntimeBehavior : DsTest
    {
        public TestRuntimeBehavior(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestArraySpreadWithUndefined()
        {
            var spec = @"
namespace M 
{
    const x: number[] = undefined;
    export const z = [...x].length;
}";
            var result = EvaluateWithFirstError(spec);

            // TypeScript emits following message: 'Cannot read property 'slice' of undefined'
            Assert.Equal((int)LogEventId.FailResolveSelectorDueToUndefined, result.ErrorCode);
        }

        [Fact]
        public void TestFunctionInvocationOnUndefined()
        {
            var spec = @"
namespace M 
{
    const q: Path = undefined;
    export const path = q.changeExtension(""bar"");  
}";
            var result = EvaluateWithFirstError(spec);

            // TypeScript emits following message: 'Cannot read property 'changeExtension' of undefined'
            Assert.Equal((int)LogEventId.FailResolveSelectorDueToUndefined, result.ErrorCode);
        }

        [Fact]
        public void TestAmbientFunctionInvocationWithUndefinedArgument()
        {
            var spec = @"
namespace M 
{
    export const dir = Context.getNewOutputDirectory(undefined);
}";
            var result = EvaluateWithFirstError(spec);

            // This error is purely from DScript evaluator, there is no corresponding TypeScript error
            Assert.Equal((int)LogEventId.UnexpectedValueTypeOnConversion, result.ErrorCode);
        }
    }
}
