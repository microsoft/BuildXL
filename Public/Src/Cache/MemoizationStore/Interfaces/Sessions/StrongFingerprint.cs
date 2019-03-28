// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     A fingerprint fully qualifying an execution.
    /// </summary>
    public readonly struct StrongFingerprint : IEquatable<StrongFingerprint>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StrongFingerprint" /> struct.
        /// </summary>
        public StrongFingerprint(Fingerprint weakFingerprint, Selector selector)
        {
            WeakFingerprint = weakFingerprint;
            Selector = selector;
        }

        /// <summary>
        ///     Gets weakFingerprint associated with this StrongFingerprint.
        /// </summary>
        public Fingerprint WeakFingerprint { get; }

        /// <summary>
        ///     Gets selector associated with this StrongFingerprint.
        /// </summary>
        public Selector Selector { get; }

        /// <inheritdoc />
        public bool Equals(StrongFingerprint other)
        {
            return WeakFingerprint == other.WeakFingerprint && Selector == other.Selector;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (WeakFingerprint, Selector).GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"WeakFingerprint=[{WeakFingerprint}],Selector=[{Selector}]";
        }

        /// <nodoc />
        public static bool operator ==(StrongFingerprint left, StrongFingerprint right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(StrongFingerprint left, StrongFingerprint right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static StrongFingerprint Random(
            int weakFingerprintLength = Fingerprint.MaxLength, HashType selectorHashType = HashType.SHA1)
        {
            return new StrongFingerprint(Fingerprint.Random(weakFingerprintLength), Selector.Random(selectorHashType));
        }
    }
}
