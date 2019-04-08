// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace ResGen.Lite
{
    /// <summary>
    /// Encapsulates the data extracted from ResX/ResW files.
    /// </summary>
    public class ResourceDataEntry
    {
        /// <summary>
        /// The name of the entry
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The string value of the entry
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Optional comment for the entry
        /// </summary>
        public string Comment { get; set; }
    }
}
