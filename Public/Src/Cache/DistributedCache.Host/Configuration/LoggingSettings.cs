// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;

#nullable enable

namespace BuildXL.Cache.Host.Configuration
{
    /// <nodoc />
    [DataContract]
    public class LoggingSettings
    {
        /// <nodoc />
        [DataMember]
        public string? NLogConfigurationPath { get; set; } = null;

        /// <summary>
        /// Defines replacements which should be applied to configuration file
        /// </summary>
        [DataMember]
        public Dictionary<string, string> NLogConfigurationReplacements { get; set; } = new Dictionary<string, string>();

        /// <nodoc />
        [DataMember]
        public AzureBlobStorageLogPublicConfiguration? Configuration { get; set; } = null;

        /// <summary>
        /// Whether to use a more optimized NLog Layout that avoids excessive allocations by using pooled StringBuilders.
        /// </summary>
        [DataMember]
        public bool? UseOptimizedNLogRenderLayout { get; set; }

        public LoggingSettings()
        {
        }
    }
}
