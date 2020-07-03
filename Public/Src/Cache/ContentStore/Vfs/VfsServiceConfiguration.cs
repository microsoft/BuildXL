// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Vfs
{
    public class VfsServiceConfiguration
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
        /// Configuration for VFS CAS
        /// </summary>
        public VfsCasConfiguration CasConfiguration { get; }

        /// <summary>
        /// The cache root used for cache state of the VFS server
        /// </summary>
        public AbsolutePath ServerRootPath { get; }

        /// <summary>
        /// The cache name
        /// </summary>
        public string CacheName { get; }

        /// <summary>
        /// The scenario of the backing server (defines ready event wait handle name)
        /// </summary>
        public string Scenario { get; }

        private VfsServiceConfiguration(Builder builder)
        {
            ServerGrpcPort = builder.ServerGrpcPort;
            BackingGrpcPort = builder.BackingGrpcPort;
            CasConfiguration = builder.CasConfiguration.Build();

            CacheName = builder.CacheName;
            Scenario = builder.Scenario;
            ServerRootPath = builder.CasConfiguration.RootPath / "server";
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
            /// The cache name
            /// </summary>
            public string CacheName { get; set; }

            /// <summary>
            /// The scenario of the backing server (defines ready event wait handle name)
            /// </summary>
            public string Scenario { get; set; }

            /// <summary>
            /// Configuration for VFS CAS
            /// </summary>
            public VfsCasConfiguration.Builder CasConfiguration { get; } = new VfsCasConfiguration.Builder();

            /// <summary>
            /// Creates a VfsCasConfiguration
            /// </summary>
            public VfsServiceConfiguration Build()
            {
                return new VfsServiceConfiguration(this);
            }
        }
    }
}
