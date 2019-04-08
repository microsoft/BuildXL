// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Tracing;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class InterpretFilesAndDirectories : DsTest
    {
        private readonly string m_testAbsolutePath = OperatingSystemHelper.IsUnixOS ? "/" : "c:/";

        public InterpretFilesAndDirectories(ITestOutputHelper output)
            : base(output)
        {}

        [Theory]
        [InlineData("extension", ".cs")]
        [InlineData("nameWithoutExtension", "foo")]
        [InlineData("parent", "p`c:/root`")]
        [InlineData("name", "foo.cs")]
        [InlineData("path", "p`c:/root/foo.cs`")]
        public void TestAmbientPropertiesForFile(string property, string expectedResult)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                expectedResult = expectedResult.Replace("`c:", "`");
            }

            const string SpecTemplate = @"
  const ff: File = f`{0}root/foo.cs`;
  export const result = ff.{1}.toString();";

            string spec = string.Format(SpecTemplate, m_testAbsolutePath, property);
            var result = EvaluateExpressionWithNoErrors(spec, "result");

            // Path could return drive letter in differnt casing. Using lowercase to avoid build breaks on different machines.
            Assert.Equal(expectedResult, result.ToString().ToLowerInvariant());
        }

        [Theory]
        [InlineData("extension", ".cs")]
        [InlineData("nameWithoutExtension", "foo")]
        [InlineData("parent", "p`c:/root`")]
        [InlineData("name", "foo.cs")]
        [InlineData("path", "p`c:/root/foo.cs`")]
        public void TestAmbientPropertiesForPath(string property, string expectedResult)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                expectedResult = expectedResult.Replace("`c:", "`");
            }

            const string SpecTemplate = @"
  const ff: File = p`{0}root/foo.cs`;
  export const result = ff.{1}.toString();";

            string spec = string.Format(SpecTemplate, m_testAbsolutePath, property);
            var result = EvaluateExpressionWithNoErrors(spec, "result");

            // Path could return drive letter in differnt casing. Using lowercase to avoid build breaks on different machines.
            Assert.Equal(expectedResult, result.ToString().ToLowerInvariant());
        }

        [Theory]
        [InlineData("nameWithoutExtension", "root")]
        [InlineData("parent", "p`c:/`")]
        [InlineData("name", "root")]
        [InlineData("path", "p`c:/root`")]
        public void TestAmbientPropertiesForDirectory(string property, string expectedResult)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                expectedResult = expectedResult.Replace("`c:", "`");
            }

            const string SpecTemplate = @"
  const ff: File = d`{0}root`;
  export const result = ff.{1}.toString();";

            string spec = string.Format(SpecTemplate, m_testAbsolutePath, property);
            var result = EvaluateExpressionWithNoErrors(spec, "result");

            // Path could return drive letter in differnt casing. Using lowercase to avoid build breaks on different machines.
            Assert.Equal(expectedResult, result.ToString().ToLowerInvariant());
        }

        [Fact]
        public void CreateFileWithPathInterpolation()
        {
            string spec = String.Format(@"
namespace M {{
  const path = p`{0}temp`;
  const ff: File = f`${{path}}/foo.cs`;

  const pp: Path = p`${{path}}/foo.cs`;

  export const areEqual = ff.path === pp;
}}", m_testAbsolutePath);

            var result = EvaluateExpressionWithNoErrors(spec, "M.areEqual");
            Assert.Equal(true, result);
        }

        [Fact]
        public void InterpolatedFilePathWithInvalidCharacters()
        {
            // This still should fail with the same 'path interpolation error'.
            string spec = String.Format(@"
namespace M {{
  const path = p`{0}temp`;
  const ff: File = f`${{path}}foo.cs`;

  const pp: Path = p`${{path}}/foo.cs`;

  export const areEqual = ff.path === pp;
}}", m_testAbsolutePath);

            EvaluateWithDiagnosticId(spec, LogEventId.InvalidPathInterpolationExpression);
        }

        [Fact]
        public void DirectoryWithPathInterpolation()
        {
            string spec = String.Format(@"
namespace M {{
  const path = p`{0}temp`;
  const dd: Directory = d`${{path}}/foo`;

  const pp: Path = p`${{path}}/foo`;

  export const areEqual = dd.path === pp;
}}", m_testAbsolutePath);

            var result = EvaluateExpressionWithNoErrors(spec, "M.areEqual");
            Assert.Equal(true, result);
        }

        [Fact]
        public void InterpolatedDirectoryPathWithInvalidCharacters()
        {
            // This still should fail with the same 'path interpolation error'.
            string spec = String.Format(@"
namespace M {{
  const path = p`{0}temp`;
  const dd: Directory = d`${{path}}foo`;

  const pp: Path = p`${{path}}/foo`;

  export const areEqual = dd.path === pp;
}}", m_testAbsolutePath);

            EvaluateWithDiagnosticId(spec, LogEventId.InvalidPathInterpolationExpression);
        }
    }
}
