// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Converts strings coming from JSON to <see cref="AbsolutePath"/> and back
    /// </summary>
    /// <remarks>
    /// Any malformed path will result in <see cref="AbsolutePath.Invalid"/>
    /// </remarks>
    public class AbsolutePathJsonConverter : JsonConverter<AbsolutePath>
    {
        private readonly PathTable m_pathTable;
        private readonly IReadOnlyCollection<(AbsolutePath, AbsolutePath)> m_redirection;

        /// <summary>
        /// Allows for setting a sequence of redirections (original root to redirected root) 
        /// </summary>
        public AbsolutePathJsonConverter(PathTable pathTable, IReadOnlyCollection<(AbsolutePath, AbsolutePath)> redirection = null) : base()
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_redirection = redirection;
        }

        /// <nodoc/>
        public override AbsolutePath ReadJson(JsonReader reader, Type objectType, AbsolutePath existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var pathAsString = (string)reader.Value;

            // If the string is empty, or the absolute path cannot be created, AbsolutePath.Invalid is returned
            if (string.IsNullOrEmpty(pathAsString))
            {
                return AbsolutePath.Invalid;
            }

            if (!AbsolutePath.TryCreate(m_pathTable, pathAsString, out AbsolutePath fullPath))
            {
                return AbsolutePath.Invalid;
            }

            // If the path is within any of the the original roots, we redirect it to the corresponding redirected root
            // The redirection collection is traversed in order and first matches win
            if (m_redirection != null && m_redirection.FirstOrDefault(kvp => fullPath.IsWithin(m_pathTable, kvp.Item1)) is var redirectedPair && redirectedPair != default)
            {
                return fullPath.Relocate(m_pathTable, redirectedPair.Item1, redirectedPair.Item2);
            }

            return fullPath;
        }

        /// <nodoc/>
        public override void WriteJson(JsonWriter writer, AbsolutePath value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString(m_pathTable));
        }
    }
}
