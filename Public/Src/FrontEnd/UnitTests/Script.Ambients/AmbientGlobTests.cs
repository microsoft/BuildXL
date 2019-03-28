// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientGlobTests : DsTest
    {
        public AmbientGlobTests(ITestOutputHelper output)
            : base(output, usePassThroughFileSystem: true)
        {
        }

        [Theory]
        [InlineData(@"glob(d`.`, 'a.txt')", "a.txt")]
        [InlineData(@"glob(d`.`, '*.txt')", "a.txt", "b.txt")]
        [InlineData(@"glob(d`.`, 'a.*')", "a.cs", "a.txt")]
        [InlineData(@"glob(d`.`, '*')", "a.cs", "a.txt", "b.cs", "b.txt", "other", "project.dsc")]
        [InlineData(@"glob(d`.`, '*.*')", "a.cs", "a.txt", "b.cs", "b.txt", "other", "project.dsc")]
        private void GlobCurrentFolder(string globFunction, params string[] expectedPaths)
        {
            TestGlob(globFunction, expectedPaths);
        }

        [Theory]
        [InlineData(@"glob(d`f1`, 'a.txt')", @"f1\a.txt")]
        [InlineData(@"glob(d`f1`, '*.txt')", @"f1\a.txt", @"f1\b.txt")]
        [InlineData(@"glob(d`f1`, 'a.*')", @"f1\a.cs", @"f1\a.txt")]
        [InlineData(@"glob(d`f1`, '*')", @"f1\a.cs", @"f1\a.txt", @"f1\b.cs", @"f1\b.txt", @"f1\other")]
        [InlineData(@"glob(d`f1`, '*.*')", @"f1\a.cs", @"f1\a.txt", @"f1\b.cs", @"f1\b.txt", @"f1\other")]
        private void GlobF1Folder(string globFunction, params string[] expectedPaths)
        {
            TestGlob(globFunction, expectedPaths);
        }

        [Theory]
        [InlineData(@"glob(d`.`, '*\\a.txt')", @"f1\a.txt", @"f2\a.txt")]
        [InlineData(@"glob(d`.`, '*/*.txt')", @"f1\a.txt", @"f1\b.txt", @"f2\a.txt", @"f2\b.txt")]
        [InlineData(@"glob(d`.`, '*\\a.*')", @"f1\a.cs", @"f1\a.txt", @"f2\a.cs", @"f2\a.txt")]
        [InlineData(@"glob(d`.`, '*/other')", @"f1\other", @"f3\other", @"f4\other")]
        [InlineData(@"glob(d`.`, '*\\*.*')",
            @"f1\a.cs", @"f1\a.txt", @"f1\b.cs", @"f1\b.txt", @"f1\other",
            @"f2\a.cs", @"f2\a.txt", @"f2\b.cs", @"f2\b.txt",
            @"f3\other",
            @"f4\other")]
        private void GlobSkippingFolder(string globFunction, params string[] expectedPaths)
        {
            TestGlob(globFunction, expectedPaths);
        }

        [Theory]
        [InlineData(@"globFolders(d`.`, 'f*')", @"f1", @"f2", @"f3", @"f4")]
        [InlineData(@"globFolders(d`.`, '*')", @"f1", @"f2", @"f3", @"f4", @"x.cs")]
        [InlineData(@"globFolders(d`f4`, '*')", @"f4\f5", @"f4\f6")]
        [InlineData(@"globFolders(d`f4`, '*5')", @"f4\f5")]
        [InlineData(@"globFolders(d`x.cs`, '*')", @"x.cs\other")]
        private void GlobFolders(string globFunction, params string[] expectedPaths)
        {
            TestGlob(globFunction, expectedPaths);
        }

        [Theory]
        [InlineData(@"globFolders(d`.`, 'f*', true)", @"f1", @"f2", @"f3", @"f4", @"f4\f5", @"f4\f6", @"x.cs\other\f7")]
        [InlineData(@"globFolders(d`.`, '*', true)", @"f1", @"f2", @"f3", @"f4", @"f4\f5", @"f4\f6", @"x.cs", @"x.cs\other", @"x.cs\other\f7")]
        [InlineData(@"globFolders(d`f4`, '*', true)", @"f4\f5", @"f4\f6")]
        [InlineData(@"globFolders(d`f4`, '*5', true)", @"f4\f5")]
        [InlineData(@"globFolders(d`x.cs`, '*', true)", @"x.cs\other", @"x.cs\other\f7")]
        private void GlobFoldersRecursively(string globFunction, params string[] expectedPaths)
        {
            TestGlob(globFunction, expectedPaths);
        }

        [Theory]
        [InlineData(@"globR(d`.`, 'a.cs')", @"a.cs", @"f1\a.cs", @"f2\a.cs", @"f4\f5\a.cs", @"x.cs\other\f7\a.cs")]
        [InlineData(@"globR(d`.`, '*.cs')", @"a.cs", @"b.cs", @"f1\a.cs", @"f1\b.cs", @"f2\a.cs", @"f2\b.cs", @"f4\f5\a.cs", @"f4\f5\b.cs", @"x.cs\other\f7\a.cs")]
        [InlineData(@"globR(d`x.cs`, '*')", @"x.cs\other\f7\a.cs")]
        private void GlobRecursive(string globFunction, params string[] expectedPaths)
        {
            TestGlob(globFunction, expectedPaths);
        }

        private void TestGlob(string globFunction, params string[] expectedPaths)
        {
            var sortedExpectedPaths = expectedPaths.OrderBy(p => p, StringComparer.InvariantCultureIgnoreCase).ToArray();
            XAssert.AreArraysEqual(sortedExpectedPaths, expectedPaths, expectedResult: true, "Must pass sorted expected paths");

            var result = Build()
                .AddSpec("src/project.dsc", "const result = " + globFunction + ";")
                .RootSpec("src/project.dsc")
                .AddTestFiles()
                .EvaluateExpressionWithNoErrors<EvaluatedArrayLiteral>("result");

            var actualPaths = new string[result.Count];
            for (int i = 0; i < actualPaths.Length; i++)
            {
                var path = AbsolutePath.Invalid;
                switch (result[i].Value)
                {
                    case FileArtifact file:
                        path = file.Path;
                        break;
                    case DirectoryArtifact dir:
                        path = dir.Path;
                        break;
                    default:
                        XAssert.Fail("Unexpected return value");
                        break;
                }

                actualPaths[i] = path.ToString(PathTable);
            }

            Array.Sort(actualPaths, StringComparer.InvariantCultureIgnoreCase);

            XAssert.AreEqual(
                expectedPaths.Length,
                result.Count,
                "Sizes don't line up: encountered: " + string.Join(",", actualPaths.Select(ap => ap.Replace(TestRoot, ""))));

            for (int i = 0; i < Math.Min(result.Count, expectedPaths.Length); i++)
            {
                AssertCanonicalEquality("src\\" + expectedPaths[i], actualPaths[i]);
            }
        }
    }

    internal static class Extensions
    {
        internal static SpecEvaluationBuilder AddTestFiles(this SpecEvaluationBuilder builder)
        {
            return builder
                .AddFile(@"src\a.txt", "a")
                .AddFile(@"src\b.txt", "a")
                .AddFile(@"src\a.cs", "a")
                .AddFile(@"src\b.cs", "a")
                .AddFile(@"src\other", "a")
                .AddFile(@"src\f1\a.txt", "a")
                .AddFile(@"src\f1\b.txt", "a")
                .AddFile(@"src\f1\a.cs", "a")
                .AddFile(@"src\f1\b.cs", "a")
                .AddFile(@"src\f1\other", "a")
                .AddFile(@"src\f2\a.txt", "a")
                .AddFile(@"src\f2\b.txt", "a")
                .AddFile(@"src\f2\a.cs", "a")
                .AddFile(@"src\f2\b.cs", "a")
                .AddFile(@"src\f3\other", "a")
                .AddFile(@"src\f4\f5\a.txt", "a")
                .AddFile(@"src\f4\f5\b.txt", "a")
                .AddFile(@"src\f4\f5\a.cs", "a")
                .AddFile(@"src\f4\f5\b.cs", "a")
                .AddFile(@"src\f4\f5\other", "a")
                .AddFile(@"src\f4\f6\other", "a")
                .AddFile(@"src\f4\other", "a")
                .AddFile(@"src\x.cs\other\f7\a.cs", "a");
        }
    }
}
