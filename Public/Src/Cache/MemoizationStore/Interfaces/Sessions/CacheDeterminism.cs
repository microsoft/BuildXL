// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Determinism value
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Copied from BuildXL in order to support the Determinism concept
    ///         while ICache lives in the BuildXL repo.
    ///         In the meantime, the important pieces to keep in sync are:
    ///         - The special "Tool" determinism value
    ///         - The special "None" determinism value
    ///         - The "ShouldUpgrade" logic, added here (not from BuildXL)
    ///         to keep it standard across implementations.
    ///     </para>
    ///     Deterministic content can be achieved by (what we can see) two mechanisms.
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 The preferred and best way is if a tool is known to produce deterministic output.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 The lesser way is to have the cache help "recover" determinism in combination
    ///                 with the build system.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         This struct encodes the fact that we have at least those two variations of
    ///         determinism that may be described.  The cache can significantly optimize its
    ///         operations using this information.
    ///     </para>
    ///     <para>
    ///         For tool-based determinism, the cache can go the simplest paths and would be
    ///         in a position to raise a serious error if a tool that claims to be deterministic
    ///         produced a non-deterministic result.  (Albeit it would likely not notice unless
    ///         there was a race)
    ///     </para>
    ///     <para>
    ///         For tools that are not deterministic, which is most tools today, the cache can
    ///         act as an agent to help bring a semblance of determinism.  There are two parts
    ///         to this.  The first is that a cache hit will produce the same results as the
    ///         cache itself will return the same data for the same key.  This is the simple case.
    ///         In the more complex case, the cache can resolve non-deterministic tools with the
    ///         help of the build system by having, at the point in time where content is trying
    ///         to be added to the cache due to a prior cache miss, an atomic check to see if
    ///         some other build process has already provided the same result and then telling
    ///         the build system that there is a "better" result of that build step - basically
    ///         a late cache hit - and that the build system should discard what it had produced
    ///         and use what the cache has.
    ///     </para>
    ///     <para>
    ///         In order to not require round trips to the centralized cache all the time and yet
    ///         handle a local cache that may have had builds that happened while the centralized
    ///         cache was off-line or otherwise not available, the cache can mark artifacts as
    ///         ViaCache determinism and thus allow L1 cache hits without verification to L2.
    ///         This gets us nearly the efficiency of fully deterministic tools when the L1 has
    ///         content it knows has ViaCache determinism from the more authoritative source.
    ///         (The L2 cache, or L3, or...)
    ///     </para>
    ///     <para>
    ///         This is logically a tri-state condition.  However, given that there may be
    ///         different "determinism domains" we are actually storing the Guid of the
    ///         determinism domain for those items that have "Via Cache" determinism such that
    ///         the aggregator can provide determinism domain switching in a reasonable manner.
    ///     </para>
    ///     <para>
    ///         Additionally, given that the retention for at least one of our authoritative
    ///         implementations (VSTS BuildCache) is driven by time-based references and metadata
    ///         may age out of the L2/3 cache, we are storing the absolute expiration time here as well.
    ///         Upon expiration, the "effective guid" (the apparent backing authority) becomes None,
    ///         prompting re-checks of the centralized cache even in cases where the guid would otherwise
    ///         have matched.
    ///     </para>
    /// </remarks>
    public readonly struct CacheDeterminism : IEquatable<CacheDeterminism>
    {
        private const int GuidLength = 16;
        private const int DateTimeBinaryLength = 8;
        private static readonly Guid NoneGuid = default(Guid);
        private static readonly Guid ToolGuid = new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
        private static readonly Guid SinglePhaseNonDeterministicGuid = new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFE");

        private CacheDeterminism(Guid cacheGuid, DateTime expirationUtc)
        {
            Guid = cacheGuid;
            ExpirationUtc = expirationUtc;
        }

        /// <summary>
        ///     Gets a value indicating whether this is deterministic
        /// </summary>
        public bool IsDeterministic => (EffectiveGuid != NoneGuid) && (EffectiveGuid != SinglePhaseNonDeterministicGuid);

        /// <summary>
        ///     Gets a value indicating whether the determinism is due to a tool.
        /// </summary>
        public bool IsDeterministicTool => this == Tool;

        /// <summary>
        ///     Gets a value indicating whether this is the special single-phase, non-deterministic form.
        /// </summary>
        public bool IsSinglePhaseNonDeterministic => this == SinglePhaseNonDeterministic;

        /// <summary>
        ///     Gets the authority, if any, providing the validity guarantee.
        /// </summary>
        public Guid EffectiveGuid => DateTime.UtcNow < ExpirationUtc ? Guid : NoneGuid;

        /// <summary>
        ///     Gets the determinism domain GUID
        /// </summary>
        public Guid Guid { get; }

        /// <summary>
        ///     Gets the time until which the Determinism is guaranteed to be valid
        /// </summary>
        public DateTime ExpirationUtc { get; }

        /// <summary>
        ///     Determines whether a value backed by this should be replaced by one backed by the other determinism value
        /// </summary>
        public bool ShouldBeReplacedWith(CacheDeterminism other)
        {
            return IsSinglePhaseNonDeterministic ||
                   (!IsDeterministicTool && !EffectiveGuid.Equals(other.EffectiveGuid) && other.IsDeterministic);
        }

        /// <inheritdoc />
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
        ///     Already-expired is and must be the default value because
        ///     default(CacheDeterminism).ExpirationUtc == default(DateTime) == DateTime.MinValue.
        /// </summary>
        public static readonly DateTime Expired = default(DateTime);

        /// <summary>
        ///     This value is guaranteed to always be valid.
        /// </summary>
        public static readonly DateTime NeverExpires = DateTime.MaxValue;

        /// <summary>
        ///     No determinism is and must be the default value.
        /// </summary>
        public static readonly CacheDeterminism None = default(CacheDeterminism);

        /// <summary>
        ///     Determinism is known due to the tool (which is higher level determinism)
        /// </summary>
        public static readonly CacheDeterminism Tool = new CacheDeterminism(ToolGuid, NeverExpires);

        /// <summary>
        ///     For single-phase lookup behavior emulation, this determinism value
        ///     signifies that there is no determinism via the cache and that a new value
        ///     always overwrites an old value, but *only* if the old and new value are
        ///     both of this determinism value.  It is an error convert from this
        ///     determinism value to any other value or to convert from any other
        ///     value to this determinism value.
        /// </summary>
        public static readonly CacheDeterminism SinglePhaseNonDeterministic =
            new CacheDeterminism(SinglePhaseNonDeterministicGuid, NeverExpires);

        /// <summary>
        ///     Determinism provided via a cache determinism domain
        /// </summary>
        /// <param name="cacheGuid">The Guid of the cache determinism domain.</param>
        /// <param name="expirationUtc">The utc time until which the determinism is guaranteed to be valid.</param>
        /// <returns>A CacheDeterminism type based on this Guid</returns>
        /// <remarks>
        ///     The determinism domain Guid is the Guid of the cache that is
        ///     acting as the source of determinism.
        ///     The expiration time is used to default the displayed Guid to "None" when it expires,
        ///     driving the cache logic consuming that value to re-query the centralized cache.
        ///     Non-authoritative implementations may choose CacheDeterminism.Expired
        ///     in order to provide no guarantees, while confident implementations may choose CacheDeterminism.NeverExpires
        ///     in order to provide an indefinite guarantee.
        ///     Authoritative cache implementations are encouraged to "fuzz" the expiration guarantees
        ///     they provide so that the values do not all expire at the same time.
        /// </remarks>
        public static CacheDeterminism ViaCache(Guid cacheGuid, DateTime expirationUtc)
        {
            Contract.Requires(expirationUtc.Kind == DateTimeKind.Utc || expirationUtc == Expired || expirationUtc == NeverExpires);

            return new CacheDeterminism(cacheGuid, expirationUtc);
        }

        /// <summary>
        ///     Generate a new (random) cache GUID that is not one of the reserved
        ///     special GUID values.
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

        /// <summary>
        ///     Serialize <see cref="CacheDeterminism"/>
        /// </summary>
        public byte[] Serialize()
        {
            var determinismBytes = new byte[GuidLength + DateTimeBinaryLength];
            Array.Copy(Guid.ToByteArray(), determinismBytes, GuidLength);
            Array.Copy(BitConverter.GetBytes(ExpirationUtc.ToBinary()), 0, determinismBytes, GuidLength, DateTimeBinaryLength);
            return determinismBytes;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(CacheDeterminism other)
        {
            return Guid.Equals(other.Guid) && ExpirationUtc.Equals(other.ExpirationUtc);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Guid.GetHashCode() ^ ExpirationUtc.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(CacheDeterminism left, CacheDeterminism right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CacheDeterminism left, CacheDeterminism right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        ///     Deserialize <see cref="CacheDeterminism"/>
        /// </summary>
        public static CacheDeterminism Deserialize(byte[] determinismValue)
        {
            if (determinismValue == null)
            {
                return None;
            }

            var guidBytes = new byte[GuidLength];
            Array.Copy(determinismValue, guidBytes, GuidLength);
            var guid = new Guid(guidBytes);
            var expirationUtc = DateTime.FromBinary(BitConverter.ToInt64(determinismValue, GuidLength));

            return ViaCache(guid, expirationUtc);
        }
    }
}
