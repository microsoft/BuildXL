// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientPathTests : DsTest
    {
        public AmbientPathTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void GetToDiangosticString()
        {
            var spec = @"
const result: string = p`a/b/foo.cs`.toDiagnosticString();
";
            var result = (string)Build().AddSpec(spec).EvaluateExpressionWithNoErrors("result");
            AssertCanonicalEquality(@"a\b\foo.cs", result);
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(@"c:\test\windows\paths")]
        [InlineData(@"c:/test/windows/with/other/slashes")]
        [InlineData(@"c:/test/windows/with/mixed\slashes")]
        [InlineData(@"c:/test/windows/with/mixed\slashes")]
        [InlineData(@"relative should fail", "This is not an absolute path")]
        [InlineData(@"relative/should/fail", "This is not an absolute path")]
        [InlineData(@"c:/no:/allowed", "not a valid path character")]
        [InlineData(@"\\?\C:", "Device Paths are not supported")]
        public void TestCreateAbsolutePathFromStringWindows(string pathToTest, string errorIfInvalid = null)
        {
            TestCreateAbsolutePathFromString(pathToTest, errorIfInvalid);
        }

        [TheoryIfSupported(requiresUnixBasedOperatingSystem: true)]
        [InlineData(@"/this/is an/absolute/unix path")]
        [InlineData(@"/this/is an/absolute/unix path\with\mixed/slashes")]
        [InlineData(@"/this:/is/allowed/on/unix")]
        [InlineData(@"relative should fail", "This is not an absolute path")]
        [InlineData(@"relative/should/fail", "This is not an absolute path")]
        [InlineData(@"relative\should\fail", "This is not an absolute path")]
        public void TestCreateAbsolutePathFromStringUnix(string pathToTest, string errorIfInvalid = null)
        {
            TestCreateAbsolutePathFromString(pathToTest, errorIfInvalid);
        }

        private void TestCreateAbsolutePathFromString(string pathToTest, string errorIfInvalid)
        {
            var spec = $@"
const result: Path = Path.createFromAbsolutePathString(""{pathToTest.Replace("\\", "\\\\")}"");
";
            var parsedSpec = Build().AddSpec(spec);
            if (string.IsNullOrEmpty(errorIfInvalid))
            {
                var result = (AbsolutePath)parsedSpec.EvaluateExpressionWithNoErrors("result");

                var canonicalActualPath = result.ToString(PathTable).Replace("/", "\\").ToLowerInvariant();
                var canonicalExpectedPath = pathToTest.Replace("/", "\\").ToLowerInvariant();
                Assert.Equal(canonicalExpectedPath, canonicalActualPath);
            }
            else
            {
                var diagnostic = parsedSpec.EvaluateWithFirstError("result");
                Assert.True(diagnostic.Message.Contains(errorIfInvalid), $"Did not find {errorIfInvalid} in {diagnostic.Message}");
            }
        }
    }
}
