// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Cache.ContentStore.InterfacesTest.FileSystem
{
    public static partial class Constants
    {
        public const string ValidLevelOneRelativePath = @"someroot";
        public static readonly string ValidLevelTwoRelativePath = PathGeneratorUtilities.GetRelativePath("someroot", "somedir");
    }

    public class RelativePathConstructorTests
    {
        [Fact]
        public void ThrowsGivenNullPath()
        {
            Action a = () => Assert.Null(new RelativePath(null));
            Assert.Throws<ArgumentNullException>(a);
        }

        [Fact]
        public void SucceedsGivenEmptyPath()
        {
            Assert.NotNull(new RelativePath(string.Empty));
        }

        [Fact]
        public void ThrowsForAbsoluteLocalPath()
        {
            Action a = () => Assert.NotNull(new RelativePath(Constants.ValidAbsoluteLocalPath));
            ArgumentException e = Assert.Throws<ArgumentException>(a);
            Assert.Contains("relative paths cannot be local absolute or UNC paths", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ThrowsForUncPath()
        {
            Action a = () => Assert.Null(new RelativePath(Constants.ValidUncPath));
            ArgumentException e = Assert.Throws<ArgumentException>(a);
            Assert.Contains("relative paths cannot be local absolute or UNC paths", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SucceedsForValidRelativePath()
        {
            Assert.NotNull(new RelativePath(Constants.ValidLevelTwoRelativePath));
        }

        [Fact]
        public void AcceptsLeadingDotDotSegment()
        {
            Assert.NotNull(new RelativePath(@"..\file.txt"));
        }
    }

    public class RelativePathPathPropertyTests
    {
        [Fact]
        public void GivesCorrectValue()
        {
            Assert.Equal(Constants.ValidLevelTwoRelativePath, new RelativePath(Constants.ValidLevelTwoRelativePath).Path);
        }

        [Fact]
        public void ConvertsForwardSlash()
        {
            Assert.Equal(PathGeneratorUtilities.GetRelativePath("some", "dir", "file.txt"), new RelativePath("some/dir/file.txt").Path);
        }

        [Fact]
        public void ConvertsBackSlash()
        {
            Assert.Equal(PathGeneratorUtilities.GetRelativePath("some", "dir", "file.txt"), new RelativePath(@"some\dir\file.txt").Path);
        }

        [Fact]
        public void NormalizesDotDotSegments()
        {
            Assert.Equal("file.txt", new RelativePath(@"some\dir\..\..\.\file.txt").Path);
        }

        [Fact]
        public void NormalizesExtraSeparators()
        {
            Assert.Equal(PathGeneratorUtilities.GetRelativePath("some", "dir", "file.txt"), new RelativePath(@"some\\\dir//file.txt").Path);
        }
    }

    public class RelativePathIllegalCharacterInPathExceptionTests
    {
        private const string IllegalPathFragment = "|";

        [Fact]
        // DotNetCore does not validate the absence of illegal path characters in paths
        [Trait("Category", "SkipDotNetCore")]
        public void InvalidPathCharacterPrintedInExceptionMessage()
        {
            var relativePath = new RelativePath(PathGeneratorUtilities.GetRelativePath("a"));
            var exception = Assert.Throws<ArgumentException>(() =>  relativePath / IllegalPathFragment);

            Assert.Contains(IllegalPathFragment, exception.Message);

            exception = Assert.Throws<ArgumentException>(() => new RelativePath(IllegalPathFragment));

            Assert.Contains(IllegalPathFragment, exception.Message);
        }
    }

    public class RelativePathParentPropertyTests
    {
        [Fact]
        public void OneLevelDirectoryShouldReturnRoot()
        {
            Assert.Equal(RelativePath.RootPath, new RelativePath(Constants.ValidLevelOneRelativePath).Parent);
        }

        [Fact]
        public void RootShouldReturnNull()
        {
            RelativePath parent = RelativePath.RootPath.Parent;
            Assert.Null(parent);
        }

        [Fact]
        public void SucceedsGivenValidPath()
        {
            Assert.Equal(Constants.ValidLevelOneRelativePath, new RelativePath(Constants.ValidLevelTwoRelativePath).Parent.Path);
        }
    }

    public class RelativePathGetParentMethodTests
    {
        [Fact]
        public void SucceedsGivenValidPath()
        {
            Assert.Equal(
                Constants.ValidLevelOneRelativePath,
                new RelativePath(Constants.ValidLevelTwoRelativePath).GetParentPath<RelativePath>().Path);
        }
    }

    public class RelativePathFileNamePropertyTests
    {
        [Fact]
        public void ReturnsItselfForRootPath()
        {
            Assert.Equal(RelativePath.RootPath.Path, RelativePath.RootPath.FileName);
        }

        [Fact]
        public void ReturnsItselfForLevelOnePath()
        {
            Assert.Equal(Constants.ValidLevelOneRelativePath, new RelativePath(Constants.ValidLevelOneRelativePath).FileName);
        }

        [Fact]
        public void ReturnsCorrectNameForLevelTwoPath()
        {
            Assert.Equal("somedir", new RelativePath(Constants.ValidLevelTwoRelativePath).FileName);
        }
    }

    public class RelativePathEquatableEqualsMethodTests
    {
        [Fact]
        public void ReturnsTrueWhenSame()
        {
            Assert.True(new RelativePath(Constants.ValidLevelTwoRelativePath).Equals(new RelativePath(Constants.ValidLevelTwoRelativePath)));
        }

        [Fact]
        public void ReturnsFalseWhenDifferent()
        {
            Assert.False(new RelativePath(Constants.ValidLevelTwoRelativePath).Equals(new RelativePath(Constants.ValidLevelOneRelativePath)));
        }

        [Fact]
        public void ReturnsTrueForSameButDifferentCase()
        {
            Assert.True(new RelativePath(Constants.ValidLevelOneRelativePath).Equals(
                new RelativePath(Constants.ValidLevelOneRelativePath.ToUpper(CultureInfo.CurrentCulture))));
        }

        [Fact]
        public void ReturnsFalseIfOtherIsNull()
        {
            const PathBase nullPath = null;
            Assert.False(new RelativePath(Constants.ValidLevelTwoRelativePath).Equals(nullPath));
        }
    }

    public class RelativePathObjectEqualsMethodOverrideTests
    {
        [Fact]
        public void ReturnsTrueWhenSame()
        {
            Assert.True(
                new RelativePath(Constants.ValidLevelTwoRelativePath).Equals(new RelativePath(Constants.ValidLevelTwoRelativePath) as object));
        }

        [Fact]
        public void ReturnsFalseWhenDifferent()
        {
            Assert.False(
                new RelativePath(Constants.ValidLevelTwoRelativePath).Equals(new RelativePath(Constants.ValidLevelOneRelativePath) as object));
        }
    }

    public class RelativePathToStringMethodOverrideTests
    {
        [Fact]
        public void IsCorrect()
        {
            Assert.Equal(Constants.ValidLevelTwoRelativePath, new RelativePath(Constants.ValidLevelTwoRelativePath).ToString());
        }
    }

    public class RelativePathEqualsOperatorTests
    {
        [Fact]
        public void ReturnsTrue()
        {
            Assert.True(new RelativePath(Constants.ValidLevelTwoRelativePath).Parent ==
                        new RelativePath(Constants.ValidLevelOneRelativePath));
        }

        [Fact]
        public void ReturnsFalse()
        {
            Assert.False(new RelativePath(Constants.ValidLevelTwoRelativePath) == new RelativePath(Constants.ValidLevelOneRelativePath));
        }
    }

    public class RelativePathNotEqualsOperatorTests
    {
        [Fact]
        public void ReturnsTrue()
        {
            Assert.True(new RelativePath(Constants.ValidLevelOneRelativePath) != new RelativePath(Constants.ValidLevelTwoRelativePath));
        }

        [Fact]
        public void ReturnsFalse()
        {
            Assert.False(new RelativePath(Constants.ValidLevelTwoRelativePath).Parent !=
                         new RelativePath(Constants.ValidLevelOneRelativePath));
        }
    }

    public class RelativePathAppendOperatorWithStringsTests
    {
        [Fact]
        public void GivenEmptyIsNop()
        {
            Assert.True((new RelativePath(Constants.ValidLevelTwoRelativePath) / string.Empty).Equals(
                new RelativePath(Constants.ValidLevelTwoRelativePath)));
        }

        [Fact]
        public void Succeeds()
        {
            Assert.True((new RelativePath(Constants.ValidLevelOneRelativePath) / "somedir").Equals(new RelativePath(Constants.ValidLevelTwoRelativePath)));
        }
    }

    public class RelativePathAppendOperatorWithRelativePathTests
    {
        [Fact]
        public void GivenEmptyIsNop()
        {
            Assert.True((new RelativePath(Constants.ValidLevelOneRelativePath) / new RelativePath(string.Empty)).Equals(
                new RelativePath(Constants.ValidLevelOneRelativePath)));
        }

        [Fact]
        public void EmptyConcatenateWithValid()
        {
            Assert.True((new RelativePath(string.Empty) / new RelativePath(Constants.ValidLevelOneRelativePath)).Equals(
                new RelativePath(Constants.ValidLevelOneRelativePath)));
        }

        [Fact]
        public void Succeeds()
        {
            Assert.True((new RelativePath(Constants.ValidLevelOneRelativePath) / new RelativePath("somedir")).Equals(
                new RelativePath(Constants.ValidLevelTwoRelativePath)));
        }
    }

    public class RelativePathConcatenateWithPathMethodTests
    {
        [Fact]
        public void Succeeds()
        {
            string expected = PathGeneratorUtilities.GetRelativePath(Constants.ValidLevelOneRelativePath, "segment");
            Assert.Equal(
                expected,
                new RelativePath(Constants.ValidLevelOneRelativePath).ConcatenateWith<RelativePath>(new RelativePath("segment")).Path);
        }
    }

    public class RelativePathConcatenateWithStringMethodTests
    {
        [Fact]
        public void Succeeds()
        {
            string expected = PathGeneratorUtilities.GetRelativePath(Constants.ValidLevelOneRelativePath, "segment");
            Assert.Equal(expected, new RelativePath(Constants.ValidLevelOneRelativePath).ConcatenateWith<RelativePath>("segment").Path);
        }
    }
}

#pragma warning restore SA1649 // File name must match first type name
#pragma warning restore SA1403 // File may only contain a single namespace
