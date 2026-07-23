// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.BlobDaemon;
using Xunit;

namespace Test.Tool.BlobDaemon
{
    public sealed class ContentTypeResolverTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ContentTypeResolverTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void ExactExtensionMatches()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string> { { ".txt", "text/plain" } });
            XAssert.AreEqual("text/plain", resolver.Resolve("foo.txt"));
        }

        [Fact]
        public void LongestSuffixWins()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string>
            {
                { ".gz", "application/gzip" },
                { ".tar.gz", "application/x-tar" },
            });
            XAssert.AreEqual("application/x-tar", resolver.Resolve("archive.tar.gz"));
            XAssert.AreEqual("application/gzip", resolver.Resolve("blob.gz"));
        }

        [Fact]
        public void CompoundFileFallsBackToShorterSuffix()
        {
            // Only the short suffix is mapped, so a compound-extension file matches it.
            var resolver = new ContentTypeResolver(new Dictionary<string, string> { { ".gz", "application/gzip" } });
            XAssert.AreEqual("application/gzip", resolver.Resolve("archive.tar.gz"));
        }

        [Fact]
        public void NoMatchingExtensionReturnsNull()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string> { { ".txt", "text/plain" } });
            XAssert.IsNull(resolver.Resolve("image.png"));
        }

        [Fact]
        public void FileWithoutExtensionReturnsNull()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string> { { ".txt", "text/plain" } });
            XAssert.IsNull(resolver.Resolve("README"));
        }

        [Fact]
        public void EmptyMapReturnsNull()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string>());
            XAssert.IsNull(resolver.Resolve("foo.txt"));
        }

        [Fact]
        public void ContentTypeWithParametersIsPreservedVerbatim()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string> { { ".txt", "text/plain; charset=utf-8" } });
            XAssert.AreEqual("text/plain; charset=utf-8", resolver.Resolve("foo.txt"));
        }

        [Fact]
        public void MatchesOnFileNameNotDirectory()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string> { { ".txt", "text/plain" } });
            var path = Path.Combine("some.dir", "sub", "foo.txt");
            XAssert.AreEqual("text/plain", resolver.Resolve(path));
        }

        [Fact]
        public void CaseSensitivityIsOsSpecific()
        {
            var resolver = new ContentTypeResolver(new Dictionary<string, string> { { ".txt", "text/plain" } });
            // On Unix the match is case-sensitive (no match); on Windows it is case-insensitive (match).
            var expected = OperatingSystemHelper.IsUnixOS ? null : "text/plain";
            XAssert.AreEqual(expected, resolver.Resolve("FOO.TXT"));
        }
    }
}
