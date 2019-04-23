// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Converter to deserialize arrays to IReadOnlySets
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ToReadOnlySetJsonConverter<T> : ReadOnlyJsonConverter<IReadOnlySet<T>>
    {
        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            T[] values = serializer.Deserialize<T[]>(reader);
            return new ReadOnlyHashSet<T>(values);
        }
    }
}
