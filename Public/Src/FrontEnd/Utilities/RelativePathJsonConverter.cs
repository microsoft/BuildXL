// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Converts strings coming from JSON to <see cref="RelativePath"/> and back
    /// </summary>
    /// <remarks>
    /// Any malformed path will result in <see cref="RelativePath.Invalid"/>
    /// </remarks>
    public class RelativePathJsonConverter : JsonConverter<RelativePath>
    {
        private readonly StringTable m_stringTable;

        /// <summary>
        /// Allows for setting a sequence of redirections (original root to redirected root) 
        /// </summary>
        public RelativePathJsonConverter(StringTable stringTable) : base()
        {
            Contract.Requires(stringTable != null);

            m_stringTable = stringTable;
        }

        /// <nodoc/>
        public override RelativePath ReadJson(JsonReader reader, Type objectType, RelativePath existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var pathAsString = (string)reader.Value;

            // If the string is empty, or the relative path cannot be created, RelativePath.Invalid is returned
            if (string.IsNullOrEmpty(pathAsString))
            {
                return RelativePath.Invalid;
            }

            if (!RelativePath.TryCreate(m_stringTable, pathAsString, out RelativePath relPath))
            {
                return RelativePath.Invalid;
            }

            return relPath;
        }

        /// <nodoc/>
        public override void WriteJson(JsonWriter writer, RelativePath value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString(m_stringTable));
        }
    }
}
