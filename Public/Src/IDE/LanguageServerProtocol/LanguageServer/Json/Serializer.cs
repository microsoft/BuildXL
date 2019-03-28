// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
