// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Collections;

namespace Test.BuildXL.Scheduler
{
    public class SourceSealWithPatternsTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public SourceSealWithPatternsTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestSourceSealPatterns()
        {
            var pt = new PathTable();
            var st = pt.StringTable;
            var dir1 = AbsolutePath.Create(pt, X("/c/sourceseal"));

            var f1 = dir1.Combine(pt, "file1");
            var f2 = dir1.Combine(pt, "file2");
            var f3 = dir1.Combine(pt, "file3.txt");
            var f4 = dir1.Combine(pt, "file4.txt");
            var f5 = dir1.Combine(pt, "file5.txt");
            var f6 = dir1.Combine(pt, "a");

            var nestedDir = AbsolutePath.Create(pt, X("/c/sourceseal/nested"));
            var nestedf1 = nestedDir.Combine(pt, "nested1");
            var nestedf2 = nestedDir.Combine(pt, "nested2.cs");

            var pattern1 = StringId.Create(st, "*");
            var pattern2 = StringId.Create(st, "*.txt");
            var pattern3 = StringId.Create(st, "*.cs");
            var pattern4 = StringId.Create(st, "file5.txt");
            var pattern5 = StringId.Create(st, "file1*");

            SourceSealWithPatterns sourceSeal1 = new SourceSealWithPatterns(dir1, ReadOnlyArray<StringId>.From(new[] { pattern1, pattern2 }));
            // Wildcard matches everything
            AssertTrue(sourceSeal1.Contains(pt, f1));
            AssertTrue(sourceSeal1.Contains(pt, f2));
            AssertTrue(sourceSeal1.Contains(pt, f3));
            AssertTrue(sourceSeal1.Contains(pt, f4));
            AssertTrue(sourceSeal1.Contains(pt, f5));
            AssertTrue(sourceSeal1.Contains(pt, f6));

            AssertTrue(!sourceSeal1.Contains(pt, nestedf1));
            AssertTrue(!sourceSeal1.Contains(pt, nestedf2));
            AssertTrue(sourceSeal1.Contains(pt, nestedf1, isTopDirectoryOnly: false));
            AssertTrue(sourceSeal1.Contains(pt, nestedf2, isTopDirectoryOnly: false));

            SourceSealWithPatterns sourceSeal2 = new SourceSealWithPatterns(dir1, ReadOnlyArray<StringId>.From(new[] { pattern3, pattern4, pattern5 }));
            // *.cs, file5.txt, file1*
            AssertTrue(sourceSeal2.Contains(pt, f1));
            AssertTrue(!sourceSeal2.Contains(pt, f2));
            AssertTrue(!sourceSeal2.Contains(pt, f3));
            AssertTrue(!sourceSeal2.Contains(pt, f4));
            AssertTrue(sourceSeal2.Contains(pt, f5));
            AssertTrue(!sourceSeal2.Contains(pt, f6));
            AssertTrue(!sourceSeal2.Contains(pt, nestedf1));
            AssertTrue(!sourceSeal2.Contains(pt, nestedf2));
            AssertTrue(!sourceSeal2.Contains(pt, nestedf1, isTopDirectoryOnly: false));
            AssertTrue(sourceSeal2.Contains(pt, nestedf2, isTopDirectoryOnly: false));

        }

        [Fact]
        public void SingleWildcardPatternMatchTests()
        {
            string filename = "file.txt";
            string[] passingPatterns = new string[] { "*", "fi*", "*t", "*.txt", "*file.txt", "file*.txt", "file*", "file.tx*" };
            foreach (var pattern in passingPatterns)
            {
                AssertTrue(SourceSealWithPatterns.SingleWildcardPatternMatch(filename, pattern), $"File name, '{filename}' does not pass the pattern, '{pattern}'");
            }

            string[] notPassingPatterns = new string[] { "*a", "i*", "f*axt", "file1.txt", "file", "file.tx", "abcdefgyhu"};
            foreach (var pattern in notPassingPatterns)
            {
                AssertTrue(!SourceSealWithPatterns.SingleWildcardPatternMatch(filename, pattern), $"File name, '{filename}' passes the pattern, '{pattern}'");
            }
        }

        [Fact]
        public void SingleWildcardPatternMatchTestsWithEmptyString()
        {
            string filename = "";
            string[] passingPatterns = new string[] { "*"};
            foreach (var pattern in passingPatterns)
            {
                AssertTrue(SourceSealWithPatterns.SingleWildcardPatternMatch(filename, pattern), $"File name, '{filename}' does not pass the pattern, '{pattern}'");
            }

            string[] notPassingPatterns = new string[] { "*a", "i*", "f*axt", "file1.txt", "file", "file.tx", "abcdefgyhu" };
            foreach (var pattern in notPassingPatterns)
            {
                AssertTrue(!SourceSealWithPatterns.SingleWildcardPatternMatch(filename, pattern), $"File name, '{filename}' passes the pattern, '{pattern}'");
            }
        }
    }
}
