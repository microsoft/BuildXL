// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class GetBulkLocationsResultTests : ResultTests<GetBulkLocationsResult>
    {
        private static readonly string Path1 = PathGeneratorUtilities.GetAbsolutePath("c", "path1");
        private static readonly string Path2 = PathGeneratorUtilities.GetAbsolutePath("c", "path2");
        private static readonly string Path3 = PathGeneratorUtilities.GetAbsolutePath("c", "path3");
        private static readonly string Path4 = PathGeneratorUtilities.GetAbsolutePath("c", "path4");
        private static readonly string Path5 = PathGeneratorUtilities.GetAbsolutePath("c", "path5");

        private static readonly IReadOnlyList<ContentHashWithSizeAndLocations> Locations1 = new List<ContentHashWithSizeAndLocations>
        {
            FromHashAndLocations(ContentHash.Random(), new AbsolutePath(Path1))
        };

        private static readonly IReadOnlyList<ContentHashWithSizeAndLocations> Locations2 = new List<ContentHashWithSizeAndLocations>
        {
            FromHashAndLocations(ContentHash.Random(), new AbsolutePath(Path2))
        };

        protected override GetBulkLocationsResult CreateFrom(Exception exception)
        {
            return new GetBulkLocationsResult(exception);
        }

        protected override GetBulkLocationsResult CreateFrom(string errorMessage)
        {
            return new GetBulkLocationsResult(errorMessage);
        }

        protected override GetBulkLocationsResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new GetBulkLocationsResult(errorMessage, diagnostics);
        }


        private static ContentHashWithSizeAndLocations FromHashAndLocations(ContentHash hash, params AbsolutePath[] paths)
            => new ContentHashWithSizeAndLocations(hash, 0, paths.Select(p => new MachineLocation(p.Path)).ToList());

        private static GetBulkLocationsResult ToResult(params ContentHashWithSizeAndLocations[] contentHashes)
            => new GetBulkLocationsResult(contentHashes);

        private static GetBulkLocationsResult ToResult(GetBulkOrigin origin, params ContentHashWithSizeAndLocations[] contentHashes)
            => new GetBulkLocationsResult(contentHashes, origin);

        [Fact]
        public void SubtractFromItselfShouldNotChangeOrigin()
        {
            var hash1 = ContentHash.Random();
            var hash2 = ContentHash.Random();
            var locations1 = FromHashAndLocations(hash1, new AbsolutePath(Path1));
            var locations2 = FromHashAndLocations(hash2, new AbsolutePath(Path2));

            var result1 = ToResult(GetBulkOrigin.Local, locations1, locations2);
            var result2 = result1.Subtract(result1);

            Assert.Equal(result1.Origin, result2.Origin);
        }

        [Fact]
        public void TestMergeAndSubtract()
        {
            var hash1 = ContentHash.Random();
            var hash2 = ContentHash.Random();
            var locations1 = FromHashAndLocations(hash1, new AbsolutePath(Path1));
            var locations2 = FromHashAndLocations(hash2, new AbsolutePath(Path2));

            var result1 = ToResult(locations1, locations2);

            var locations3 = FromHashAndLocations(hash1, new AbsolutePath(Path1), new AbsolutePath(Path3));
            var locations4 = FromHashAndLocations(hash2, new AbsolutePath(Path4), new AbsolutePath(Path5));

            var result2 = ToResult(locations3, locations4);
            var mergedLocations = result1.Merge(result2);

            Assert.Equal(hash1, mergedLocations.ContentHashesInfo[0].ContentHash);
            Assert.Equal(hash2, mergedLocations.ContentHashesInfo[1].ContentHash);

            // Merged locations should only have unique locations
            Assert.Equal(2, mergedLocations.ContentHashesInfo[0].Locations.Count);
            Assert.Equal(3, mergedLocations.ContentHashesInfo[1].Locations.Count);

            GetBulkLocationsResult subtractedLocation = mergedLocations.Subtract(result1);
            Assert.Equal(1, subtractedLocation.ContentHashesInfo[0].Locations.Count);
            Assert.Equal(2, subtractedLocation.ContentHashesInfo[1].Locations.Count);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.False(new GetBulkLocationsResult(other, "message").Succeeded);
        }

        [Fact]
        public void SuccessPropertyTrue()
        {
            Assert.True(new GetBulkLocationsResult(Locations1).Succeeded);
        }

        [Fact]
        public void SuccessPropertyFalseByErrorMessage()
        {
            Assert.False(CreateFrom("error").Succeeded);
        }

        [Fact]
        public void SuccessPropertyFalseByException()
        {
            Assert.False(CreateFrom(new InvalidOperationException()).Succeeded);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var o1 = new GetBulkLocationsResult(Locations1);
            var o2 = new GetBulkLocationsResult(Locations1) as object;
            Assert.True(o1.Equals(o2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var o1 = new GetBulkLocationsResult(Locations1);
            var o2 = new object();
            Assert.False(o1.Equals(o2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var o1 = new GetBulkLocationsResult(Locations1);
            var o2 = new GetBulkLocationsResult(Locations1);
            Assert.True(o1.Equals(o2));
        }

        [Fact]
        public void EqualsFalseSuccessMismatch()
        {
            var o1 = new GetBulkLocationsResult(Locations1);
            var o2 = new GetBulkLocationsResult((IReadOnlyList<ContentHashWithSizeAndLocations>)null);
            Assert.False(o1.Equals(o2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var o1 = new GetBulkLocationsResult("error1");
            var o2 = new GetBulkLocationsResult("error2");
            Assert.False(o1.Equals(o2));
        }

        [Fact]
        public void EqualsFalseLocationsMismatch()
        {
            var o1 = new GetBulkLocationsResult(Locations1);
            var o2 = new GetBulkLocationsResult(Locations2);
            Assert.False(o1.Equals(o2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var o1 = new GetBulkLocationsResult(Locations1);
            var o2 = new GetBulkLocationsResult(Locations1);
            Assert.Equal(o1.GetHashCode(), o2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var o1 = new GetBulkLocationsResult(Locations1);
            var o2 = new GetBulkLocationsResult("error");
            Assert.NotEqual(o1.GetHashCode(), o2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            var o = new GetBulkLocationsResult("something");
            Assert.Contains("something", o.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            var o = new GetBulkLocationsResult(Locations1);
            Assert.Contains("Success", o.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
