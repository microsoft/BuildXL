// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class ModuleConfiguration : TrackedValue, IModuleConfiguration
    {
        /// <nodoc />
        public ModuleConfiguration()
        {
            ModuleId = ModuleId.Invalid;
            FileAccessWhiteList = new List<IFileAccessWhitelistEntry>();
            CacheableFileAccessWhitelist = new List<IFileAccessWhitelistEntry>();
            DirectoryMembershipFingerprinterRules = new List<IDirectoryMembershipFingerprinterRule>();
            Mounts = new List<IMount>();
        }

        /// <nodoc />
        public ModuleConfiguration(IModuleConfiguration template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            ModuleId = template.ModuleId;
            Name = template.Name;
            FileAccessWhiteList = new List<IFileAccessWhitelistEntry>(template.FileAccessWhiteList.Select(entry => new FileAccessWhitelistEntry(entry, pathRemapper)));
            CacheableFileAccessWhitelist = new List<IFileAccessWhitelistEntry>(template.CacheableFileAccessWhitelist.Select(entry => new FileAccessWhitelistEntry(entry, pathRemapper)));
            DirectoryMembershipFingerprinterRules = new List<IDirectoryMembershipFingerprinterRule>(template.DirectoryMembershipFingerprinterRules.Select(rule => new DirectoryMembershipFingerprinterRule(rule, pathRemapper)));
            Mounts = new List<IMount>(template.Mounts.Select(mount => new Mount(mount, pathRemapper)));
        }

        /// <inheritdoc />
        public ModuleId ModuleId { get; set; }

        /// <inheritdoc />
        public string Name { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IFileAccessWhitelistEntry> FileAccessWhiteList { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IFileAccessWhitelistEntry> IModuleConfiguration.FileAccessWhiteList => FileAccessWhiteList;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IFileAccessWhitelistEntry> CacheableFileAccessWhitelist { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IFileAccessWhitelistEntry> IModuleConfiguration.CacheableFileAccessWhitelist => CacheableFileAccessWhitelist;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IDirectoryMembershipFingerprinterRule> DirectoryMembershipFingerprinterRules { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IDirectoryMembershipFingerprinterRule> IModuleConfiguration.DirectoryMembershipFingerprinterRules => DirectoryMembershipFingerprinterRules;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IMount> Mounts { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IMount> IModuleConfiguration.Mounts => Mounts;
    }
}
