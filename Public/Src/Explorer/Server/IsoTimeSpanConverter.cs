// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Xml;
using Newtonsoft.Json;

namespace BuildXL.Explorer.Server
{
    /// <summary>
    /// Custom converter for timespans that prints them in ISO duration format
    /// </summary>
    public class IsoTimeSpanConverter : JsonConverter
    {
        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // Use the xml helper which is Iso compliant
            serializer.Serialize(writer, XmlConvert.ToString((TimeSpan)value));
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var value = serializer.Deserialize<string>(reader);
            return XmlConvert.ToTimeSpan(value);
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(TimeSpan) || objectType == typeof(TimeSpan?);
        }
    }
}
