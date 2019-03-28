// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Hashing
{
    public class BlobIdentifierTests
    {
        private const string InvalidHashIdentifier = "54-CE-41-8A-2A-89-A7-4B-42-CC-39-63-01-67-79-5D-ED-5F-3B-16-A7-5F-F3-2A-01-B2-B0-1C-59-69-77-84";
        private const string HashIdentifier = "54CE418A2A89A74B42CC39630167795DED5F3B16A75FF32A01B2B01C59697784";

        [Fact]
        public void EqualsMethodReturnsTrueOnlyForIdentifiersWhoseValueMatch()
        {
            var identifier1 = BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier.ToUpperInvariant());
            var oppositeCaseIdentifier = BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier.ToLowerInvariant());
            var nonMatchingIdentifier = BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier.Replace('5', 'F'));
            Assert.True(identifier1.Equals(identifier1));
            Assert.True(identifier1.Equals(oppositeCaseIdentifier));
            Assert.False(identifier1.Equals(nonMatchingIdentifier));
        }

        [Fact]
        public void AlgorithmIdIsPreservedWhenPassedIn()
        {
            var identifier1 = BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier, 0xF);
            Assert.True(identifier1.AlgorithmId.Equals(0xF));
        }

        [Fact]
        public void InputHashIsPreservedButCaseIsNot()
        {
            var identifier1 = BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier.ToLowerInvariant(), 0xF);
            Assert.True(identifier1.ValueString.Equals(HashIdentifier.ToUpperInvariant() + "0F"));
        }

        [Fact]
        public void ToStringIncludesAlgorithmIdSuffixAndOriginalHash()
        {
            var identifier1 = BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier);
            Assert.NotEqual(HashIdentifier, identifier1.ToString());
            Assert.True(identifier1.ValueString.EndsWith("00", StringComparison.CurrentCultureIgnoreCase));
            Assert.True(identifier1.ValueString.StartsWith(HashIdentifier, StringComparison.CurrentCultureIgnoreCase));

            identifier1 = BlobIdentifier.CreateFromAlgorithmResult(HashIdentifier, 0x0F);
            Assert.True(identifier1.ValueString.EndsWith("0F", StringComparison.CurrentCultureIgnoreCase));
        }

        [Fact]
        public void BlobHashMatchesForContentStreamsThatAreIdentical()
        {
            string filePath = GetFilePath();

            BlobIdentifier identifier1;
            BlobIdentifier identifier2;

            using (Stream contentStream = GetFileStream(filePath))
            {
                identifier1 = contentStream.CalculateBlobIdentifier();
            }

            using (Stream contentStream = GetFileStream(filePath))
            {
                identifier2 = contentStream.CalculateBlobIdentifier();
            }

            if (identifier1 == null || identifier2 == null)
            {
                Assert.True(false, "inconclusive");
            }

            Assert.True(identifier1 != null && identifier1.Equals(identifier2));
        }

        [Fact]
        public void SupportsValidHexadecimalStringValuesWhenProvidedToTheConstructor()
        {
            Parallel.ForEach(
                new[]
                {
                    "A1B3F6AA",
                    "1234567890ABCDEF",
                    "01abcdef"
                },
                hexValue =>
                {
                    var identifier = BlobIdentifier.CreateFromAlgorithmResult(hexValue);
                    Assert.NotNull(identifier);
                });
        }

        [Fact]
        public void ThrowsWhenNonHexadecimalStringValuesAreProvidedToTheConstructor()
        {
            Parallel.ForEach(
                new[]
                {
                    "G1HHZ4",
                    "G1-HH-Z4",
                    "G1--HH---Z4",
                    "@$-A1",
                    InvalidHashIdentifier
                },
                nonHexValue =>
                {
                    try
                    {
                        var identifier = BlobIdentifier.CreateFromAlgorithmResult(nonHexValue);
                        Assert.Null(identifier);
                        Assert.False(true, "Expected ArgumentException was not thrown.");
                    }
                    catch (ArgumentException)
                    {
                    }
                });
        }

        [Fact]
        public void ToByteArrayReturnsEmptyBytesWhenEmptyString()
        {
            byte[] bytes = HexUtilities.HexToBytes(string.Empty);
            Assert.Equal(0, bytes.Length);
        }

        private static string GetFilePath()
        {
            return Directory.GetFiles(Directory.GetCurrentDirectory(), "*.dll").First();
        }

        private static Stream GetFileStream(string filePath)
        {
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
    }
}
