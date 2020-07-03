// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <nodoc />
        [DataMember]
        public AzureBlobStorageLogPublicConfiguration? Configuration { get; set; } = null;

        public LoggingSettings()
        {
        }
    }
}
