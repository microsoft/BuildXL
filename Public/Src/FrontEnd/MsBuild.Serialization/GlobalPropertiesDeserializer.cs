// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// Allows global properties to be deserialized
    /// </summary>
    /// <remarks>
    /// Newtonsoft does not support deserializing read-only collections where references may be preserved
    /// </remarks>
    public sealed class GlobalPropertiesDeserializer : JsonConverter
    {
        /// <nodoc/>
        public GlobalPropertiesDeserializer()
        {
        }

        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsAssignableFrom(typeof(GlobalProperties));
        }

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var dictionary = serializer.Deserialize<Dictionary<string, string>>(reader);
            return dictionary == null ? null : new GlobalProperties(dictionary);
        }

        /// <inheritdoc/>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
