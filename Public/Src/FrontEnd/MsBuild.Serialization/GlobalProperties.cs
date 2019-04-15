// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildXL.FrontEnd.MsBuild.Serialization
{
    /// <summary>
    /// MsBuild global properties: a readonly case insensitive string dictionary
    /// </summary>
    public class GlobalProperties : ReadOnlyDictionary<string, string>
    {
        /// <nodoc/>
        public static GlobalProperties Empty = new GlobalProperties();
        
        private GlobalProperties() : base(new Dictionary<string, string>())
        {
        }

        /// <nodoc/>
        public GlobalProperties(IEnumerable<KeyValuePair<string, string>> dictionary): base(dictionary.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase))
        { }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"[{string.Join(";", Keys.Select(key => $"{key.ToUpperInvariant()}={this[key]}"))}]";
        }
    }
}
