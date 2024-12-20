// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

#nullable disable

namespace BuildXL.AdoBuildRunner.Vsts
{
    #region Data objects for HTTP requests, to be serialized to and from JSON
    /// <nodoc />
    [DataContract]
    public class AdoLink
    {
        /// <nodoc />
        [JsonPropertyName("href")]
        [DataMember(Name = "href")]
        public string Href { get; set; }
    }

    /// <nodoc />
    [DataContract]
    public class AdoLinks
    {
        /// <nodoc />
        [JsonPropertyName("self")]
        [DataMember(Name = "self")]
        public AdoLink Self { get; set; }

        /// <nodoc />
        [JsonPropertyName("web")]
        [DataMember(Name = "web")]
        public AdoLink Web { get; set; }
    }

    /// <nodoc />
    [DataContract]
    public class BuildData
    {
        /// <nodoc />
        [JsonPropertyName("_links")]
        [DataMember(Name = "_links")]
        public AdoLinks Links { get; set; }

        /// <nodoc />
        [JsonPropertyName("triggerInfo")]
        [DataMember(Name = "triggerInfo")]
        public Dictionary<string, string> TriggerInfo { get; set; }
    }
    #endregion

    #region IMDS
    // These data objects are a projection of the metadata we need from
    // https://learn.microsoft.com/en-us/azure/virtual-machines/instance-metadata-service

    /// <nodoc />
    [DataContract]
    public class MetadataTag
    {
        /// <nodoc />
        [JsonPropertyName("name")]
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <nodoc />
        [JsonPropertyName("value")]
        [DataMember(Name = "value")]
        public string Value { get; set; }
    }

    /// <nodoc />
    [DataContract]
    public class MetadataComputeSection
    {
        /// <nodoc />
        [JsonPropertyName("tagsList")]
        [DataMember(Name = "tagsList")]
        public List<MetadataTag> TagsList { get; set; }
    }

    /// <nodoc />
    [DataContract]
    public class InstanceMetadata
    {
        /// <nodoc />
        [JsonPropertyName("compute")]
        [DataMember(Name = "compute")]
        public MetadataComputeSection Compute { get; set; }
    }
    #endregion
}