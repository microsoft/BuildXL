// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientContractTests : DsTest
    {
        public AmbientContractTests(ITestOutputHelper output) 
            : base(output)
        {
        }

        [Fact]
        public void TestContractAssert()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function contract(condition: boolean) : void { Contract.assert(condition, ""contract: assertion violation""); }

    export const x = contract(1 === 2);
    export const y = contract(1 === 1);
}", new[] { "M.x", "M.y" });

            result.ExpectErrors(count: 1);
            result.ExpectValues(count: 2);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            Assert.Equal(UndefinedValue.Instance, result.Values[1]);

            result.ExpectErrorCode((int)LogEventId.ContractAssert, count: 1);
            result.ExpectErrorMessageSubstrings(
                new[]
                {
                    "contract: assertion violation",
                });
        }

        [Fact]
        public void TestContractRequire()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function contract(condition: boolean) : void { Contract.requires(condition, ""contract: precondition violation""); }

    export const x = contract(1 === 2);
    export const y = contract(1 === 1);
}", new[] { "M.x", "M.y" });

            result.ExpectErrors(count: 1);
            result.ExpectValues(count: 2);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            Assert.Equal(UndefinedValue.Instance, result.Values[1]);

            result.ExpectErrorCode((int)LogEventId.ContractRequire, count: 1);
            result.ExpectErrorMessageSubstrings(
                new[]
                {
                    "contract: precondition violation",
                });
        }

        [Fact]
        public void TestContractFail()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function contract() : void { Contract.fail(""contract: failure""); }

    export const x = contract();
}", new[] { "M.x" });

            result.ExpectErrors(count: 1);
            result.ExpectValues(count: 1);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);

            result.ExpectErrorCode((int)LogEventId.ContractFail, count: 1);
            result.ExpectErrorMessageSubstrings(
                new[]
                {
                    "contract: failure",
                });
        }

        [Fact]
        public void TestContractWarn()
        {
            var result = EvaluateSpec(@"
namespace M
{
    function contract() : void { Contract.warn(""contract: warn""); }

    export const x = contract();
}", new[] { "M.x" });

            result.ExpectErrors(count: 0);
            result.ExpectValues(count: 1);
            Assert.Equal(UndefinedValue.Instance, result.Values[0]);

            result.ExpectErrorCode((int)LogEventId.ContractWarn, count: 1);
        }
    }
}
