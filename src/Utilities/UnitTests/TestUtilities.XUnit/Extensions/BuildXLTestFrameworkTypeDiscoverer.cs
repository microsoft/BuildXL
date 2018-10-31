// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom Xunit type discoverer that allows to use BuildXL-specific logic during test execution.
    /// </summary>
    public class BuildXLTestFrameworkTypeDiscoverer : ITestFrameworkTypeDiscoverer
    {
        /// <nodoc />
        public Type GetTestFrameworkType(IAttributeInfo attribute) => typeof(BuildXLTestFramework);
    }
}
