// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildToolsInstaller.Utilities
{
    public sealed class CaseInsensitiveDictionaryConverter<TValue> : JsonConverter<IReadOnlyDictionary<string, TValue>>
    {
        public override IReadOnlyDictionary<string, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dic = JsonSerializer.Deserialize<Dictionary<string, TValue>>(ref reader, options);
            if (dic == null)
            {
                return null;
            };

            return new Dictionary<string, TValue>(dic, StringComparer.OrdinalIgnoreCase);
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, TValue> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
