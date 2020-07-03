// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
