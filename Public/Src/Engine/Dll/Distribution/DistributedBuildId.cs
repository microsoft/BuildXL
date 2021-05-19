// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Identifies a distributed build session
    /// </summary>
    /// <remarks>
    /// Activity id is unique per CB session. However, this is not enough as we may invoke more than one BuildXL 
    /// per machine per CB session. That's why, we also consider the environment in the build id.
    /// </remarks>
    public struct DistributedBuildId : IEquatable<DistributedBuildId>
    {

        /// <nodoc />
        public string RelatedActivityId { get; }

        /// <nodoc />
        public string Environment { get; }

        /// <nodoc />
        public DistributedBuildId(string sessionId, string environment)
        {
            RelatedActivityId = sessionId;
            Environment = environment;            
        }

        /// <nodoc />
        public override string ToString() => $"{RelatedActivityId}-{Environment}";

        #region Equals and hashcode
        /// <inheritdoc />
        public bool Equals(DistributedBuildId other) => RelatedActivityId == other.RelatedActivityId && Environment == other.Environment;

        /// <inheritdoc />
        public override bool Equals(object o) => o is DistributedBuildId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCodeHelper.Combine(RelatedActivityId?.GetHashCode() ?? 0, Environment?.GetHashCode() ?? 0);

        /// <inheritdoc />
        public static bool operator ==(DistributedBuildId lhs, DistributedBuildId rhs) => lhs.Equals(rhs);

        /// <inheritdoc />
        public static bool operator !=(DistributedBuildId lhs, DistributedBuildId rhs) => !(lhs == rhs);
        #endregion
    }
}
