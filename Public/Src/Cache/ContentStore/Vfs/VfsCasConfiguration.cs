// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Vfs
{
    public class VfsCasConfiguration
    {
        /// <summary>
        /// Indicates whether symlinks to virtual paths should be used.
        /// </summary>
        public bool UseSymlinks { get; }

        /// <summary>
        /// The root used by the VFS for materializing files
        /// </summary>
        public AbsolutePath RootPath { get; }

        /// <summary>
        /// The data root used for state of the VFS
        /// </summary>
        public AbsolutePath DataRootPath { get; }

        /// <summary>
        /// The root virtualized directory
        /// </summary>
        public AbsolutePath VfsRootPath { get; }

        /// <summary>
        /// The root of VFS cas used for symlink targets
        /// </summary>
        public AbsolutePath VfsCasRootPath { get; }

        /// <summary>
        /// The root of VFS cas used for symlink targets (relative to vfs root)
        /// </summary>
        public RelativePath VfsCasRelativeRoot { get; }

        /// <summary>
        /// The root of mounted virtualized directories (i.e. {VfsRootPath}\mounts)
        /// </summary>
        public AbsolutePath VfsMountRootPath { get; }

        /// <summary>
        /// The root of mounted virtualized directories (relative to vfs root)
        /// </summary>
        public RelativePath VfsMountRelativeRoot { get; }

        /// <summary>
        /// Specifies folder names under the VFS which will be junctioned to given destination paths
        /// Maybe just names (i.e. no sub paths)
        /// </summary>
        public IReadOnlyDictionary<string, AbsolutePath> VirtualizationMounts { get; }

        private VfsCasConfiguration(Builder builder)
        {
            RootPath = builder.RootPath;
            UseSymlinks = builder.UseSymlinks;
            VirtualizationMounts = builder.VirtualizationMounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            DataRootPath = RootPath / "data";
            VfsRootPath = RootPath / "vfs";
            VfsMountRelativeRoot = new RelativePath("mounts");
            VfsCasRelativeRoot = new RelativePath("cas");
            VfsMountRootPath = VfsRootPath / VfsMountRelativeRoot;
            VfsCasRootPath = VfsRootPath / VfsCasRelativeRoot;
        }

        public class Builder
        {
            /// <summary>
            /// Indicates whether symlinks to virtual paths should be used.
            /// </summary>
            public bool UseSymlinks { get; set; }

            /// <summary>
            /// The root used by the VFS for materializing files
            /// </summary>
            public AbsolutePath RootPath { get; set; }

            /// <summary>
            /// Specifies folder names under the VFS which will be junctioned to given destination paths
            /// Maybe just names (i.e. no sub paths)
            /// </summary>
            public Dictionary<string, AbsolutePath> VirtualizationMounts { get; } = new Dictionary<string, AbsolutePath>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Creates a VfsCasConfiguration
            /// </summary>
            public VfsCasConfiguration Build()
            {
                return new VfsCasConfiguration(this);
            }
        }
    }
}
