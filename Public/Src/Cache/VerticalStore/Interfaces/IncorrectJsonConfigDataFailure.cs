// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Represents a failure where the Json cache configuration data is either incorrect or it is missing some required fields.
    /// </summary>
    public class IncorrectJsonConfigDataFailure : CacheBaseFailure
    {
        private readonly object[] m_args;
        private readonly string m_format;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="format">Format string</param>
        /// <param name="args">Arguments used with string format</param>
        public IncorrectJsonConfigDataFailure(string format, params object[] args)
        {
            Contract.Requires(format != null);

            m_format = format;
            m_args = args;
        }

        /// <summary>
        /// Returns a string that describes the failure
        /// </summary>
        /// <returns>String that describes the failure</returns>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, m_format, m_args);
        }
    }
}
