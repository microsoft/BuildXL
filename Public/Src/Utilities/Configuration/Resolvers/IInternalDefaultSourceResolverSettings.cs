// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Internal default source resolver.
    /// </summary>
    /// <remarks>
    /// Users should not create a resolver of this kind. To use the resolver,
    /// users create a marker using <see cref="IDefaultSourceResolverSettings" />.
    /// </remarks>
    public partial interface IInternalDefaultDScriptResolverSettings : IDScriptResolverSettings
    {
        /// <summary>
        /// Paths to orphan projects.
        /// </summary>
        IReadOnlyList<AbsolutePath> Projects { get; }

        /// <summary>
        /// Path to the configuration file.
        /// </summary>
        AbsolutePath ConfigFile { get; }
    }
}
