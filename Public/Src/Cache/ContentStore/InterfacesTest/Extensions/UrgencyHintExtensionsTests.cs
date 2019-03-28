// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Extensions
{
    public class UrgencyHintExtensionsTests
    {
        [Theory]
        [InlineData(int.MinValue, UrgencyHint.Minimum)]
        [InlineData(int.MinValue / 2, UrgencyHint.Low)]
        [InlineData(0, UrgencyHint.Nominal)]
        [InlineData(int.MaxValue / 2, UrgencyHint.High)]
        [InlineData(int.MaxValue, UrgencyHint.Maximum)]
        public void ToUrgencyHint(int value, UrgencyHint hint)
        {
            Assert.Equal(hint, value.ToUrgencyHint());
        }

        [Theory]
        [InlineData(UrgencyHint.Minimum, int.MinValue)]
        [InlineData(UrgencyHint.Low, int.MinValue / 2)]
        [InlineData(UrgencyHint.Nominal, 0)]
        [InlineData(UrgencyHint.High, int.MaxValue / 2)]
        [InlineData(UrgencyHint.Maximum, int.MaxValue)]
        public void ToValue(UrgencyHint hint, int value)
        {
            Assert.Equal(value, hint.ToValue());
        }
    }
}
