// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Operations
{
    /// <nodoc/>
    public readonly struct RegexDescriptorComparer : IComparer<RegexDescriptor>
    {
        private readonly IComparer<StringId> m_stringIdComparer;

        /// <nodoc/>
        public RegexDescriptorComparer(IComparer<StringId> stringIdComparer) 
        {
            m_stringIdComparer = stringIdComparer;
        }
        
        /// <inheritdoc/>
        public int Compare(RegexDescriptor x, RegexDescriptor y)
        {
            var result = m_stringIdComparer.Compare(x.Pattern, y.Pattern);

            if (result != 0)
            {
                return result;
            }

            return x.Options - y.Options;
        }
    }
}
