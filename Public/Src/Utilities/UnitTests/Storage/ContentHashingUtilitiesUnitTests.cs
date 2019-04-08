// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Storage
{
    /// <summary>
    /// Tests for <see cref="ContentHashingUtilities" />
    /// </summary>
    public sealed class ContentHashingUtilitiesUnitTests : XunitBuildXLTest
    {
        public ContentHashingUtilitiesUnitTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void OrderIndependentCombine()
        {
            var rightHashBytes = ContentHashingUtilities.EmptyHash.ToHashByteArray();
            for (var i = 0; i < rightHashBytes.Length; i++)
            {
                rightHashBytes[i] = unchecked((byte)(~rightHashBytes[i] - 1));
            }

            ContentHash rightHash = new ContentHash(ContentHashingUtilities.EmptyHash.HashType, rightHashBytes);
            ContentHash leftHash = ContentHashingUtilities.EmptyHash;

            XAssert.AreEqual(
                ContentHashingUtilities.CombineOrderIndependent(leftHash, rightHash),
                ContentHashingUtilities.CombineOrderIndependent(rightHash, leftHash));

            XAssert.AreNotEqual(
                leftHash,
                ContentHashingUtilities.CombineOrderIndependent(leftHash, rightHash));

            XAssert.AreNotEqual(
                rightHash,
                ContentHashingUtilities.CombineOrderIndependent(leftHash, rightHash));
        }

        [Fact]
        public void CreateSpecialValueGivesCorrectHash()
        {
            var contentHash = ContentHashingUtilities.CreateSpecialValue(7);
            var hashBytes = contentHash.ToHashByteArray();

            for (var i = 1; i < ContentHashingUtilities.HashInfo.ByteLength; i++)
            {
                XAssert.AreEqual(0, hashBytes[i]);
            }

            XAssert.AreEqual(7, hashBytes[0]);
        }

        [Fact]
        public void CreateSpecialValueIsSpecial()
        {
            XAssert.IsTrue(ContentHashingUtilities.ZeroHash.IsSpecialValue());
            XAssert.IsTrue(ContentHashingUtilities.CreateSpecialValue(1).IsSpecialValue());
            XAssert.IsTrue(ContentHashingUtilities.CreateSpecialValue(2).IsSpecialValue());
            XAssert.IsTrue(ContentHashingUtilities.CreateSpecialValue(3).IsSpecialValue());

            // Technically, this could fail but if that ever happens its very strange for a random value to
            // actually end up being a special value
            var randomHash = ContentHashingUtilities.CreateRandom();
            var randomHashIsSpecialValue = randomHash.IsSpecialValue();
            XAssert.IsFalse(randomHashIsSpecialValue, "Random hash is a special value: '{0}'", randomHash.ToHex());
        }

        [Fact]
        public void CreateFingerprintFromWellDistributedHash()
        {
            var hash = new MurmurHash3(0x0102030405060608, 0x0910111213141516);
            var fingerprint = ContentHashingUtilities.CreateFrom(hash).ToHex();
            XAssert.AreEqual("080606050403020116151413121110090000000000000000000000000000000000", fingerprint);
        }
    }
}
