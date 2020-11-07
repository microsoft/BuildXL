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
        public uint BufferSize { get; } = 64 << 10;

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
        /// The root of temp virtualized files (i.e. {VfsRootPath}\temp)
        /// </summary>
        public AbsolutePath VfsTempRootPath { get; }

        /// <summary>
        /// The root of temp virtualized directories (relative to vfs root)
        /// </summary>
        public RelativePath VfsTempRelativeRoot { get; }

        private VfsCasConfiguration(Builder builder)
        {
            RootPath = builder.RootPath;

            DataRootPath = RootPath / "data";
            VfsRootPath = RootPath / "vfs";
            VfsCasRelativeRoot = new RelativePath("cas");
            VfsTempRelativeRoot = new RelativePath("temp");
            VfsTempRootPath = VfsRootPath / VfsTempRelativeRoot;
            VfsCasRootPath = VfsRootPath / VfsCasRelativeRoot;
        }

        public class Builder
        {
            /// <summary>
            /// The root used by the VFS for materializing files
            /// </summary>
            public AbsolutePath RootPath { get; set; }

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
