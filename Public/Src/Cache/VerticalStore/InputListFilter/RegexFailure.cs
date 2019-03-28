// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.InputListFilter
{
    /// <summary>
    /// A failure due to a bad regex in the configuration
    /// </summary>
    public sealed class RegexFailure : CacheBaseFailure
    {
        private readonly string m_regex;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Failure to construct a Regex in the cache config
        /// </summary>
        /// <param name="regex">The contents of the regex</param>
        /// <param name="rootCause">The exception</param>
        public RegexFailure(string regex, Exception rootCause)
        {
            Contract.Requires(regex != null);
            Contract.Requires(rootCause != null);

            m_regex = regex;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "InputFilterList regex [{0}] failed to construct: {1}", m_regex, m_rootCause.ToString());
        }
    }
}
