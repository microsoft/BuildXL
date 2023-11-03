// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{
    /// <summary>
    /// The class is used to represents the corresponding property and its value for a given pipsemistablehash.
    /// </summary>
    public record PipSpecificPropertyAndValue(PipSpecificPropertiesConfig.PipSpecificProperty PropertyName, long PipSemiStableHash, string PropertyValue)
    {
        /// <nodoc />
        public PipSpecificPropertyAndValue() : this(default, default, default) { }
    }

}

