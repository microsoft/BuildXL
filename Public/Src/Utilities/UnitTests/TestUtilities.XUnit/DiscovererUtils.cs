// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Utilities for trait discoverer.
    /// </summary>
    public class DiscovererUtils
    {
        /// <summary>
        /// Assembly name.
        /// </summary>
        public const string AssemblyName =
            nameof(Test)
            + "." + nameof(Test.BuildXL)
            + "." + nameof(Test.BuildXL.TestUtilities)
            + "." + nameof(Test.BuildXL.TestUtilities.Xunit);
    }
}
