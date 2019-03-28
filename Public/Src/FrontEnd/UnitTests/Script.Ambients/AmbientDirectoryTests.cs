// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientDirectoryTests : DsTest
    {
        public AmbientDirectoryTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestDirectoryFromPath()
        {
            var spec = @"
export const result = Directory.fromPath(p`fooBarDirectory`).toString();
";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("result");
            Assert.Contains("fooBarDirectory", result.ToString());
        }

        [Fact]
        public void DirectoryExistsOnceFileCreatedInIt()
        {
            var spec = @"
export const result = Directory.exists(d`foo`);
";
            var result = Build().AddSpec(spec).AddFile("foo/foo.txt", "").EvaluateExpressionWithNoErrors("result");
            Assert.Equal(true, result);
        }

        [Fact]
        public void DirectoryDoesNotExists()
        {
            var spec = @"
export const result = Directory.exists(d`foo`);
";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("result");
            Assert.Equal(false, result);
        }

        [Fact]
        public void GetToDiangosticString()
        {
            var spec = @"
const result: string = d`a/b`.toDiagnosticString();
";
            var result = Build().AddSpec(spec).EvaluateExpressionWithNoErrors("result");
            AssertCanonicalEquality(@"a\b", result as string);
        }
    }
}
