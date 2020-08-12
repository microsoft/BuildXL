// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Policy to be applied when a process incurs in a double write or source rewrite
    /// </summary>
    [Flags]
    public enum RewritePolicy : byte
    {
        /// <summary>
        /// Double writes are blocked
        /// </summary>
        DoubleWritesAreErrors = 1,

        /// <summary>
        /// Double writes are allowed as long as the file content is the same
        /// </summary>
        AllowSameContentDoubleWrites = 2,

        /// <summary>
        /// Double writes are allowed and the first output will (non-deterministically) represent the final
        /// output
        /// </summary>
        /// <remarks>
        /// This option is unsafe since it introduces non-deterministic behavior.
        /// </remarks>
        UnsafeFirstDoubleWriteWins = 4,

        /// <summary>
        /// Any attempt to rewrite a source file is blocked
        /// </summary>
        SourceRewritesAreErrors = 8,

        /// <summary>
        /// Dynamic outputs can rewrite sources or alien files
        /// </summary>
        SafeSourceRewritesAreAllowed = 16,

        /// <summary>
        /// A default strict value
        /// </summary>
        DefaultStrict = DoubleWritesAreErrors | SourceRewritesAreErrors,

        /// <summary>
        /// A default safe value
        /// </summary>
        DefaultSafe = AllowSameContentDoubleWrites | SafeSourceRewritesAreAllowed,

        /// <summary>
        /// All double write cases
        /// </summary>
        DoubleWriteMask = DoubleWritesAreErrors | AllowSameContentDoubleWrites | UnsafeFirstDoubleWriteWins,

        /// <summary>
        /// All source rewrite cases
        /// </summary>
        SourceRewriteMask = SourceRewritesAreErrors | SafeSourceRewritesAreAllowed,
    }

    /// <nodoc/>
    public static class RewritePolicyExtensions
    {
        /// <summary>
        /// Checks the policy is valid. There should be at most one double write policy set and one source rewrite policy set
        /// </summary>
        public static bool IsValid(this RewritePolicy policy)
        {
            var doubleWrite = policy & RewritePolicy.DoubleWriteMask;

            // If the double write policy is not a power of two, then there is more than one set and this is not valid
            if (doubleWrite != 0 && (doubleWrite & (doubleWrite - 1)) != 0)
            {
                return false;
            }

            // Same treatment for source rewrite policies
            var sourceRewrite = policy & RewritePolicy.SourceRewriteMask;
            return (sourceRewrite == 0 || (sourceRewrite & (sourceRewrite - 1)) == 0);
        }

        /// <summary>
        /// Sets the strictest defaults for both double write and source rewrite settings if absent, respecting already configured settings.
        /// </summary>
        public static RewritePolicy StrictDefaultsIfAbsent(this RewritePolicy rewritePolicy)
        {
            if ((rewritePolicy & RewritePolicy.DoubleWriteMask) == 0)
            {
                rewritePolicy |= RewritePolicy.DoubleWritesAreErrors;
            }

            if ((rewritePolicy & RewritePolicy.SourceRewriteMask) == 0)
            {
                rewritePolicy |= RewritePolicy.SourceRewritesAreErrors;
            }

            return rewritePolicy;
        }

        /// <summary>
        /// Whether the double-write policy implies that double writes should be flagged as a warning (as opposed to an error)
        /// </summary>
        public static bool ImpliesDoubleWriteIsWarning(this RewritePolicy policy)
        {
            switch (policy & RewritePolicy.DoubleWriteMask)
            {
                case RewritePolicy.DoubleWritesAreErrors:
                case RewritePolicy.AllowSameContentDoubleWrites:
                    return false;
                case RewritePolicy.UnsafeFirstDoubleWriteWins:
                    return true;
                default:
                    throw new InvalidOperationException("Unexpected double write policy " + policy.ToString());
            }
        }

        /// <summary>
        /// Whether the double-write policy implies that double writes are possible without implying a build break
        /// </summary>
        public static bool ImpliesDoubleWriteAllowed(this RewritePolicy policy)
        {
            switch (policy & RewritePolicy.DoubleWriteMask)
            {
                case RewritePolicy.DoubleWritesAreErrors:
                    return false;
                case RewritePolicy.AllowSameContentDoubleWrites:
                case RewritePolicy.UnsafeFirstDoubleWriteWins:
                    return true;
                default:
                    throw new InvalidOperationException("Unexpected double write policy " + policy.ToString());
            }
        }

        /// <summary>
        /// Whether the policy implies that produced content defines what is allowed/denied
        /// </summary>
        public static bool ImpliesContentAwareness(this RewritePolicy policy)
        {
            return (policy & RewritePolicy.AllowSameContentDoubleWrites) != 0 || (policy & RewritePolicy.SafeSourceRewritesAreAllowed) != 0;
        }
    }
}
