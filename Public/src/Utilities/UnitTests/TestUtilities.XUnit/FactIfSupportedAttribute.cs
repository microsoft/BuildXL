// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Custom fact attribute that allows dynamically skipping tests based on what operations are supported
    /// </summary>
    /// <remarks>
    /// Any test using this is non-deterministic with respect to caching since pip fingerprints don't take this dynamic
    /// skipping into account. Ideally we wouldn't do things like this in tests.
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1019:DefineAccessorsForAttributeArguments")]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactIfSupportedAttribute : global::Xunit.FactAttribute
    {
        // Cache these at the test process invocation level
        private static bool? s_isElevated;

        /// <nodoc/>
        public FactIfSupportedAttribute(bool requiresAdmin = false, bool requiresSymlinkPermission = false, bool requiresWindowsBasedOperatingSystem = false, bool requiresUnixBasedOperatingSystem = false)
        {
            if (Skip != null)
            {
                // If skip is specified, do nothing because the test will be skipped anyway.
                return;
            }

            if (requiresAdmin || requiresSymlinkPermission)
            {
                if (!s_isElevated.HasValue)
                {
                    s_isElevated = global::BuildXL.Utilities.CurrentProcess.IsElevated;
                }

                if (requiresAdmin && !s_isElevated.Value)
                {
                    Skip = "Test must be run elevated!";
                    return;
                }

                if (requiresSymlinkPermission)
                {
                    if (!s_isElevated.Value && !OperatingSystemHelper.IsUnixOS)
                    {
                        Skip = "Test must be run elevated!";
                        return;
                    }
                }
            }

            if (requiresWindowsBasedOperatingSystem)
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    Skip = "Test must be run on the CLR on Windows based operating systems!";
                    return;
                }
            }

            if (requiresUnixBasedOperatingSystem)
            {
                if (!OperatingSystemHelper.IsUnixOS)
                {
                    Skip = "Test must be run on the CoreCLR on Unix based operating systems!";
                    return;
                }
            }
        }
    }
}
