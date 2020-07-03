// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Reflection;
using LanguageServer.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LanguageServer.Infrastructure.JsonDotNet
{
    internal class ResultConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(IResult).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        private static JsonDataType Convert(JsonToken token)
        {
            switch (token)
            {
                case JsonToken.Null:
                    return JsonDataType.Null;
                case JsonToken.StartObject:
                    return JsonDataType.Object;
                default:
                    return default(JsonDataType);
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // This is mainly used by tests
            var token = JToken.ReadFrom(reader);

            return token;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var objectValue = value is IResult result ? result.SuccessObject ?? result.ErrorObject : null;
            if (objectValue == null)
            {
                writer.WriteNull();
                return;
            }

            JToken.FromObject(objectValue, serializer).WriteTo(writer);
        }
    }
}
