// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class DefaultSourceResolverSettings : ResolverSettings, IDefaultSourceResolverSettings
    {
        /// <nodoc />
        public DefaultSourceResolverSettings()
        {
        }

        /// <nodoc />
        public DefaultSourceResolverSettings(IDefaultSourceResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Requires(template != null);
            Contract.Requires(pathRemapper != null);
        }
    }
}
