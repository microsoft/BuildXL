// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class PathTranslatorTests
    {
        [Fact]
        public void BasicTest()
        {
            var pt = GetPathTranslator();

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(" d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(" x", "foo", "bar")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("x", "foo", "bar")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "bar  '") +
                (OperatingSystemHelper.IsPathComparisonCaseSensitive
                    ? PathGeneratorUtilities.GetAbsolutePath("X", "FOO", "BAR")
                    : PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "BAR")),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("x", "foo", "bar  '") + PathGeneratorUtilities.GetAbsolutePath("X", "FOO", "BAR")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(" d", "src", "123", "bar  '") +
                (OperatingSystemHelper.IsPathComparisonCaseSensitive
                    ? PathGeneratorUtilities.GetAbsolutePath("X", "FOO", "BAR")
                    : PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "BAR")),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(" x", "foo", "bar  '") + PathGeneratorUtilities.GetAbsolutePath("X", "FOO", "BAR")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("where is my head", "src", "foo", "bar  '") +
                (OperatingSystemHelper.IsPathComparisonCaseSensitive
                    ? PathGeneratorUtilities.GetAbsolutePath("X", "FOO", "BAR")
                    : PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "BAR")),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("where is my head", "src", "foo", "bar  '") + PathGeneratorUtilities.GetAbsolutePath("X", "FOO", "BAR")));
        }

        [Fact]
        public void PathsInMarkers()
        {
            var pt = GetPathTranslator();

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"logging a path: [d", "src", "123", "bar]"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"logging a path: [x", "foo", "bar]")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"##vso[task.uploadsummary]d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"##vso[task.uploadsummary]x", "foo", "bar")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@" \\?\d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@" \\?\x", "foo", "bar")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"\\?\d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"\\?\x", "foo", "bar")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"\??\d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"\??\x", "foo", "bar")));
        }

        [Fact]
        public void CheckForFalsePositives()
        {
            var pt = GetPathTranslator();

            // Don't match the patterns. Validate for off by one errors & false positives
            if (OperatingSystemHelper.IsUnixOS)
            {
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(null, @"\\?x", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(null, @"\\?x", "foo", "bar")));
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(null, @"comb", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(null, @"comb", "foo", "bar")));
            }
            else
            {
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(@"\\?x", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"\\?x", "foo", "bar")));
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(@"comb", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"comb", "foo", "bar")));
            }
        }

        [Fact]
        public void PrefixedPaths()
        {
            var pt = GetPathTranslator();
            // Quotes
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: type \"d", "src", "123", "a directory", "bar\""),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: type \"x", "foo", "a directory", "bar\"")));

            // @
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: cmd.exe @d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: cmd.exe @x", "foo", "bar")));

            // @"..."
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: cmd.exe @\"d", "src", "123", "a directory", "bar\""),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: cmd.exe @\"x", "foo", "a directory", "bar\"")));

            // Colon
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Path:d", "src", "123", "bar\""),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("[Log] Path:x", "foo", "bar\"")));

            // Pipe
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: a.bat|d", "src", "123", "bar\""),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: a.bat|x", "foo", "bar\"")));

            // Redirectors
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: a.bat >d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"[Log] Command line: a.bat >x", "foo", "bar")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: a.bat >>d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"[Log] Command line: a.bat >>x", "foo", "bar")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("[Log] Command line: a.bat <d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"[Log] Command line: a.bat <x", "foo", "bar")));
        }

        private PathTranslator GetPathTranslator()
        {
            return new PathTranslator(
                PathGeneratorUtilities.GetAbsolutePath("x", "foo"),
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123")
            );
        }
    }
}
