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
        private readonly AbsolutePath m_originalRoot;
        private readonly AbsolutePath m_redirectedRoot;

        /// <nodoc/>
        public AbsolutePathJsonConverter(PathTable pathTable) : this(pathTable, AbsolutePath.Invalid, AbsolutePath.Invalid)
        { }

        /// <summary>
        /// Allows for setting a single path redirection (original root to redirected root)
        /// </summary>
        public AbsolutePathJsonConverter(PathTable pathTable, AbsolutePath originalRoot, AbsolutePath redirectedRoot) : base()
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(!originalRoot.IsValid || redirectedRoot.IsValid);

            m_pathTable = pathTable;
            m_originalRoot = originalRoot;
            m_redirectedRoot = redirectedRoot;
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

            // If the path is within the original root, we redirect it to the redirected root
            if (m_originalRoot.IsValid && fullPath.IsWithin(m_pathTable, m_originalRoot))
            {
                return fullPath.Relocate(m_pathTable, m_originalRoot, m_redirectedRoot);
            }

            return fullPath;
        }
        
    }
}
