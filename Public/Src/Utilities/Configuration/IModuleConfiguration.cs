// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines a module's configuration
    /// </summary>
    public interface IModuleConfiguration : ITrackedValue
    {
        /// <summary>
        /// The module id for the module
        /// </summary>
        ModuleId ModuleId { get; }

        /// <summary>
        /// The module name
        /// </summary>
        [CanBeNull]
        string Name { get; }

        /// <summary>
        /// List of file access exception rules.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<IFileAccessWhitelistEntry> FileAccessWhiteList { get; }

        /// <summary>
        /// List of file accesses that are benign and allow the pip that caused them to be cached.
        /// </summary>
        /// <remarks>
        /// This is a separate list from the above, rather than a bool field on the exceptions, because that makes it easier for a
        /// central build team to control the contents of the (relatively dangerous) cacheable whitelist.  It can be placed in a
        /// separate file in a locked-down area in source control, even while exposing the (safer)
        /// do-not-cache-but-also-do-not-error whitelist to users.
        /// </remarks>
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<IFileAccessWhitelistEntry> CacheableFileAccessWhitelist { get; }

        /// <summary>
        /// Exceptions for computing directory membership fingerprints
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<IDirectoryMembershipFingerprinterRule> DirectoryMembershipFingerprinterRules { get; }

        /// <summary>
        /// The set of mounts defined for the module
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IReadOnlyList<IMount> Mounts { get; }
    }
}
