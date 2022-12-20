// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// Common setting for serializing and deserializing the constructed graph
    /// </summary>
    public sealed class ProjectGraphSerializationSettings
    {
        /// <summary>
        /// Preserves references for objects (so project references get correctly reconstructed), adds indentation for easier 
        /// debugging (at the cost of a slightly higher serialization size) and includes nulls explicitly
        /// </summary>
        public static readonly JsonSerializerSettings Settings = new()
        {
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            Converters = new [] { new GlobalPropertiesDeserializer() }
        };
    }
}
