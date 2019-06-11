// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using RelativePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.RelativePath;
using static BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#pragma warning disable SA1402 // File may only contain a single class
#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Cache.ContentStore.InterfacesTest.FileSystem
{
    public static partial class Constants
    {
        public static readonly string ValidAbsoluteLocalPath = PathGeneratorUtilities.GetAbsolutePath("e", "dir");
        // Getting the path by constructing AbsolutePath to get the right prefix
        public static readonly string ValidAbsoluteLocalLongPath = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("e", "dir", new string('a', 200), new string('b', 200), new string('c', 200), "\\")).ToString();
        public static readonly string ValidAbsoluteLocalRootPath = PathGeneratorUtilities.GetAbsolutePath("e");
        public const string ValidUncPath = @"\\host\dir\subdir";
        public const string ValidUncRootPath = @"\\host\dir";
    }

    public class AbsolutePathConstructorTests
    {
        [Fact]
        public void ThrowsGivenNullPath()
        {
            Action a = () => Assert.Null(new AbsolutePath(null));
            Assert.Throws<ArgumentNullException>(a);
        }

        [Fact]
        public void ThrowsGivenEmptyPath()
        {
            Action a = () => Assert.Null(new AbsolutePath(string.Empty));
            Assert.Throws<ArgumentException>(a);
        }

        [Fact]
        public void SucceedsForAbsoluteLocalPath()
        {
            Assert.NotNull(new AbsolutePath(Constants.ValidAbsoluteLocalPath));
        }

        [Fact]
        // No UNC-like paths on Mac
        [Trait("Category", "WindowsOSOnly")] 
        public void SucceedsForUncPath()
        {
            Assert.NotNull(new AbsolutePath(Constants.ValidUncPath));
        }

        [Fact]
        public void ThrowsIfUncPathMissingTopLevelDirectory()
        {
            Action a = () => Assert.Null(new AbsolutePath(@"\\host"));
            ArgumentException e = Assert.Throws<ArgumentException>(a);
            Assert.Contains("UNC path is missing directory", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ThrowsIfNotAnAbsolutePath()
        {
            Action a = () => Assert.Null(new AbsolutePath(@"somerelpath"));
            ArgumentException e = Assert.Throws<ArgumentException>(a);
            Assert.Contains("is neither an absolute local or UNC path", e.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ThrowsIfInvalidDotDotSegment()
        {
            Action a = () => Assert.Null(new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "..", "file.txt")));
            ArgumentException e = Assert.Throws<ArgumentException>(a);
            Assert.Contains("invalid format", e.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class AbsolutePathIsLocalPropertyTests
    {
        [Fact]
        // No UNC-like paths on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void ReturnsFalseForUncPath()
        {
            Assert.False(new AbsolutePath(Constants.ValidUncPath).IsLocal);
        }

        [Fact]
        public void ReturnsTrueForLocalPath()
        {
            Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalPath).IsLocal);
        }

        [Fact]
        public void ReturnsTrueForLocalLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalLongPath).IsLocal);
            }
        }

        [Fact]
        public void ReturnsTrueForLocalLongPathConstructedByCombiningThePath()
        {
            if (LongPathsSupported)
            {
                var path = new AbsolutePath(Constants.ValidAbsoluteLocalLongPath) / new string('a', 200);
                Assert.True(path.IsLocal);

                if (OperatingSystemHelper.IsWindowsOS)
                {
                    // Windows specific check: the resulting long path should have a long path prefix.
                    Assert.StartsWith(FileSystemConstants.LongPathPrefix, path.ToString());
                }
            }
        }
    }

    public class LongPathTests
    {
        [Fact]
        [Trait("Category", "WindowsOSOnly")] // PathTooLongException is windows specific issue.
        public void NoErrorsIfLongPathsSupported()
        {
            if (LongPathsSupported)
            {
                // Parent property throws PathTooLongException, not a constructor.
                // So need to access it to make sure the exception is not thrown.
                var longPath = new AbsolutePath(Constants.ValidAbsoluteLocalLongPath);
                Assert.NotNull(longPath.Parent);

                Assert.StartsWith(FileSystemConstants.LongPathPrefix, longPath.ToString());
            }
        }
        
        [Fact]
        [Trait("Category", "WindowsOSOnly")] // PathTooLongException is windows specific issue.
        public void ShouldFailWithPathTooLongExceptionIfNotSupported()
        {
            if (!LongPathsSupported)
            {
                Func<AbsolutePath> a = () => new AbsolutePath(Constants.ValidAbsoluteLocalLongPath).Parent;
                Assert.Throws<PathTooLongException>(a);
            }
        }
    }

    public class AbsolutePathIsUncPropertyTests
    {
        [Fact]
        public void ReturnsFalseForLocalPath()
        {
            Assert.False(new AbsolutePath(Constants.ValidAbsoluteLocalPath).IsUnc);
        }

        [Fact]
        // No UNC-like paths on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void ReturnsTrueForUncPath()
        {
            Assert.True(new AbsolutePath(Constants.ValidUncPath).IsUnc);
        }
    }

    public class AbsolutePathIsRootPropertyTests
    {
        [Fact]
        // No UNC-like paths on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void ReturnsFalseForNonRootUnc()
        {
            Assert.False(new AbsolutePath(Constants.ValidUncPath).IsRoot);
        }

        [Fact]
        // No UNC-like paths on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void ReturnsTrueForRootUnc()
        {
            Assert.True(new AbsolutePath(Constants.ValidUncRootPath).IsRoot);
        }

        [Fact]
        public void ReturnsFalseForNonRootLocal()
        {
            Assert.False(new AbsolutePath(Constants.ValidAbsoluteLocalPath).IsRoot);
        }

        [Fact]
        public void ReturnsTrueForRootLocal()
        {
            Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath).IsRoot);
        }

        [Fact]
        public void ReturnsFalseForNonRoolLocalLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.False(new AbsolutePath(Constants.ValidAbsoluteLocalLongPath).IsRoot);
            }
        }
    }

    public class AbsolutePathPathPropertyTests
    {
        [Fact]
        [Trait("Category", "WindowsOSOnly")] // PathTooLongException is windows specific issue.
        public void PathTooLongExceptionContainsAPathName()
        {
            if (!LongPathsSupported)
            {
                string path = Constants.ValidAbsoluteLocalPath + new string('a', 300);

                var e = Assert.Throws<PathTooLongException>(() => new AbsolutePath(path).Parent);
                Assert.Contains("path", e.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void GivesCorrectValue()
        {
            Assert.Equal(Constants.ValidAbsoluteLocalPath, new AbsolutePath(Constants.ValidAbsoluteLocalPath).Path);
        }

        [Fact]
        public void GivesCorrectValueForLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.Equal(Constants.ValidAbsoluteLocalLongPath, new AbsolutePath(Constants.ValidAbsoluteLocalLongPath).Path);
            }
        }

        [Fact]
        public void ConvertsForwardSlash()
        {
            Assert.Equal(Constants.ValidAbsoluteLocalRootPath + @"some\dir\file.txt".Replace('\\', Path.DirectorySeparatorChar), new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"some/dir/file.txt").Path);
        }

        [Fact]
        public void ConvertsForwardSlashForLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.Equal(Constants.ValidAbsoluteLocalLongPath + @"some\dir\file.txt".Replace('\\', Path.DirectorySeparatorChar), new AbsolutePath(Constants.ValidAbsoluteLocalLongPath + @"some/dir/file.txt").Path);
            }
        }

        [Fact]
        public void ConvertsBackSlash()
        {
            Assert.Equal(Constants.ValidAbsoluteLocalRootPath + @"some\dir\file.txt".Replace('\\', Path.DirectorySeparatorChar), new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"some\dir\file.txt").Path);
        }

        [Fact]
        public void ConvertsBackSlashForLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.Equal(Constants.ValidAbsoluteLocalLongPath + @"some\dir\file.txt".Replace('\\', Path.DirectorySeparatorChar), new AbsolutePath(Constants.ValidAbsoluteLocalLongPath + @"some\dir\file.txt").Path);
            }
        }

        [Fact]
        public void NormalizesDotDotSegments()
        {
            Assert.Equal(Constants.ValidAbsoluteLocalRootPath + "file.txt", new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"some\dir\..\..\.\file.txt").Path);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void NormalizesDotDotSegmentsForLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.Equal(Constants.ValidAbsoluteLocalLongPath + "\\file.txt", new AbsolutePath(Constants.ValidAbsoluteLocalLongPath + @"\some\dir\..\..\.\file.txt").Path);
            }
        }

        [Fact]
        public void NormalizesExtraSeparators()
        {
            Assert.Equal(Constants.ValidAbsoluteLocalRootPath + @"some\dir\file.txt".Replace('\\', Path.DirectorySeparatorChar), new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"\some\\\dir//file.txt").Path);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public void NormalizesExtraSeparatorsForLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.Equal(Constants.ValidAbsoluteLocalLongPath + @"some\dir\file.txt".Replace('\\', Path.DirectorySeparatorChar), new AbsolutePath(Constants.ValidAbsoluteLocalLongPath + @"some\\\dir//file.txt").Path);
            }
        }
    }

    public class AbsolutePathParentPropertyTests
    {
        [Fact]
        public void NullParentGivenRootLocalPath()
        {
            ExpectNullForParentGivenRootPath(Constants.ValidAbsoluteLocalRootPath);
        }

        [Fact]
        // No UNC-like paths on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void NullParentGivenRootUncPath()
        {
            ExpectNullForParentGivenRootPath(Constants.ValidUncRootPath);
        }

        private static void ExpectNullForParentGivenRootPath(string path)
        {
            var fsPath = new AbsolutePath(path);
            AbsolutePath parent = fsPath.Parent;
            Assert.Null(parent);
        }

        [Fact]
        public void SucceedsGivenLocalPath()
        {
            Assert.Equal(
                Path.GetDirectoryName(Constants.ValidAbsoluteLocalPath), new AbsolutePath(Constants.ValidAbsoluteLocalPath).Parent.Path);
        }

        [Fact]
        public void SucceedsGivenLocalPathForLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.Equal(
                    Path.GetDirectoryName(Constants.ValidAbsoluteLocalLongPath), new AbsolutePath(Constants.ValidAbsoluteLocalLongPath).Parent.Path);
            }
        }

        [Fact]
        // No UNC-like paths on Mac
        [Trait("Category", "WindowsOSOnly")]
        public void SucceedsGivenUncPath()
        {
            Assert.Equal(Path.GetDirectoryName(Constants.ValidUncPath), new AbsolutePath(Constants.ValidUncPath).Parent.Path);
        }
    }

    public class AbsolutePathFileNamePropertyTests
    {
        [Fact]
        public void ReturnsEmptyForLocalRootPath()
        {
            Assert.Equal(string.Empty, new AbsolutePath(Constants.ValidAbsoluteLocalRootPath).FileName);
        }
    }

    public class AbsolutePathEquatableEqualsMethodTests
    {
        [Fact]
        public void ReturnsTrue()
        {
            Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalPath).Equals(new AbsolutePath(Constants.ValidAbsoluteLocalPath)));
        }

        [Fact]
        public void ReturnsFalse()
        {
            Assert.False(new AbsolutePath(Constants.ValidAbsoluteLocalPath).Equals(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath)));
        }

        [Fact]
        // Only Windows is insensitive to path casing
        [Trait("Category", "WindowsOSOnly")]
        public void ReturnsTrueForSameButDifferentCase()
        {
            Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + "dir").Equals(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"DIR")));
        }

        [Fact]
        // Only Windows is insensitive to path casing
        [Trait("Category", "WindowsOSOnly")]
        public void ReturnsTrueForSameButDifferentCaseForLongPath()
        {
            if (LongPathsSupported)
            {
                Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalLongPath + "dir").Equals(new AbsolutePath(Constants.ValidAbsoluteLocalLongPath + @"DIR")));
            }
        }

        [Fact]
        public void ReturnsFalseIfOtherIsNull()
        {
            const PathBase nullPath = null;
            Assert.False(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"dir").Equals(nullPath));
        }
    }

    public class AbsolutePathObjectEqualsMethodOverrideTests
    {
        [Fact]
        public void ReturnsTrue()
        {
            Assert.True(
                new AbsolutePath(Constants.ValidAbsoluteLocalPath).Equals(new AbsolutePath(Constants.ValidAbsoluteLocalPath) as object));
        }

        [Fact]
        public void ReturnsFalse()
        {
            Assert.False(
                new AbsolutePath(Constants.ValidAbsoluteLocalPath).Equals(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath) as object));
        }
    }

    public class AbsolutePathToStringMethodOverrideTests
    {
        [Fact]
        public void IsCorrect()
        {
            Assert.Equal(Constants.ValidAbsoluteLocalPath, new AbsolutePath(Constants.ValidAbsoluteLocalPath).ToString());
        }
    }

    public class AbsolutePathEqualsOperatorTests
    {
        [Fact]
        public void ReturnsTrue()
        {
            Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalPath).Parent == new AbsolutePath(Constants.ValidAbsoluteLocalRootPath));
        }

        [Fact]
        public void ReturnsFalse()
        {
            Assert.False(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath) == new AbsolutePath(Constants.ValidAbsoluteLocalPath));
        }
    }

    public class AbsolutePathNotEqualsOperatorTests
    {
        [Fact]
        public void ReturnsTrue()
        {
            Assert.True(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath) != new AbsolutePath(Constants.ValidAbsoluteLocalPath));
        }

        [Fact]
        public void ReturnsFalse()
        {
            Assert.False(new AbsolutePath(Constants.ValidAbsoluteLocalPath).Parent != new AbsolutePath(Constants.ValidAbsoluteLocalRootPath));
        }
    }

    public class AbsolutePathAppendOperatorWithStringTests
    {
        [Fact]
        public void GivenEmptyIsNop()
        {
            Assert.Equal(
                new AbsolutePath(Constants.ValidAbsoluteLocalPath), new AbsolutePath(Constants.ValidAbsoluteLocalPath) / string.Empty);
        }

        [Fact]
        public void Succeeds()
        {
            
            Assert.Equal(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"dir\folder"), new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"dir") / "folder");
        }

        private const string IllegalPathFragment = "|";

