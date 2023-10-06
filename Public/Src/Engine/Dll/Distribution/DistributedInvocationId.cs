// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Identifies a distributed build session
    /// </summary>
    /// <remarks>
    /// Activity id is unique per CB session. However, this is not enough as we may invoke more than one BuildXL 
    /// per machine per CB session. That's why, we also consider the environment in the invocation id.
    /// 
    /// Because workers run in independent jobs on ADO distributed builds, we include the engine version
    /// to verify that we are running the same BuildXL version in all build agents.
    /// </remarks>
    public struct DistributedInvocationId : IEquatable<DistributedInvocationId>
    {

        /// <nodoc />
        public string RelatedActivityId { get; }

        /// <nodoc />
        public string Environment { get; }

        /// <nodoc />
        public string EngineVersion { get;  }

        /// <nodoc />
        public DistributedInvocationId(string sessionId, string environment, string engineVersion)
        {
            RelatedActivityId = sessionId;
            Environment = environment;    
            EngineVersion = engineVersion;
        }

        /// <nodoc />
        public override string ToString() => $"{RelatedActivityId}-{Environment}-{EngineVersion}";

        #region Equals and hashcode
        /// <inheritdoc />
        public bool Equals(DistributedInvocationId other) => RelatedActivityId == other.RelatedActivityId && Environment == other.Environment && EngineVersion == other.EngineVersion;

        /// <inheritdoc />
        public override bool Equals(object o) => o is DistributedInvocationId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCodeHelper.Combine(RelatedActivityId?.GetHashCode() ?? 0, Environment?.GetHashCode() ?? 0, EngineVersion?.GetHashCode() ?? 0);

        /// <inheritdoc />
        public static bool operator ==(DistributedInvocationId lhs, DistributedInvocationId rhs) => lhs.Equals(rhs);

        /// <inheritdoc />
        public static bool operator !=(DistributedInvocationId lhs, DistributedInvocationId rhs) => !(lhs == rhs);
        #endregion
    }
}
