// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Determinism value
    /// </summary>
    /// <remarks>
    /// Deterministic content can be achieved by (what we can see) two mechanisms.
    /// <list type="bullet">
    /// <item><description>
    /// The preferred and best way is if a tool is known to produce deterministic output.
    /// </description></item>
    /// <item><description>
    /// The lesser way is to have the cache help "recover" determinism in combination
    /// with the build system.
    /// </description></item>
    /// </list>
    /// <para>
    /// This struct encodes the fact that we have at least those two variations of
    /// determinism that may be described.  The cache can significantly optimize its
    /// operations using this information.
    /// </para>
    /// <para>
    /// For tool-based determinism, the cache can go the simplest paths and would be
    /// in a position to raise a serious error if a tool that claims to be deterministic
    /// produced a non-deterministc result.  (Albeit it would likely not notice unless
    /// there was a race)
    /// </para>
    /// <para>
    /// For tools that are not deterministic, which is most tools today, the cache can
    /// act as an agent to help bring a semblance of determinism.  There are two parts
    /// to this.  The first is that a cache hit will produce the same results as the
    /// cache itself will return the same data for the same key.  This is the simple case.
    /// In the more complex case, the cache can resolve non-deterministic tools with the
    /// help of the build system by having, at the point in time where content is trying
    /// to be added to the cache due to a prior cache miss, an atomic check to see if
    /// some other build process has already provided the same result and then telling
    /// the build system that there is a "better" result of that build step - basically
    /// a late cache hit - and that the build system should discard what it had produced
    /// and use what the cache has.
    /// </para>
    /// <para>
    /// In order to not require round trips to the centralized cache all the time and yet
    /// handle a local cache that may have had builds that happened while the centralized
    /// cache was off-line or otherwise not available, the cache can mark artifacts as
    /// ViaCache determinism and thus allow L1 cache hits without verification to L2.
    /// This gets us nearly the efficiency of fully deterministic tools when the L1 has
    /// content it knows has ViaCache determinism from the more authoratative source.
    /// (The L2 cache, or L3, or...)
    /// </para>
    /// <para>
    /// This is logically a tri-state condition.  However, given that there may be
    /// different "determinism domains" we are actually storing the Guid of the
    /// determinism domain for those items that have "Via Cache" determinism such that
    /// the aggregator can provide determinism domain switching in a reasonable manner.
    /// </para>
    /// </remarks>
    [EventData]
    public readonly struct CacheDeterminism : IEquatable<CacheDeterminism>
    {
        private static readonly Guid NoneGuid = default(Guid);
        private static readonly Guid ToolGuid = new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
        private static readonly Guid SinglePhaseNonDeterministicGuid = new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFE");

        private readonly Guid m_guid;
        private readonly DateTime m_expirationUtc;

        private CacheDeterminism(Guid cacheGuid, DateTime expirationUtc)
        {
            m_guid = cacheGuid;
            m_expirationUtc = expirationUtc;
        }

        /// <summary>
        /// Returns true if this is deterministic
        /// </summary>
        [EventIgnore]
        public bool IsDeterministic => (EffectiveGuid != NoneGuid) && (EffectiveGuid != SinglePhaseNonDeterministicGuid);

        /// <summary>
        /// Returns true if the determinism is due to a tool.
        /// </summary>
        [EventIgnore]
        public bool IsDeterministicTool => EffectiveGuid == ToolGuid;

        /// <summary>
        /// Returns true if this is the special single-phase, non-deterministic form.
        /// </summary>
        [EventIgnore]
        public bool IsSinglePhaseNonDeterministic => EffectiveGuid == SinglePhaseNonDeterministicGuid;

        /// <summary>
        /// Returns true if the determinism meets the None determinism.
        /// </summary>
        [EventIgnore]
        public bool IsNone => EffectiveGuid == NoneGuid;

        /// <summary>
        /// Returns the determinism domain GUID if it is currently valid; None otherwise.
        /// </summary>
        [EventIgnore]
        public Guid EffectiveGuid => DateTime.UtcNow < ExpirationUtc ? Guid : NoneGuid;

        /// <summary>
        /// The determinism domain GUID
        /// </summary>
        [EventField]
        public Guid Guid => m_guid;

        /// <summary>
        /// The time until which the Determinism is guaranteed to be valid.
        /// </summary>
        [EventField]
        public DateTime ExpirationUtc => m_expirationUtc;

        /// <inheritdoc />
        public bool Equals(CacheDeterminism other) => m_guid.Equals(other.m_guid) && m_expirationUtc.Equals(other.m_expirationUtc);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is CacheDeterminism && Equals((CacheDeterminism) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (m_guid.GetHashCode() * 397) ^ m_expirationUtc.GetHashCode();
            }
        }

        /// <nodoc />
        public static bool operator ==(CacheDeterminism left, CacheDeterminism right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(CacheDeterminism left, CacheDeterminism right) => !left.Equals(right);

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Globalization", "CA1305:CultureInfo.InvariantCulture")]
        public override string ToString()
        {
            var effectiveGuid = EffectiveGuid;
            if (effectiveGuid == NoneGuid)
            {
                return "non-deterministic";
            }

            if (effectiveGuid == ToolGuid)
            {
                return "tool-deterministic";
            }

            return ExpirationUtc == NeverExpires
                ? $"cache-deterministic: {Guid}, expiring never"
                : $"cache-deterministic: {Guid}, expiring at {ExpirationUtc}";
        }

        /// <summary>
        /// Determinism provided via a cache determinism domain
        /// </summary>
        /// <param name="cacheGuid">The Guid of the cache determinism domain</param>
        /// <param name="expirationUtc">An expiration time as to when this guid no longer is valid</param>
        /// <returns>A CacheDeterminism type based on this Guid</returns>
        /// <remarks>
        /// The determinism domain Guid is the Guid of the cache that is
        /// acting as the source of determinism.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1720", Justification = "Name is descriptive on purpose")]
        public static CacheDeterminism ViaCache(Guid cacheGuid, DateTime expirationUtc)
        {
            Contract.Requires(expirationUtc.Kind == DateTimeKind.Utc || expirationUtc == Expired || expirationUtc == NeverExpires);
            return new CacheDeterminism(cacheGuid, expirationUtc);
        }

        /// <summary>
        /// Already-expired is and must be the default value because
        /// default(CacheDeterminism).ExpirationUtc == default(DateTime) == DateTime.MinValue.
        /// </summary>
        public static readonly DateTime Expired = default(DateTime);

        /// <summary>
        /// This value is guaranteed to always be valid.
        /// </summary>
        public static readonly DateTime NeverExpires = DateTime.MaxValue;

        /// <summary>
        /// No determinism is and must be the default value.
        /// </summary>
        public static readonly CacheDeterminism None = default(CacheDeterminism);

        /// <summary>
        /// Determinism is known due to the tool (which is higher level determinism)
        /// </summary>
        public static readonly CacheDeterminism Tool = new CacheDeterminism(new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"), NeverExpires);

        /// <summary>
        /// For single-phase lookup behavior emulation, this determinism value
        /// signifies that there is no determinism via the cache and that a new value
        /// always overwrites an old value, but *only* if the old and new value are
        /// both of this determinism value.  It is an error convert from this
        /// determinism value to any other value or to convert from any other
        /// value to this determinism value.
        /// </summary>
        public static readonly CacheDeterminism SinglePhaseNonDeterministic = new CacheDeterminism(new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFE"), NeverExpires);

        /// <summary>
        /// Generate a new (random) cache GUID that is not one of the reserved
        /// special GUID values.
        /// </summary>
        /// <returns>A GUID that is appropriate to use for the cache</returns>
        public static Guid NewCacheGuid()
        {
            Guid result;
            do
            {
                result = Guid.NewGuid();
            }
            while (result.Equals(NoneGuid) || result.Equals(ToolGuid) || result.Equals(SinglePhaseNonDeterministicGuid));

            return result;
        }
    }
}