#if PLATFORM_OSX
        [Fact]
        public void WindowsIllegalCharacterWorksOnMac()
        {
            var path = new AbsolutePath(Constants.ValidAbsoluteLocalPath) / IllegalPathFragment;
            Assert.NotNull(path);
        }
#endif

        [Fact]
        // DotNetCore does not validate the absence of illegal path characters in paths
        [Trait("Category", "SkipDotNetCore")]
        public void InvalidPathCharacterPrintedInExceptionMessage()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => new AbsolutePath(Constants.ValidAbsoluteLocalPath) / IllegalPathFragment);
            Assert.Contains(IllegalPathFragment, exception.Message);
        }

        [Fact]
        // DotNetCore does not validate the absence of illegal path characters in paths
        [Trait("Category", "SkipDotNetCore")]
        public void InvalidPathCharacterPrintedInExceptionMessage_ConstructedFromRelativePath()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => new AbsolutePath(Constants.ValidAbsoluteLocalPath) / new RelativePath(IllegalPathFragment));
            Assert.Contains(IllegalPathFragment, exception.Message);
        }

        [Fact]
        // DotNetCore does not validate the absence of illegal path characters in paths
        [Trait("Category", "SkipDotNetCore")]
        public void InvalidPathCharacterPrintedInExceptionMessage_ParentProperty()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => new AbsolutePath(Constants.ValidAbsoluteLocalPath + IllegalPathFragment).Parent);
            Assert.Contains(IllegalPathFragment, exception.Message);
        }
    }

    public class AbsolutePathAppendOperatorWithRelativePathTests
    {
        [Fact]
        public void Succeeds()
        {
            Assert.True((new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"dir") / new RelativePath("folder")).Equals(new AbsolutePath(Constants.ValidAbsoluteLocalRootPath + @"dir\folder")));
        }
    }
}

#pragma warning restore SA1649 // File name must match first type name
#pragma warning restore SA1402 // File may only contain a single class
