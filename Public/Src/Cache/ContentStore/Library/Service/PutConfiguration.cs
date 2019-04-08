// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Configuration used to put a file into a running instance of CASaaS.
    /// </summary>
    [DataContract]
    public class PutConfiguration : DistributedServiceConfiguration
    {
        /// <summary>
        /// Constructor for PutConfiguration.
        /// </summary>
        public PutConfiguration(
            HashType hashType,
            AbsolutePath sourcePath,
            uint grpcPort,
            string cacheName,
            AbsolutePath cachePath)
            : base(null, 5, grpcPort, null, null, cacheName, cachePath, null)
        {
            HashType = hashType;
            SourcePath = sourcePath;
        }

        /// <summary>
        /// Hash type for the file to be put.
        /// </summary>
        [DataMember]
        public HashType HashType { get; private set; }

        /// <summary>
        /// Path to the file to be inserted into the cache.
        /// </summary>
        [DataMember]
        public AbsolutePath SourcePath { get; private set; }

        /// <inheritdoc />
        public override string GetVerb()
        {
            return "putfile";
        }

        /// <inheritdoc />
        public override string GetCommandLineArgs(LocalServerConfiguration localContentServerConfiguration = null, string scenario = null, bool logAutoFlush = false, bool passMaxConnections = false)
        {
            var args = new StringBuilder(base.GetCommandLineArgs(localContentServerConfiguration, scenario, logAutoFlush, false));

            args.AppendFormat(" /hashType:{0}", HashType.ToString());
            args.AppendFormat(" /path:{0}", SourcePath.Path);

            return args.ToString();
        }
    }
}
