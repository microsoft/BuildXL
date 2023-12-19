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

        /// <summary>
        /// If <see cref="SaveMetricsAsynchronously"/> is true, then this value specifies the size of the internal buffer. Once the buffer is full, the metrics would be dropped.
        /// </summary>
        [DataMember]
        public int? MetricsNagleQueueCapacityLimit { get; set; }

        /// <summary>
        /// The batch size of the metrics nagle queue.
        /// </summary>
        [DataMember]
        public int? MetricsNagleQueueBatchSize { get; set; }

        public LoggingSettings()
        {
        }
    }
}
