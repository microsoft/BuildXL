// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class ResolverDefaults : IResolverDefaults
    {
        /// <nodoc />
        public ResolverDefaults()
        {
        }

        /// <nodoc />
        public ResolverDefaults(IResolverDefaults template)
        {
            Contract.Assume(template != null);
        }
    }
}
