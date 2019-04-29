// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Allows aggregation of multiple Failures into a single response.
    /// </summary>
    public class AggregateFailure : CacheBaseFailure
    {
        private readonly List<Failure> m_failures;

        /// <nodoc/>
        public AggregateFailure(params Failure[] failures)
        {
            Contract.Requires(failures != null);

            m_failures = new List<Failure>(failures);
        }

        /// <summary>
        /// Count of m_failures
        /// </summary>
        public int Count => m_failures.Count;

        /// <summary>
        /// Adds an additional failure
        /// </summary>
        /// <param name="failure">The failure to add</param>
        public void AddFailure(Failure failure)
        {
            m_failures.Add(failure);
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Aggregation of the following m_failures occurred:");

            foreach (Failure f in m_failures)
            {
                sb.AppendLine(f.Describe());
            }

            return sb.ToString();
        }
    }
}
