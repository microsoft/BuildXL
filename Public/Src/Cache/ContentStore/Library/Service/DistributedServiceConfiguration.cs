// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    /// Distributed CASaaS configuration
    /// </summary>
    [DataContract]
    public class DistributedServiceConfiguration : ServiceConfiguration
    {
        /// <summary>
        /// Constructor for DistributedServiceConfiguration
        /// </summary>
        public DistributedServiceConfiguration(
            AbsolutePath dataRootPath,
            uint gracefulShutdownSeconds,
            uint grpcPort,
            string stampId,
            string ringId,
            string cacheName,
            AbsolutePath cachePath,
            string grpcPortFileName = null)
            : base(new Dictionary<string, AbsolutePath>(), dataRootPath, 0, gracefulShutdownSeconds, (int)grpcPort, grpcPortFileName)
        {
            StampId = stampId;
            RingId = ringId;
            CacheName = cacheName;
            CachePath = cachePath;
        }

        /// <summary>
        /// The stamp identifier for this service instance.
        /// </summary>
        [DataMember]
        public string StampId { get; private set; }

        /// <summary>
        /// The ring identifier for this service instance.
        /// </summary>
        [DataMember]
        public string RingId { get; private set; }

        /// <summary>
        /// Name of the cache behind this service instance.
        /// </summary>
        [DataMember]
        public string CacheName { get; private set; }

        /// <summary>
        /// Path to the root of the cache behind this service instance.
        /// </summary>
        [DataMember]
        public AbsolutePath CachePath { get; private set; }

        /// <inheritdoc />
        public override string GetVerb()
        {
            return "distributedservice";
        }

        /// <inheritdoc />
        public override string GetCommandLineArgs(LocalServerConfiguration localContentServerConfiguration = null, string scenario = null, bool logAutoFlush = false, bool passMaxConnections = false)
        {
            var args = new StringBuilder(base.GetCommandLineArgs(localContentServerConfiguration, scenario, logAutoFlush, false));

            if (!string.IsNullOrEmpty(StampId))
            {
                args.AppendFormat(" /stampId:{0}", StampId);
            }

            if (!string.IsNullOrEmpty(RingId))
            {
                args.AppendFormat(" /ringId:{0}", RingId);
            }

            if (!string.IsNullOrEmpty(CacheName))
            {
                args.AppendFormat(" /cacheName:{0}", CacheName);
            }

            if (CachePath != null)
            {
                args.AppendFormat(" /cachePath:{0}", CachePath.Path);
            }

            return args.ToString();
        }
    }
}
