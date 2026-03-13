// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Xunit.v3;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Apply this attribute to your test method to specify a feature category.
    /// In v3, this directly implements ITraitAttribute to register the "Feature" trait.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [SuppressMessage("Microsoft.Performance", "CA1813:AvoidUnsealedAttributes")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class FeatureAttribute : Attribute, ITraitAttribute
    {
        private readonly string m_feature;

        /// <summary>
        /// Apply this attribute to your test method to specify a feature category.
        /// </summary>
        public FeatureAttribute(string feature)
        {
            m_feature = feature;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
        {
            return new[] { new KeyValuePair<string, string>("Feature", m_feature) };
        }
    }
}
