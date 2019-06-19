// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Scheduler;
using BuildXL.Storage;
// using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for the <see cref="ContentFingerprint" /> and <see cref="DirectoryFingerprint" /> structs.
    /// </summary>
    public sealed class FingerprintUnitTests
    {
#if false
        [Fact]
        public void ContentFingerprintEquality()
        {
            TestFingerprintEquality(
                construct: h => new ContentFingerprint(h),
                eq: (a, b) => a == b,
                neq: (a, b) => a != b);
        }

        [Fact]
        public void DirectoryFingerprintEquality()
        {
            var fp1 = new DirectoryFingerprint(ContentHashingUtilities.CreateRandom());
            var fp2 = fp1;
            var fp3 = new DirectoryFingerprint(ContentHashingUtilities.CreateRandom());

            XAssert.IsTrue(fp1 == fp2);
            XAssert.IsFalse(fp1 != fp2);
            XAssert.IsTrue(fp2 != fp3);
            XAssert.IsFalse(fp2 == fp3);
        }

        private static void TestFingerprintEquality<TStruct>(
            Func<Fingerprint, TStruct> construct,
            Func<TStruct, TStruct, bool> eq,
            Func<TStruct, TStruct, bool> neq)
            where TStruct : struct, IEquatable<TStruct>
        {
            // SHA1 hash of "This is a sacrifice to the code coverage gods"
            Fingerprint baseValueHash;
            XAssert.IsTrue(Fingerprint.TryParse("64be6ae7b487e04701056542c4943e3cfe52280f", out baseValueHash));

            TStruct baseValue = construct(baseValueHash);

            // The first four bytes of the hash should be part of the hash code.
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: construct(baseValueHash),
                notEqualValues: Enumerable.Range(0, 4).Select(i => construct(TwiddleHashByte(baseValueHash, i))).ToArray(),
                eq: eq,
                neq: neq,
                skipHashCodeForNotEqualValues: false);

            // The remaining bytes do not impact the hash code, so we opt out of the hash check.
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: construct(baseValueHash),
                notEqualValues: Enumerable.Range(4, FingerprintUtilities.FingerprintLength - 4).Select(i => construct(TwiddleHashByte(baseValueHash, i))).ToArray(),
                eq: eq,
                neq: neq,
                skipHashCodeForNotEqualValues: true);
        }

        private static Fingerprint TwiddleHashByte(Fingerprint fingerprint, int index)
        {
            Contract.Requires(index >= 0 && index < FingerprintUtilities.FingerprintLength);

            var buffer = new byte[FingerprintUtilities.FingerprintLength];
            fingerprint.Serialize(buffer);
            buffer[index] = unchecked((byte) ~buffer[index]);

            return FingerprintUtilities.CreateFrom(buffer);
        }
#endif
    }
}
