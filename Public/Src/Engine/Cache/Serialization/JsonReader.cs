// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Newtonsoft.Json;

namespace BuildXL.Engine.Cache.Serialization
{
    /// <summary>
    /// Reads a valid JSON object into a tree.
    /// </summary>
    public class JsonReader
    {
        private JsonTextReader m_reader;

        /// <summary>
        /// Constructor
        /// </summary>
        public JsonReader(string json)
        {
            m_reader = new JsonTextReader(new StringReader(json));
        }

        /// <summary>
        /// Gets the next property with a string value, if any.
        /// </summary>
        /// <returns>
        /// If a string property exists, a <see cref="Property"/>;
        /// otherwise, null.
        /// </returns>
        public Property? GetNextStringValueProperty()
        {
            var property = new Property();
            while (m_reader.Read())
            {
                if (m_reader.TokenType == JsonToken.PropertyName)
                {
                    property.Name = m_reader.Value.ToString();

                    // Attempt to get a string value
                    m_reader.Read();
                    if (m_reader.TokenType == JsonToken.String)
                    {
                        property.Value = m_reader.Value.ToString();
                        return property;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to retrieve the value of a json property by property name.
        /// </summary>
        /// <returns>
        /// True, if the property name was found; otherwise false.
        /// </returns>
        public bool TryGetPropertyValue(string propertyName, out string value)
        {
            value = null;
            for (var maybeProperty = GetNextStringValueProperty(); maybeProperty != null; maybeProperty = GetNextStringValueProperty())
            {
                var property = maybeProperty.Value;

                if (property.Name == propertyName)
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Represents a JSON property.
        /// </summary>
        public struct Property
        {
            /// <summary>
            /// Name of the property.
            /// </summary>
            public string Name;

            /// <summary>
            /// Value of the property.
            /// </summary>
            public string Value;
        }
    }
}
