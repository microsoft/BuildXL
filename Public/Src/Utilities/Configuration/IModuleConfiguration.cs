// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;
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
        [NotNull]
        IReadOnlyList<IFileAccessAllowlistEntry> FileAccessAllowList { get; }

        /// <summary>
        /// List of file accesses that are benign and allow the pip that caused them to be cached.
        /// </summary>
        /// <remarks>
        /// This is a separate list from the above, rather than a bool field on the exceptions, because that makes it easier for a
        /// central build team to control the contents of the (relatively dangerous) cacheable allowlist.  It can be placed in a
        /// separate file in a locked-down area in source control, even while exposing the (safer)
        /// do-not-cache-but-also-do-not-error allowlist to users.
        /// </remarks>
        [NotNull]
        IReadOnlyList<IFileAccessAllowlistEntry> CacheableFileAccessAllowList { get; }

        /// <summary>
        /// Exceptions for computing directory membership fingerprints
        /// </summary>
        [NotNull]
        IReadOnlyList<IDirectoryMembershipFingerprinterRule> DirectoryMembershipFingerprinterRules { get; }

        /// <summary>
        /// The set of mounts defined for the module
        /// </summary>
        [NotNull]
        IReadOnlyList<IMount> Mounts { get; }

        // These fields exist to support the legacy name of AllowLists. The name must exactly match what is specified in configuration files.
        #region Compatibility
        /// <summary>
        /// Compatibility
        /// </summary>
        [NotNull]
        IReadOnlyList<IFileAccessAllowlistEntry> CacheableFileAccessWhitelist { get; }

        /// <summary>
        /// Compatibility
        /// </summary>
        [NotNull]
        IReadOnlyList<IFileAccessAllowlistEntry> FileAccessWhiteList { get; }
        #endregion
    }
}
