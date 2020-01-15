// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.VerticalAggregator.Test
{
    /// <summary>
    /// Specific test failure used to posion cache API's for testing disconnect and error
    /// cases.
    /// </summary>
    internal class TestInducedFailure : CacheBaseFailure
    {
        private readonly string m_method;

        internal TestInducedFailure(string method)
        {
            m_method = method;
        }

        public override string Describe()
        {
            return "Test induced failure on method " + m_method;
        }
    }
}
