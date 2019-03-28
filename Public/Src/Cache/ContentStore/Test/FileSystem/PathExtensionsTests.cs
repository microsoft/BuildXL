// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.FileSystem
{
    public class PathExtensionsTests : TestBase
    {
        private static readonly AbsolutePath SourceRoot = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "some", "path", "there"));
        private static readonly AbsolutePath DestinationRoot = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("D", "some", "path", "here"));
        private readonly AbsolutePath _sourcePath = SourceRoot / "directory" / "file.txt";
        private readonly AbsolutePath _destinationPath = DestinationRoot / "directory" / "file.txt";

        public PathExtensionsTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void SwapRootWithExactMatchSucceeds()
        {
            AbsolutePath result = _sourcePath.SwapRoot(SourceRoot, DestinationRoot);
            result.Should().Be(_destinationPath);
        }

        [Fact]
        public void SwapRootWithIgnoreCaseMatchSucceeds()
        {
            var sourceRootUpper = new AbsolutePath(SourceRoot.Path.ToUpper(CultureInfo.InvariantCulture));

            var result = _sourcePath.SwapRoot(sourceRootUpper, DestinationRoot);
            result.Should().Be(_destinationPath);
        }

        [Fact]
        public void SwapRootNoMatchReturnsOriginalPath()
        {
            var result = _sourcePath.SwapRoot(new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "something")), new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C", "else")));
            result.Should().Be(_sourcePath);
        }

        [Fact]
        public void SwapRootWithPathEqualToRootGivesDestinationRoot()
        {
            var result = SourceRoot.SwapRoot(SourceRoot, DestinationRoot);
            result.Should().Be(DestinationRoot);
        }
    }
}
