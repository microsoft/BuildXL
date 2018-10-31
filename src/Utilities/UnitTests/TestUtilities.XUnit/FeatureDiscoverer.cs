// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit
{
    /// <summary>
    /// This class discovers all of the tests and test classes that have
    /// applied the Feature attribute
    /// </summary>
    public class FeatureDiscoverer : ITraitDiscoverer
    {
        /// <summary>
        /// Gets the trait values from the Feature attribute.
        /// </summary>
        /// <param name="traitAttribute">The trait attribute containing the trait values.</param>
        /// <returns>The trait values.</returns>
        public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
        {
            var ctorArgs = traitAttribute.GetConstructorArguments().ToList();
            yield return new KeyValuePair<string, string>(Features.Feature, ctorArgs[0].ToString());
        }
    }
}
