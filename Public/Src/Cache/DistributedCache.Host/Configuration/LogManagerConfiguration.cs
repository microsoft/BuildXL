// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class LogManagerConfiguration
    {
        [DataMember]
        public Dictionary<string, OperationLoggingConfiguration> Logs { get; set; } = new Dictionary<string, OperationLoggingConfiguration>();
    }

    /// <nodoc />
    [DataContract]
    public class OperationLoggingConfiguration
    {
        [DataMember]
        public bool? StartMessage { get; set; }

        [DataMember]
        public bool? StopMessage { get; set; }

        [DataMember]
        public bool? ErrorsOnly { get; set; }

        /// <summary>
        /// If true then none of the messages should be traced.
        /// </summary>
        [DataMember]
        public bool? Disabled { get; set; }

        [DataMember]
        public TimeSpanSetting? SilentOperationDurationThreshold { get; set; }

        [DataMember]
        public TimeSpanSetting? PendingOperationTracingInterval { get; set; }
    }
}
