// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// An additional name-value parameter for the Rush plugin.
    /// </summary>
    public interface IAdditionalNameValueParameter
    {
        /// <nodoc/>
        string Name { get; }

        /// <nodoc/>
        string Value { get; }
    }
}
