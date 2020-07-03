// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
