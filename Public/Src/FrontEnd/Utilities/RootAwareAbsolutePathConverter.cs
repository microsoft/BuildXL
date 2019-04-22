// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Utilities
{
    /// <summary>
    /// Can convert to an AbsolutePath from a JSON, but assuming the json has the paths relative to a
    /// 'root' directory. This converter assumes the JSON strings are formatted like DScript relative
    /// paths (i.e., path atoms separated by /), except that they can navigate out of scope 
    /// as long as they don't go out of scope of the 'root'.
    /// </summary>
    public class RootAwareAbsolutePathConverter : ReadOnlyJsonConverter<AbsolutePath>
    {
        private readonly PathTable m_pathTable;
        private readonly AbsolutePath m_root;

        /// <nodoc />
        public RootAwareAbsolutePathConverter(PathTable pathTable, AbsolutePath root)
        {
            m_pathTable = pathTable;
            m_root = root;
        }
        
        /// <inheritdoc cref="JsonConverter"/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var rootAsString = m_root.ToString(m_pathTable);
            var possiblyRelativePath = (string)reader.Value;

            // If the string is already an absolute path just return it
            if (AbsolutePath.TryCreate(m_pathTable, possiblyRelativePath, out var result))
            {
                return result;
            }

            // If not, it may be a path relative to the root (possibly with leading ..)
            var fullPath = Path.Combine(rootAsString, possiblyRelativePath);
            if (!AbsolutePath.TryCreate(m_pathTable, fullPath, out result))
            {
                return AbsolutePath.Invalid;
            } 
            return result;
        }
    }
}
