// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Apply this attribute to your test method to specify a feature category.
    /// The attribute is left unsealed so that derived test classes inheriting test cases
    /// will also inherit associated FeatureAttributes.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [SuppressMessage("Microsoft.Performance", "CA1813:AvoidUnsealedAttributes")]
    [TraitDiscoverer("FeatureDiscoverer", "Test.BuildXL.TestUtilities.Xunit")]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class FeatureAttribute : Attribute, ITraitAttribute
    {
        /// <summary>
        /// Apply this attribute to your test method to specify a feature category.
        /// </summary>
        public FeatureAttribute(string feature) { }
    }
}
