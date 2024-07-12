// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace BuildXL.Cache.BuildCacheResource.Model
{
    /// <nodoc/>
    public class JsonUriConverter : JsonConverter<Uri>
    {
        /// <inheritdoc/>
        public override Uri Read(ref Utf8JsonReader reader,Type typeToConvert, JsonSerializerOptions options) =>
                    new Uri(reader.GetString()!);

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, Uri uriValue, JsonSerializerOptions options) =>
                    writer.WriteStringValue(uriValue.AbsoluteUri);
    }
}
