// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Operations
{
    /// <nodoc/>
    public readonly struct BreakawayChildProcessComparer : IComparer<IBreakawayChildProcess>
    {
        private readonly IComparer<StringId> m_stringIdComparer;

        /// <nodoc/>
        public BreakawayChildProcessComparer(IComparer<StringId> stringIdComparer) 
        {
            m_stringIdComparer = stringIdComparer;
        }
        
        /// <inheritdoc/>
        public int Compare(IBreakawayChildProcess x, IBreakawayChildProcess y)
        {
            var result = m_stringIdComparer.Compare(x.ProcessName.StringId, y.ProcessName.StringId);

            if (result != 0)
            {
                return result;
            }

            result = x.RequiredArguments.CompareTo(y.RequiredArguments);

            if (result != 0)
            {
                return result;
            }

            return x.RequiredArgumentsIgnoreCase.CompareTo(y.RequiredArgumentsIgnoreCase);
        }
    }
}
