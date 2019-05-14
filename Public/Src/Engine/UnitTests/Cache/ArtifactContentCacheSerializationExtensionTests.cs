// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine.Cache
{
    /// <summary>
    /// Tests for <see cref="ArtifactContentCacheSerializationExtensions"/>.
    /// </summary>
    public sealed class ArtifactContentCacheSerializationExtensionTests : XunitBuildXLTest
    {
        public ArtifactContentCacheSerializationExtensionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task RoundtripSimpleStructure()
        {
            var cache = new InMemoryArtifactContentCache();

            var originalEntry = new PipCacheDescriptorV2Metadata()
                                {
                                    Id = 123,
                                    NumberOfWarnings = 456,
                                    TraceInfo = "Good job."
                                };

            Possible<ContentHash> maybeStored =
                await cache.TrySerializeAndStoreContent(originalEntry);
            XAssert.IsTrue(maybeStored.Succeeded);

            var maybeDeserialized =
                await cache.TryLoadAndDeserializeContent<PipCacheDescriptorV2Metadata>(maybeStored.Result);
            XAssert.IsTrue(maybeDeserialized.Succeeded);

            PipCacheDescriptorV2Metadata roundtripped = maybeDeserialized.Result;
            XAssert.IsNotNull(roundtripped, "Expected content available");

            XAssert.AreEqual(originalEntry.Id, roundtripped.Id);
            XAssert.AreEqual(originalEntry.NumberOfWarnings, roundtripped.NumberOfWarnings);
            XAssert.AreEqual(originalEntry.TraceInfo, roundtripped.TraceInfo);
        }

        [Fact]
        public async Task DeserializationReturnsNullIfUnavailable()
        {
            var cache = new InMemoryArtifactContentCache();

            ContentHash imaginaryContent = ContentHashingUtilities.HashBytes(Encoding.UTF8.GetBytes("Imagination"));

            var maybeDeserialized =
                await cache.TryLoadAndDeserializeContent<PipCacheDescriptorV2Metadata>(imaginaryContent);
            XAssert.IsTrue(maybeDeserialized.Succeeded);
            XAssert.IsNull(maybeDeserialized.Result, "Should be a miss (cache empty)");
        }
    }
}
