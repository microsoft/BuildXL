// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
