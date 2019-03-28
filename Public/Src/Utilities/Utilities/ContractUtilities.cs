// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utilities for using Code Contracts.
    /// </summary>
    public static class ContractUtilities
    {
        /// <summary>
        /// Allows erasing a precondition / postcondition expression as part of rewriting contracts (even when runtime checking is enabled).
        /// </summary>
        /// <example>
        /// Contract.Requires(ContractUtilities.Static(ExpensiveCheckIsTrue()));
        /// </example>
        [ContractVerification(false)]
        [ContractRuntimeIgnored]
        [Pure]
        public static bool Static(bool expression)
        {
            return expression;
        }
    }
}
