// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using Xunit;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Interface for tests which are only enabled if requirements are met
    /// </summary>
    public interface ITestIfSupportedTraitAttribute : ITraitAttribute
    {
        /// <summary>
        /// The test requirements
        /// </summary>
        TestRequirements Requirements { get; }

        /// <summary>
        /// Whether to skip the tests
        /// </summary>
        string Skip { get; }
    }
}
