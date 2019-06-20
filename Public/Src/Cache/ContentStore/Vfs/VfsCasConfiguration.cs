// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Vfs
{
    public class VfsCasConfiguration
    {
        /// <summary>
        /// Grpc port exposed by VFS CAS
        /// </summary>
        public int ServerGrpcPort { get; }

        /// <summary>
        /// Grpc port of backing CAS service used to materialize files
        /// </summary>
        public int BackingGrpcPort { get; }

        /// <summary>
        /// The root used by the VFS for materializing files
        /// </summary>
        public AbsolutePath RootPath { get; }

        /// <summary>
        /// The data root used for state of the VFS
        /// </summary>
        public AbsolutePath DataRootPath { get; }

        /// <summary>
        /// The cache root used for cache state of the VFS server
        /// </summary>
        public AbsolutePath ServerRootPath { get; }

        /// <summary>
        /// The root virtualized directory
        /// </summary>
        public AbsolutePath VfsRootPath { get; }

        /// <summary>
        /// The root of mounted virtualized directories
        /// </summary>
        public AbsolutePath VfsMountRootPath { get; }

        /// <summary>
        /// The root of mounted virtualized directories
        /// </summary>
        public RelativePath VfsMountRelativeRoot { get; }

        /// <summary>
        /// The root of mounted virtualized directories
        /// </summary>
        public RelativePath VfsCasRelativeRoot { get; }

        /// <summary>
        /// The root of virtualized CAS directory
        /// </summary>
        public AbsolutePath VfsCasRootPath { get; }

        /// <summary>
        /// The cache name
        /// </summary>
        public string CacheName { get; }

        /// <summary>
        /// The scenario of the backing server (defines ready event wait handle name)
        /// </summary>
        public string Scenario { get; }

        /// <summary>
        /// Specifies folder names under the VFS which will be junctioned to given destination paths
        /// Maybe just names (i.e. no sub paths)
        /// </summary>
        public IReadOnlyDictionary<string, AbsolutePath> VirtualizationMounts { get; }

        private VfsCasConfiguration(Builder builder)
        {
            ServerGrpcPort = builder.ServerGrpcPort;
            BackingGrpcPort = builder.BackingGrpcPort;
            RootPath = builder.RootPath;
            VirtualizationMounts = builder.VirtualizationMounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            CacheName = builder.CacheName;
            Scenario = builder.Scenario;

            DataRootPath = RootPath / "data";
            VfsRootPath = RootPath / "vfs";
            VfsMountRelativeRoot = new RelativePath("mounts");
            VfsMountRootPath = VfsRootPath / VfsMountRelativeRoot;
            VfsCasRelativeRoot = new RelativePath("cas");
            VfsCasRootPath = VfsRootPath / VfsCasRelativeRoot;
            ServerRootPath = RootPath / "server";
        }

        public class Builder
        {
            /// <summary>
            /// Grpc port exposed by VFS CAS
            /// </summary>
            public int ServerGrpcPort { get; set; }

            /// <summary>
            /// Grpc port of backing CAS service used to materialize files
            /// </summary>
            public int BackingGrpcPort { get; set; }

            /// <summary>
            /// The root used by the VFS for materializing files
            /// </summary>
            public AbsolutePath RootPath { get; set; }

            /// <summary>
            /// The cache name
            /// </summary>
            public string CacheName { get; set; }

            /// <summary>
            /// The scenario of the backing server (defines ready event wait handle name)
            /// </summary>
            public string Scenario { get; set; }

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
