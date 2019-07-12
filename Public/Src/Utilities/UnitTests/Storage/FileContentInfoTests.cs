// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Storage;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Storage
{
    public sealed class FileContentInfoTests : XunitBuildXLTest
    {
        public FileContentInfoTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void FileContentInfoEquality()
        {
            ContentHash hash1 = ContentHashingUtilities.CreateRandom();
            ContentHash hash2 = hash1;
            ContentHash hash3 = ContentHashingUtilities.CreateRandom();

            StructTester.TestEquality(
                baseValue: new FileContentInfo(hash1, 100),
                equalValue: new FileContentInfo(hash2, 100),
                notEqualValues: new[]
                                {
                                    new FileContentInfo(hash3, 100),
                                    new FileContentInfo(hash2, 200),
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right);
        }        

        [Fact]
        public void ValidateRenderParseLogic()
        {
            var original = new FileContentInfo(ContentHashingUtilities.CreateRandom(), 100);
            
            var parsed = FileContentInfo.Parse(original.Render());

            XAssert.IsTrue(original == parsed);            
        }

        [Fact]
        public void ValidateLengthSerializationRoundtrip()
        {
            var hash = ContentHashingUtilities.CreateRandom();
            int length = 100;

            FileContentInfo original = new FileContentInfo(hash, length);
            
            var deserialized = new FileContentInfo(hash, FileContentInfo.LengthAndExistence.Deserialize(original.SerializedLengthAndExistence));

            XAssert.IsTrue(original == deserialized);
        }

        [Fact]
        public void LengthAndExistenceRejectsIncorrectLength()
        {            
            XAssert.ThrowsAny(() => { var t = new FileContentInfo.LengthAndExistence(FileContentInfo.LengthAndExistence.MaxSupportedLength + 1, PathExistence.ExistsAsFile); });

            XAssert.ThrowsAny(() => { var t = new FileContentInfo.LengthAndExistence(-1, PathExistence.ExistsAsFile); });
        }

        [Fact]
        public void ExistenceAndHasKnownLengthBehavior()
        {
            // non-empty hash + non-zero length => exists as a file
            var fci = new FileContentInfo(ContentHashingUtilities.CreateRandom(), 100);
            XAssert.IsTrue(fci.HasKnownLength);
            XAssert.IsTrue(fci.Existence.HasValue && fci.Existence.Value == PathExistence.ExistsAsFile);

            // empty hash + zero length => exists as a file
            fci = new FileContentInfo(ContentHashingUtilities.EmptyHash, 0);
            XAssert.IsTrue(fci.HasKnownLength);
            XAssert.IsTrue(fci.Existence.HasValue && fci.Existence.Value == PathExistence.ExistsAsFile);

            // empty hash + non-zero length => undefined
            fci = new FileContentInfo(ContentHashingUtilities.EmptyHash, 1);
            XAssert.IsFalse(fci.HasKnownLength);
            XAssert.IsFalse(fci.Existence.HasValue);

            // if the existence was not explicitly set, it should not magically appear
            fci = FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.CreateRandom());
            XAssert.IsFalse(fci.HasKnownLength);
            XAssert.IsFalse(fci.Existence.HasValue);

            // we should see exactly the same value that was passed when the struct was created
            var existence = PathExistence.ExistsAsDirectory;
            fci = FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.CreateRandom(), existence);
            XAssert.IsFalse(fci.HasKnownLength);
            XAssert.IsTrue(fci.Existence.HasValue && fci.Existence.Value == existence);

            // if a special hash is used, the length is invalid
            var specialHash = ContentHashingUtilities.CreateSpecialValue(1);
            fci = new FileContentInfo(specialHash, 100);
            XAssert.IsFalse(fci.HasKnownLength);
            XAssert.IsFalse(fci.Existence.HasValue);

            fci = FileContentInfo.CreateWithUnknownLength(specialHash);
            XAssert.IsFalse(fci.HasKnownLength);
            XAssert.IsFalse(fci.Existence.HasValue);

            fci = FileContentInfo.CreateWithUnknownLength(specialHash, existence);
            XAssert.IsFalse(fci.HasKnownLength);
            XAssert.IsTrue(fci.Existence.HasValue && fci.Existence.Value == existence);
        }
    }
}
