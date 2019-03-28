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

            // We allow for empty strings (but in any other case the assumption is that they are well-formed paths)
            if (string.IsNullOrEmpty(pathAsString))
            {
                return AbsolutePath.Invalid;
            }

            AbsolutePath fullPath = pathAsString.ToNormalizedAbsolutePath(m_pathTable);

            return fullPath;
        }
        
    }
}
