// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Can convert from JSON to the specified type, but can't serialize
    /// Inheritors must implement the conversion strategy overriding the ReadJson method
    /// </summary>
    public abstract class ReadOnlyJsonConverter<T> : JsonConverter
    {

        /// <inheritdoc />
        /// <summary>
        /// Always true
        /// </summary>
        public sealed override bool CanRead => true;

        /// <inheritdoc />
        /// <summary>
        /// Always false, this converter is just for reading
        /// </summary>
        public sealed override bool CanWrite => false;

        /// <inheritdoc />
        /// <nodoc />
        public override bool CanConvert(Type objectType)
        {
            return typeof(T).IsAssignableFrom(objectType);
        }

        /// <inheritdoc />
        /// <summary>
        /// Throws an exception: this converter shouldn't be used to write 
        /// </summary>
        public sealed override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotImplementedException();
    }
}
