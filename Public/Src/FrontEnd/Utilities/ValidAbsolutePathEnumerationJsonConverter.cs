// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Core;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Do not add invalid absolute paths when deserializing collections
    /// </summary>
    public class ValidAbsolutePathEnumerationJsonConverter : ReadOnlyJsonConverter<IEnumerable<AbsolutePath>>
    {
        /// <nodoc/>
        public static ValidAbsolutePathEnumerationJsonConverter Instance = new ValidAbsolutePathEnumerationJsonConverter();

        private ValidAbsolutePathEnumerationJsonConverter()
        { }

        /// <nodoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var ret = new List<AbsolutePath>();

            if (reader.TokenType == JsonToken.StartArray)
            {
                reader.Read();
                while (reader.TokenType != JsonToken.EndArray)
                {
                    var item = serializer.Deserialize(reader, objectType.GetGenericArguments()[0]);
                    reader.Read();

                    if (item is AbsolutePath path && path.IsValid)
                    {
                        ret.Add(path);
                    }
                }
            }

            return ret;
        }
    }
}
