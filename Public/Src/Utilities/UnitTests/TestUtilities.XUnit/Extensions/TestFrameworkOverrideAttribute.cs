// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
