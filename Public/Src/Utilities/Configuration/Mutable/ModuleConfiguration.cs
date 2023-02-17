// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class ModuleConfiguration : TrackedValue, IModuleConfiguration
    {
        /// <nodoc />
        public ModuleConfiguration()
        {
            ModuleId = ModuleId.Invalid;
            FileAccessAllowList = new List<IFileAccessAllowlistEntry>();
            CacheableFileAccessAllowlist = new List<IFileAccessAllowlistEntry>();
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
            FileAccessAllowList = new List<IFileAccessAllowlistEntry>(template.FileAccessAllowList.Select(entry => new FileAccessAllowlistEntry(entry, pathRemapper)));
            CacheableFileAccessAllowlist = new List<IFileAccessAllowlistEntry>(template.CacheableFileAccessAllowList.Select(entry => new FileAccessAllowlistEntry(entry, pathRemapper)));
            DirectoryMembershipFingerprinterRules = new List<IDirectoryMembershipFingerprinterRule>(template.DirectoryMembershipFingerprinterRules.Select(rule => new DirectoryMembershipFingerprinterRule(rule, pathRemapper)));
            Mounts = new List<IMount>(template.Mounts.Select(mount => new Mount(mount, pathRemapper)));
        }

        /// <inheritdoc />
        public ModuleId ModuleId { get; set; }

        /// <inheritdoc />
        public string Name { get; set; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IFileAccessAllowlistEntry> FileAccessAllowList { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IFileAccessAllowlistEntry> IModuleConfiguration.FileAccessAllowList => FileAccessAllowList;


        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IFileAccessAllowlistEntry> CacheableFileAccessAllowlist { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IFileAccessAllowlistEntry> IModuleConfiguration.CacheableFileAccessAllowList => CacheableFileAccessAllowlist;

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


        /// <summary>
        /// Compatibility
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IFileAccessAllowlistEntry> FileAccessWhiteList
        {
            get => FileAccessAllowList;
            set => FileAccessAllowList = value;
        }

        /// <summary>
        /// Compatibility
        /// </summary>
        IReadOnlyList<IFileAccessAllowlistEntry> IModuleConfiguration.FileAccessWhiteList => FileAccessAllowList;

        /// <summary>
        /// Compatibility
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IFileAccessAllowlistEntry> CacheableFileAccessWhitelist
        {
            get => CacheableFileAccessAllowlist;
            set => CacheableFileAccessAllowlist = value;
        }

        /// <summary>
        /// Compatibility
        /// </summary>
        IReadOnlyList<IFileAccessAllowlistEntry> IModuleConfiguration.CacheableFileAccessWhitelist => CacheableFileAccessAllowlist;
    }
}
