// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using StructUtilities = BuildXL.Cache.ContentStore.Interfaces.Utils.StructUtilities;

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

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        /// <remarks>
        ///     The included <see cref="Fingerprint"/> needs to always come first in the serialization order. This is
        ///     needed to be able to do prefix searches by weak fingerprint in key value stores.
        /// </remarks>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            WeakFingerprint.Serialize(writer);
            Selector.Serialize(writer);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StrongFingerprint" /> struct from its binary
        ///     representation.
        /// </summary>
        public static StrongFingerprint Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var weakFingerprint = Fingerprint.Deserialize(reader);
            var selector = Selector.Deserialize(reader);
            return new StrongFingerprint(weakFingerprint, selector);
        }

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
