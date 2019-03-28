// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientEnvironmentTests : DsTest
    {
        public AmbientEnvironmentTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData("", false)]
        [InlineData(null, false)]


        [InlineData("1", true)]
        [InlineData("true", true)]
        [InlineData("TRUE", true)]
        [InlineData("TrUe", true)]

        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("FALSE", false)]
        [InlineData("FaLsE", false)]
        public void GetFlagTest(string value, bool expected)
        {
            var envGuid = $"test-${Guid.NewGuid().ToString()}";

            Environment.SetEnvironmentVariable(envGuid, value);

            var spec = $"const x = Environment.getFlag('{envGuid}');";

            var result = EvaluateExpressionWithNoErrors<bool>(spec, "x");

            Environment.SetEnvironmentVariable(envGuid, null);
            
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetFlagFailureTest()
        {
            var envGuid = $"test-${Guid.NewGuid().ToString()}";

            Environment.SetEnvironmentVariable(envGuid, "BADVALUE");

            var spec = $"const x = Environment.getFlag('{envGuid}');";

            EvaluateWithDiagnosticId(spec, LogEventId.InvalidTypeFormat, "x");

            Environment.SetEnvironmentVariable(envGuid, null);
        }
    }
}
