// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using LanguageServer.Infrastructure.JsonDotNet;

namespace LanguageServer.Json
{
    /// <nodoc />
    public abstract class Serializer
    {
        /// <nodoc />
        public abstract object Deserialize(Type objectType, string json);

        /// <nodoc />
        public abstract string Serialize(Type objectType, object value);

        /// <nodoc />
        public static Serializer Instance { get; set; } = new JsonDotNetSerializer();
    }
}
