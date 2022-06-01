// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Hints for cache operations. These hints are not necessarily enforced,
    ///     but may be used to achieve better throughput.
    /// </summary>
    public struct OperationHints
    {
        /// When adding fields to this struct consider that the default value is used implicitly
        /// in most codepaths

        /// <inheridoc cref="UrgencyHint" />
        public UrgencyHint Urgency { get; init; }

        /// <summary>
        /// Try to avoid remote requests associated with this operation,
        /// as long as correctness is not compromised.
        /// </summary>
        public bool AvoidRemote { get; init; }

        /// <nodoc />
        public static explicit operator OperationHints(UrgencyHint urgency) => new() { Urgency = urgency };

        /// <nodoc />
        public static implicit operator UrgencyHint(OperationHints hints) => hints.Urgency;
    }
}
