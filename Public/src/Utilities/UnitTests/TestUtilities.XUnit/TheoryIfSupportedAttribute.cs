// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;
using Xunit;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom theory attribute that allows dynamically skipping tests based on what operations are supported
    /// </summary>
    /// <remarks>
    /// Any test using this is non-deterministic with respect to caching since pip fingerprints don't take this dynamic
    /// skipping into account. Ideally we wouldn't do things like this in tests.
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TheoryIfSupportedAttribute : TheoryAttribute
    {
        /// <nodoc/>
        public TheoryIfSupportedAttribute(bool requiresAdmin = false, bool requiresSymlinkPermission = false, bool requiresWindowsBasedOperatingSystem = false, bool requiresUnixBasedOperatingSystem = false)
        {
            // Use same logic and underlying static state to determine wheter to Skip tests
            Skip = new FactIfSupportedAttribute(
                requiresAdmin: requiresAdmin,
                requiresSymlinkPermission: requiresSymlinkPermission,
                requiresWindowsBasedOperatingSystem: requiresWindowsBasedOperatingSystem,
                requiresUnixBasedOperatingSystem: requiresUnixBasedOperatingSystem
            ).Skip;
        }
    }
}
