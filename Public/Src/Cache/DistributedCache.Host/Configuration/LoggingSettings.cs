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
        /// If specified, then this account will be used to write mdm metrics to.
        /// </summary>
        [DataMember]
        public string? MdmAccountName { get; set; }

        /// <summary>
        /// If true, then the metrics saved asynchronously using a queue.
        /// </summary>
        [DataMember]
        public bool SaveMetricsAsynchronously { get; set; }

        public LoggingSettings()
        {
        }
    }
}
