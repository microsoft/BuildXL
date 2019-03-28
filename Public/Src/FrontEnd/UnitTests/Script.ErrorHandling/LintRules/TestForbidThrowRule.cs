// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public class TestForbidThrowRule : DsTest
    {
        public TestForbidThrowRule(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(GetStuffToThrow))]
        public void TestThrowNotAllowedInIIFE(string somethingToThrow)
        {
            string code = $"const x = (() => {{ throw {somethingToThrow}; }})();";
            ParseWithDiagnosticId(code, LogEventId.ThrowNotAllowed);
        }

        [Theory]
        [MemberData(nameof(GetStuffToThrow))]
        public void TestThrowNotAllowedInFunction(string somethingToThrow)
        {
            string code = $"function f() {{ throw {somethingToThrow}; }}";
            ParseWithDiagnosticId(code, LogEventId.ThrowNotAllowed);
        }

        public static IEnumerable<object[]> GetStuffToThrow()
        {
            yield return new[] { "'hi'" };
            yield return new[] { "\"hi\"" };
            yield return new[] { "1" };
            yield return new[] { "1 + 2" };
    }
    }
}
