// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Xunit.v3;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Attribute that is used to mark a test as requiring MTA.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class MtaTraitAttribute : Attribute, ITraitAttribute
    {
        /// <summary>
        /// The name of the trait.
        /// </summary>
        public const string MtaTrait = "UseMta";

        private readonly string m_name;
        private readonly string m_value;

        /// <summary>
        /// Creates a new instance of the <see cref="MtaTraitAttribute"/> class.
        /// </summary>
        public MtaTraitAttribute(string name = MtaTrait, string value = "true")
        {
            m_name = name;
            m_value = value;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
        {
            return new[] { new KeyValuePair<string, string>(m_name, m_value) };
        }
    }
}
