// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class AdditionalNameValueParameter : IAdditionalNameValueParameter
    {
        /// <nodoc />
        public AdditionalNameValueParameter()
        { }

        /// <nodoc />
        public AdditionalNameValueParameter(IAdditionalNameValueParameter template)
        {
            Name = template.Name;
            Value = template.Value;
        }

        /// <inheritdoc/>
        public string Name { get; set; }

        /// <inheritdoc/>
        public string Value { get; set; }
    }
}
