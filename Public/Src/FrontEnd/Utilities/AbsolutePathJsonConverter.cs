// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Converts strings coming from JSON to <see cref="AbsolutePath"/>
    /// </summary>
    /// <remarks>
    /// Any malformed path will result in <see cref="AbsolutePath.Invalid"/>
    /// </remarks>
    public class AbsolutePathJsonConverter : ReadOnlyJsonConverter<AbsolutePath>
    {
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public AbsolutePathJsonConverter(PathTable pathTable) : base()
        {
            Contract.Requires(pathTable != null);
            m_pathTable = pathTable;
        }

        /// <nodoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
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

            return fullPath;
        }
        
    }
}
