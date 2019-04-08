// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Reflection;
using LanguageServer.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LanguageServer.Infrastructure.JsonDotNet
{
    internal class EitherConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(IEither).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        private static JsonDataType Convert(JsonToken token)
        {
            switch (token)
            {
                case JsonToken.Null:
                    return JsonDataType.Null;
                case JsonToken.Boolean:
                    return JsonDataType.Boolean;
                case JsonToken.Float:
                    return JsonDataType.Number;
                case JsonToken.Integer:
                    return JsonDataType.Number;
                case JsonToken.String:
                    return JsonDataType.String;
                case JsonToken.StartArray:
                    return JsonDataType.Array;
                case JsonToken.StartObject:
                    return JsonDataType.Object;
                default:
                    return default(JsonDataType);
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var either = Activator.CreateInstance(objectType) as IEither;
            var jsonType = Convert(reader.TokenType);
            var tag = either.OnDeserializing(jsonType);
            if (tag == EitherTag.Left)
            {
                var left = serializer.Deserialize(reader, either.LeftType);
                either.Left = left;
                return either;
            }
            else if (tag == EitherTag.Right)
            {
                var right = serializer.Deserialize(reader, either.RightType);
                either.Right = right;
                return either;
            }

            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var either = value as IEither;
            if (either == null)
            {
                return;
            }

            var objectValue = (either.IsLeft) ? either.Left : (either.IsRight) ? either.Right : null;
            if (objectValue == null)
            {
                return;
            }

            JToken.FromObject(objectValue, serializer).WriteTo(writer);
        }
    }
}
