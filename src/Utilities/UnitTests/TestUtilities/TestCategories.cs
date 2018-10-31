// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// String constants for the [TestCategory] attribute.
    /// </summary>
    public static class TestCategories
    {
        /// <summary>
        /// The Stress category contains tests which use large input sizes, iteration counts, etc.
        /// as a means to measure performance or more rigorously exercise a non-deterministic component.
        /// Tests that run for a quarter second or longer should typically be in this category.
        /// </summary>
        public const string Stress = "Stress";

        /// <summary>
        /// Tests which can only run with elevated permissions.
        /// </summary>
        public const string Admin = "Admin";
    }
}
