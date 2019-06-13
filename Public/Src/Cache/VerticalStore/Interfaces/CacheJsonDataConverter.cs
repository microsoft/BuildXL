// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Utility class that works with the Newtonsoft Json parser and converts Json data into ICacheConfigData
    /// </summary>
    public sealed class CacheJsonDataConverter : JsonConverter
    {
        // Class that implements the basic ICacheConfigData (basically a
        // simple dictionary of string to object mapping.
        private sealed class CacheConfigData : Dictionary<string, object>, ICacheConfigData
        {
        }

        /// <summary>
        /// Returns true if the given object type can be converted
        /// </summary>
        /// <param name="objectType">The type of the object that the converter supports</param>
        /// <returns>Always returns true</returns>
        public override bool CanConvert(Type objectType)
        {
            return true;
        }

        /// <summary>
        /// Reads the JSON representation of the object.
        /// </summary>
        /// <param name="reader"> The Newtonsoft.Json.JsonReader to read from</param>
        /// <param name="objectType">Type of the object</param>
        /// <param name="existingValue">The existing value of object being read</param>
        /// <param name="serializer">The calling serializer</param>
        /// <returns>Returns an object that contains the data that has been converted from Json</returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Load the Json data into a JObject
            JObject jObject = JObject.Load(reader);

            // Convert the JObject to CacheConfigData
            return Create(jObject.Root);
        }

        /// <summary>
        /// Writes the JSON representation of the object. We do not need this method because we never write Json.
        /// </summary>
        /// <param name="writer">Json writer</param>
        /// <param name="value">Object to write</param>
        /// <param name="serializer">The serializer that calls this method</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Character array used to split Json field names
        /// </summary>
        private static readonly char[] delimiterChars = { '.' };

        /// <summary>
        /// Recursive method used to convert Json data into CacheConfigData (a multi layer dictionary)
        /// </summary>
        /// <param name="jObject">The Json DOM object to convert</param>
        /// <returns>Returns a CacheConfigData structure containing the converted data</returns>
        private ICacheConfigData Create(JToken jObject)
        {
            // create an empty return object
            CacheConfigData cacheConfigObject = new CacheConfigData();

            // enumerate all child nodes
            foreach (JToken data in jObject.Children())
            {
                // each child node can contain more than one elements
                foreach (var child in data.Children())
                {
                    // The Json field name is in the ParentField.ChildField format. Extract the ChildField part.
                    string[] fieldNameParts = child.Path.Split(delimiterChars);

                    // Check the element type
                    if (child.Type == JTokenType.Object)
                    {
                        // we have an object. Call ourselves recursively to process it.
                        cacheConfigObject.Add(fieldNameParts[fieldNameParts.Length - 1], Create(child.Value<JObject>()));
                    }
                    else
                    {
                        // add simple value type elements to the dictionary
                        cacheConfigObject.Add(fieldNameParts[fieldNameParts.Length - 1], child.ToObject<object>());
                    }
                }
            }

            // return CacheConfigData object
            return cacheConfigObject;
        }
    }
}
