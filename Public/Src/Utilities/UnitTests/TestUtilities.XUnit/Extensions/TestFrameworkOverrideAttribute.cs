// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.XUnit.Extensions
{
    /// <summary>
    /// Attribute that forces Xunit to use <see cref="TestFrameworkTypeDiscoverer"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    [TestFrameworkDiscoverer("Test.BuildXL.TestUtilities.XUnit.Extensions.TestFrameworkTypeDiscoverer", "Test.BuildXL.TestUtilities.XUnit")]
    public sealed class TestFrameworkOverrideAttribute : Attribute, ITestFrameworkAttribute
    {
    }
}
