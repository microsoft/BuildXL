// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Collections;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <nodoc/>
    public class SortedReadOnlyArrayTests
    {
        [Fact]
        public void IsValidTests()
        {
            var testArray = new SortedReadOnlyArray<string, StringComparer>();
            Assert.False(testArray.IsValid);
            Assert.False(testArray.BaseArray.IsValid);

            testArray = SortedReadOnlyArray<string, StringComparer>.CloneAndSort(new string[0], StringComparer.Ordinal);
            Assert.True(testArray.IsValid);
            Assert.True(testArray.BaseArray.IsValid);

            testArray = CollectionUtilities.EmptySortedReadOnlyArray<string, StringComparer>(StringComparer.Ordinal);
            Assert.True(testArray.IsValid);
            Assert.True(testArray.BaseArray.IsValid);

            bool exceptionThrown = false;
            try
            {
                testArray = SortedReadOnlyArray<string, StringComparer>.CloneAndSort(null, StringComparer.Ordinal);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                exceptionThrown = true;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            Assert.True(exceptionThrown);            
        }
    }
}
