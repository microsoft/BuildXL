// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using LanguageServer.Json;
using Newtonsoft.Json;

namespace LanguageServer.Infrastructure.JsonDotNet
{
    /// <nodoc />
    public class JsonDotNetSerializer : Serializer
    {
        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new JsonConverter[] { new EitherConverter() },
        };

        /// <nodoc />
        public override object Deserialize(Type objectType, string json)
        {
            try
            {
                return JsonConvert.DeserializeObject(json, objectType, _settings);
            }
            catch (JsonReaderException e)
            {
                Debug.Fail(string.Format("Failed to deserialize json message. Exception {0}", e.ToString()));
                return null;
            }
        }

        /// <nodoc />
        public override string Serialize(Type objectType, object value)
        {
            return JsonConvert.SerializeObject(value, _settings);
        }
    }
}
