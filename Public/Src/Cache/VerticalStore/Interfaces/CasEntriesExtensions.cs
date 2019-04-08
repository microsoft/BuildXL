// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Extension methods for CASEntries.
    /// </summary>
    public static class CasEntriesExtensions
    {
        /// <summary>
        /// Given a set of casentries returns a potentially different set of CASEntries with a determinism guid
        /// that matches the requirements of whether the entry is from an authoritative cache with the guid specified
        /// and an expiration time.
        /// </summary>
        public static CasEntries GetModifiedCasEntriesWithDeterminism(in this CasEntries originalEntries, bool isauthoritative, Guid value, DateTime expirationUtc)
        {
            if (ShouldStampNewDeterminism(originalEntries, isauthoritative))
            {
                return new CasEntries(originalEntries, CacheDeterminism.ViaCache(value, expirationUtc));
            }
            else
            {
                return originalEntries;
            }
        }

        /// <summary>
        /// Given a determinism returns a potentially different determinism
        /// that matches the requirements of whether the determinism is from an authoritative cache with the guid specified
        /// and an expiration time.
        /// </summary>
        public static CacheDeterminism GetFinalDeterminism(in this CasEntries originalEntries, bool isauthoritative, Guid value, DateTime expirationUtc)
        {
            if (ShouldStampNewDeterminism(originalEntries, isauthoritative))
            {
                return CacheDeterminism.ViaCache(value, expirationUtc);
            }
            else
            {
                return originalEntries.Determinism;
            }
        }

        private static bool ShouldStampNewDeterminism(in CasEntries originalEntries, bool isauthoritative)
        {
            return isauthoritative && !originalEntries.Determinism.IsDeterministicTool && !originalEntries.Determinism.IsSinglePhaseNonDeterministic;
        }
    }
}
