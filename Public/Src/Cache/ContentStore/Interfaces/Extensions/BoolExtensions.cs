// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Extensions
{
    /// <summary>
    ///     Extensions to help with basic boolean operations
    /// </summary>
    public static class BoolExtensions
    {
        /// <summary>
        ///     Verifies that an implication holds.
        /// </summary>
        /// <returns>False if premise is true, but conclusion is false.</returns>
        public static bool Implies(this bool premise, bool conclusion)
        {
            return !premise || conclusion;
        }
    }
}
