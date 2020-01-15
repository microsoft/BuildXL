// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Custom Xunit type discoverer that allows to use BuildXL-specific logic during test execution.
    /// </summary>
    public class TestFrameworkTypeDiscoverer : ITestFrameworkTypeDiscoverer
    {
        /// <nodoc />
        public Type GetTestFrameworkType(IAttributeInfo attribute) => typeof(TestFramework);
    }
}
