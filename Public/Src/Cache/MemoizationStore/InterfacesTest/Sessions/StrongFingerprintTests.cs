// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public class StrongFingerprintTests
    {
        [Fact]
        public void EqualsObjectTrue()
        {
            var weakFingerprint = Fingerprint.Random();
            var selector = Selector.Random();
            var v1 = new StrongFingerprint(weakFingerprint, selector);
            var v2 = new StrongFingerprint(weakFingerprint, selector) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var weakFingerprint = Fingerprint.Random();
            var selector = Selector.Random();
            var v1 = new StrongFingerprint(weakFingerprint, selector);
            var v2 = new StrongFingerprint(weakFingerprint, selector);
            Assert.True(v1.Equals(v2));
            Assert.True(v1 == v2);
            Assert.False(v1 != v2);
        }

        [Fact]
        public void EqualsFalseSameWeakFingerprint()
        {
            var weakFingerprint = Fingerprint.Random();
            var v1 = new StrongFingerprint(weakFingerprint, Selector.Random());
            var v2 = new StrongFingerprint(weakFingerprint, Selector.Random());
            Assert.False(v1.Equals(v2));
            Assert.False(v1 == v2);
            Assert.True(v1 != v2);
        }

        [Fact]
        public void EqualsFalseSameSelector()
        {
            var selector = Selector.Random();
            var v1 = new StrongFingerprint(Fingerprint.Random(), selector);
            var v2 = new StrongFingerprint(Fingerprint.Random(), selector);
            Assert.False(v1.Equals(v2));
            Assert.False(v1 == v2);
            Assert.True(v1 != v2);
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var weakFingerprint = Fingerprint.Random();
            var selector = Selector.Random();
            var v1 = new StrongFingerprint(weakFingerprint, selector);
            var v2 = new StrongFingerprint(weakFingerprint, selector);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = StrongFingerprint.Random();
            var v2 = StrongFingerprint.Random();
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void SerializeRoundtrip()
        {
            var value = StrongFingerprint.Random();
            Utilities.TestSerializationRoundtrip(value, value.Serialize, StrongFingerprint.Deserialize);
        }

        [Fact]
        public void WeakFingerprintIsSerializedFirst()
        {
            var value = StrongFingerprint.Random();
            Utilities.TestSerializationRoundtrip(value.WeakFingerprint, value.Serialize, Fingerprint.Deserialize);
        }
    }
}
