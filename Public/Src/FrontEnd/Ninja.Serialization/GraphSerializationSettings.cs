// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Ninja.Serialization
{
    /// <summary>
    /// Common setting for serializing and deserializing the constructed graph
    /// </summary>
    public sealed class GraphSerializationSettings
    {
        /// <summary>
        /// Preserves references for objects (so project references get correctly reconstructed), adds indentation for easier 
        /// debugging (at the cost of a slightly higher serialization size) and includes nulls explicitly
        /// </summary>
        public static JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
        };
    }
}
