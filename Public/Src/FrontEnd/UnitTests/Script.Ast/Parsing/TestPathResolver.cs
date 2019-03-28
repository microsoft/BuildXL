// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.DScript.Ast.Parsing
{
    /// <summary>
    /// Represents different kinds of parsed path.
    /// </summary>
    /// <remarks>
    /// Enumeration is helpful for creating factory methods for parametrized tests.
    /// </remarks>
    public enum ParsedPathKind
    {
        /// <nodoc />
        AbsolutePath,

        /// <nodoc />
        PackageRelative,

        /// <nodoc />
        FileRelative,
    }

    /// <summary>
    /// Set of test cases for <see cref="PathResolver"/> type.
    /// </summary>
    [Trait("Category", "Parsing")]
    public class TestPathResolver
    {
        private readonly PathTable m_pathTable;
        private readonly PathResolver m_pathResolver;

        public TestPathResolver()
        {
            m_pathTable = new PathTable();

            m_pathResolver = new PathResolver(m_pathTable);
        }

        [MemberData(nameof(ParsePathTestCases))]
        [Theory]
        public void TestPathParsing(string path, string expected, int? parentCount, ParsedPathKind expectedKind)
        {
            var parsedPath = m_pathResolver.ParseRegularPath(path);
            var expectedPath = CreateParsedPath(expected, parentCount, expectedKind);
            Assert.Equal(expectedPath, parsedPath);
        }

        public static IEnumerable<object[]> ParsePathTestCases()
        {
            yield return TestAbsolutePath(
                A("d", "path", "foo.dsc"),
                A("d", "path", "foo.dsc")
            );

            if (OperatingSystemHelper.IsUnixOS)
            {
                yield return TestAbsolutePath("/foo.dsc", "/foo.dsc");
                yield return TestAbsolutePath("/folder/foo.dsc", "/folder/foo.dsc");
            }
            else
            {
                yield return TestPackageRelative("/foo.dsc", "foo.dsc");
                yield return TestPackageRelative("/folder/foo.dsc", "folder/foo.dsc");
            }

            yield return TestFileRelative("../foo.dsc", "foo.dsc", 1);
            yield return TestFileRelative("../../../folder/foo.dsc", "folder/foo.dsc", 3);
            yield return TestFileRelative("./foo.dsc", "foo.dsc", 0);
            yield return TestFileRelative("foo.dsc", "foo.dsc", 0);
            yield return TestFileRelative("folder/foo.dsc", "folder/foo.dsc", 0);
            yield return TestFileRelative("folder/../foo.dsc", "folder/../foo.dsc", 0);
        }

        [MemberData(nameof(ParsePathFragmentTestCases))]
        [Theory]
        public void TestPathFragmentsParsing(string path, string expected)
        {
            Assert.True(PathResolver.IsPathFragment(path));

            var parsedPath = m_pathResolver.ParsePathFragment(path);
            var expectedPath = TryCreateRelativePath(expected);
            Assert.Equal(expectedPath, parsedPath);
        }

        public static IEnumerable<object[]> ParsePathFragmentTestCases()
        {
            // Rooted pathes are invalid as path fragments
            yield return new object[] {"#" +  A("d", "path", "foo.dsc"), null};

            // Package relative paths are invalid as path fragments
            yield return new object[] {"#/foo.dsc", null};

            // Empty path fragment is invalid
            yield return new object[] {"#", null};

            yield return new object[] {"#../foo.dsc", null};
            yield return new object[] {"#foo.dsc", "foo.dsc"};
            yield return new object[] {"#folder/foo.dsc", "folder/foo.dsc"};
        }

        [MemberData(nameof(ParsePathAtomTestCases))]
        public void TestPathAtomParsing(string path, string expected)
        {
            Assert.True(PathResolver.IsPathAtom(path));

            var expectedPathAtom = PathAtom.Create(m_pathTable.StringTable, expected);
            Assert.Equal(expectedPathAtom, m_pathResolver.ParsePathAtom(path));
        }

        public static IEnumerable<object[]> ParsePathAtomTestCases()
        {
            yield return new object[] {"@foo", "foo"};
            yield return new object[] {"@foo.ds", null};
        }

        private ParsedPath CreateParsedPath(string path, int? parentCount, ParsedPathKind kind)
        {
            switch (kind)
            {
                case ParsedPathKind.AbsolutePath:
                    return ParsedPath.AbsolutePath(CreateAbsolutePath(path), m_pathTable);
                case ParsedPathKind.PackageRelative:
                    return ParsedPath.PackageRelativePath(RelativePath.Create(m_pathTable.StringTable, path), m_pathTable);
                case ParsedPathKind.FileRelative:
                    return ParsedPath.FileRelativePath(RelativePath.Create(m_pathTable.StringTable, path), parentCount ?? 0, m_pathTable);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static object[] TestAbsolutePath(string path, string absolutePath)
        {
            return new object[] {path, absolutePath, null, ParsedPathKind.AbsolutePath};
        }

        private static object[] TestPackageRelative(string path, string relativePath)
        {
            return new object[] {path, relativePath, null, ParsedPathKind.PackageRelative};
        }

        private static object[] TestFileRelative(string path, string fileRelative, int parentCount)
        {
            return new object[] {path, fileRelative, parentCount, ParsedPathKind.FileRelative};
        }

        private AbsolutePath CreateAbsolutePath(string path)
        {
            return global::BuildXL.Utilities.AbsolutePath.Create(m_pathTable, path);
        }

        private RelativePath TryCreateRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return RelativePath.Invalid;
            }

            RelativePath result;
            RelativePath.TryCreate(m_pathTable.StringTable, path, out result);
            return result;
        }
    }
}
