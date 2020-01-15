// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     Full record of a memoization.
    /// </summary>
    public class Record
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Record" /> class.
        /// </summary>
        public Record(StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            StrongFingerprint = strongFingerprint;
            ContentHashListWithDeterminism = contentHashListWithDeterminism;
        }

        /// <summary>
        ///     Gets the key.
        /// </summary>
        public StrongFingerprint StrongFingerprint { get; }

        /// <summary>
        ///     Gets the value.
        /// </summary>
        public ContentHashListWithDeterminism ContentHashListWithDeterminism { get; }
    }
}
