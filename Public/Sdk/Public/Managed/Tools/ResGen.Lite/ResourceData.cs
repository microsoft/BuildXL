// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace ResGen.Lite
{
    /// <summary>
    /// Encapsulates the data extracted from ResX/ResW files.
    /// </summary>
    public class ResourceData
    {
        private readonly Dictionary<string, ResourceDataEntry> m_stringValues = new Dictionary<string, ResourceDataEntry>();

        /// <summary>
        /// Adds a string resource the the resources.
        /// </summary>
        public bool TryAddString(ResourceDataEntry entry)
        {
            if (m_stringValues.ContainsKey(entry.Name))
            {
                return false;
            }

            m_stringValues.Add(entry.Name, entry);
            return true;
        }

        /// <summary>
        /// Access to the string resources
        /// </summary>
        public IEnumerable<ResourceDataEntry> StringValues => m_stringValues.Values;
    }
}
