// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientPathAtomTests : DsTest
    {
        public AmbientPathAtomTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void CreateSanitizedNoInvalidChars()
        {
            var result = EvaluateSpec(@"
namespace M
{
    export const result = PathAtom.createSanitized(""hello.txt"") === PathAtom.create(""hello.txt"");
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void CreateSanitizedSlashReplacedOnAllPlatforms()
        {
            // '/' is invalid in file names on both Windows and Linux
            var result = EvaluateSpec(@"
namespace M
{
    export const result = PathAtom.createSanitized(""a/b"") === PathAtom.create(""a_b"");
}
", new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData("he<l>lo", "he_l_lo")]
        [InlineData("a|b?c*d", "a_b_c_d")]
        [InlineData("file:name", "file_name")]
        [InlineData("my<file>name.txt", "my_file_name.txt")]
        public void CreateSanitizedReplacesWindowsInvalidChars(string input, string expected)
        {
            var result = EvaluateSpec(string.Format(@"
namespace M
{{
    export const result = PathAtom.createSanitized(""{0}"") === PathAtom.create(""{1}"");
}}
", input, expected), new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }

        [TheoryIfSupported(requiresUnixBasedOperatingSystem: true)]
        [InlineData("he<l>lo")]
        [InlineData("a:b|c")]
        [InlineData("a?b*c")]
        public void CreateSanitizedPreservesLinuxValidChars(string input)
        {
            var result = EvaluateSpec(string.Format(@"
namespace M
{{
    export const result = PathAtom.createSanitized(""{0}"") === PathAtom.create(""{0}"");
}}
", input), new[] { "M.result" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);
            Assert.Equal(true, result.Values[0]);
        }
    }
}
