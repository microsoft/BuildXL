// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(" b", "foo", "bar")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("b", "foo", "bar")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "bar  '") +
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "BAR"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("b", "foo", "bar  '") + PathGeneratorUtilities.GetAbsolutePath("B", "FOO", "BAR")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(" d", "src", "123", "bar  '") +
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "BAR"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(" b", "foo", "bar  '") + PathGeneratorUtilities.GetAbsolutePath("B", "FOO", "BAR")));

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath("where is my head", "src", "foo", "bar  '") +
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123", "BAR"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath("where is my head", "src", "foo", "bar  '") + PathGeneratorUtilities.GetAbsolutePath("B", "FOO", "BAR")));
        }

        [Fact]
        public void PathsInMarkers()
        {
            var pt = GetPathTranslator();

            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"logging a path: [d", "src", "123", "bar]"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"logging a path: [b", "foo", "bar]")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"##vso[task.uploadsummary]d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"##vso[task.uploadsummary]b", "foo", "bar")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@" \\?\d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@" \\?\b", "foo", "bar")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"\\?\d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"\\?\b", "foo", "bar")));
            XAssert.AreEqual(
                PathGeneratorUtilities.GetAbsolutePath(@"\??\d", "src", "123", "bar"),
                pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"\??\b", "foo", "bar")));
        }

        [Fact]
        public void CheckForFalsePositives()
        {
            var pt = GetPathTranslator();

            // Don't match the patterns. Validate for off by one errors & false positives
            if (OperatingSystemHelper.IsUnixOS)
            {
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(null, @"\\?b", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(null, @"\\?b", "foo", "bar")));
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(null, @"comb", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(null, @"comb", "foo", "bar")));
            }
            else
            {
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(@"\\?b", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"\\?b", "foo", "bar")));
                XAssert.AreEqual(
                    PathGeneratorUtilities.GetAbsolutePath(@"comb", "foo", "bar"),
                    pt.Translate(PathGeneratorUtilities.GetAbsolutePath(@"comb", "foo", "bar")));
            }
        }

        private PathTranslator GetPathTranslator()
        {
            return new PathTranslator(
                PathGeneratorUtilities.GetAbsolutePath("b", "foo"),
                PathGeneratorUtilities.GetAbsolutePath("d", "src", "123")
            );
        }
    }
}
