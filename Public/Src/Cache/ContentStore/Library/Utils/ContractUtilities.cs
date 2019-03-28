// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    ///     Utilities for using Code Contracts.
    /// </summary>
    public static class ContractUtilities
    {
        /// <summary>
        ///     Calls Contract.Assert(false, message). This wrapper informs the static checker,
        ///     and so can be used to statically assert that branches are unreachable (e.g. switch
        ///     default if all enum values have cases). For informing the C# compiler, this function
        ///     allegedly returns an exception. This allows e.g. <code>throw ContractUtilities.AssertFailure("Oh no!");</code>
        /// </summary>
        /// <remarks>ContractVerification(false) is required here due to the unsatisfiable Requires(false) precondition.</remarks>
        [ContractVerification(false)]

        // ReSharper disable once UnusedParameter.Global
        // ReSharper disable once UnusedMethodReturnValue.Global
        public static Exception AssertFailure([Localizable(false)] string message)
        {
            // The Requires precondition informs the static checker. It's an effectively an assert at the callsite.
            // Can't pass in the message here since it isn't a literal.
            Contract.Requires(false);

            throw new Exception("Should be unreachable (unless the above Require was erased)");
        }
    }
}
