// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// MsBuild global properties: a readonly case insensitive string dictionary
    /// </summary>
    /// <remarks>
    /// MSBuild expects property values to be non-null. Any null value passed at construction time will be swaped by an empty string
    /// </remarks>
    public class GlobalProperties : ReadOnlyDictionary<string, string>
    {
        /// <nodoc/>
        public static GlobalProperties Empty = new GlobalProperties();
        
        private GlobalProperties() : base(new Dictionary<string, string>())
        {
        }

        /// <nodoc/>
        public GlobalProperties(IEnumerable<KeyValuePair<string, string>> dictionary): 
            base(dictionary.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        { }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[{string.Join(";", Keys.Select(key => $"{key.ToUpperInvariant()}={this[key]}"))}]";
        }
    }
}
