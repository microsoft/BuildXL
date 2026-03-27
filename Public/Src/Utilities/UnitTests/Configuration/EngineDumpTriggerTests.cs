// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class EngineDumpTriggerTests
    {
        [Theory]
        [InlineData("8000mb", EngineDumpTriggerKind.MemoryMb, 8000)]
        [InlineData("1mb", EngineDumpTriggerKind.MemoryMb, 1)]
        [InlineData("100MB", EngineDumpTriggerKind.MemoryMb, 100)]
        [InlineData("32000Mb", EngineDumpTriggerKind.MemoryMb, 32000)]
        public void TryParseValidMemory(string input, EngineDumpTriggerKind expectedKind, int expectedValue)
        {
            Assert.True(EngineDumpTrigger.TryParse(input, out var trigger));
            Assert.Equal(expectedKind, trigger.Kind);
            Assert.Equal(expectedValue, trigger.Value);
            Assert.True(trigger.IsEnabled);
        }

        [Theory]
        [InlineData("600s", EngineDumpTriggerKind.TimeSec, 600)]
        [InlineData("1s", EngineDumpTriggerKind.TimeSec, 1)]
        [InlineData("3600S", EngineDumpTriggerKind.TimeSec, 3600)]
        public void TryParseValidTime(string input, EngineDumpTriggerKind expectedKind, int expectedValue)
        {
            Assert.True(EngineDumpTrigger.TryParse(input, out var trigger));
            Assert.Equal(expectedKind, trigger.Kind);
            Assert.Equal(expectedValue, trigger.Value);
            Assert.True(trigger.IsEnabled);
        }

        [Theory]
        [InlineData("50pct", EngineDumpTriggerKind.BuildPercentage, 50)]
        [InlineData("1pct", EngineDumpTriggerKind.BuildPercentage, 1)]
        [InlineData("100pct", EngineDumpTriggerKind.BuildPercentage, 100)]
        [InlineData("50PCT", EngineDumpTriggerKind.BuildPercentage, 50)]
        [InlineData("75percent", EngineDumpTriggerKind.BuildPercentage, 75)]
        [InlineData("25PERCENT", EngineDumpTriggerKind.BuildPercentage, 25)]
        public void TryParseValidPercentage(string input, EngineDumpTriggerKind expectedKind, int expectedValue)
        {
            Assert.True(EngineDumpTrigger.TryParse(input, out var trigger));
            Assert.Equal(expectedKind, trigger.Kind);
            Assert.Equal(expectedValue, trigger.Value);
            Assert.True(trigger.IsEnabled);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("8000")]
        [InlineData("mb")]
        [InlineData("s")]
        [InlineData("%")]
        [InlineData("pct")]
        [InlineData("percent")]
        [InlineData("0mb")]
        [InlineData("0s")]
        [InlineData("0pct")]
        [InlineData("-1mb")]
        [InlineData("-5s")]
        [InlineData("101pct")]
        [InlineData("abc")]
        [InlineData("50.5pct")]
        [InlineData("100.0mb")]
        public void TryParseInvalid(string input)
        {
            Assert.False(EngineDumpTrigger.TryParse(input, out var trigger));
            Assert.Equal(EngineDumpTriggerKind.None, trigger.Kind);
            Assert.False(trigger.IsEnabled);
        }

        [Fact]
        public void DisabledIsNotEnabled()
        {
            var trigger = EngineDumpTrigger.Disabled;
            Assert.False(trigger.IsEnabled);
            Assert.Equal(EngineDumpTriggerKind.None, trigger.Kind);
            Assert.Equal(0, trigger.Value);
        }

        [Fact]
        public void ToStringRoundTrips()
        {
            EngineDumpTrigger.TryParse("8000mb", out var memory);
            Assert.Equal("8000mb", memory.ToString());

            EngineDumpTrigger.TryParse("600s", out var time);
            Assert.Equal("600s", time.ToString());

            EngineDumpTrigger.TryParse("50pct", out var percentage);
            Assert.Equal("50pct", percentage.ToString());

            Assert.Equal("disabled", EngineDumpTrigger.Disabled.ToString());
        }

        [Fact]
        public void Equality()
        {
            EngineDumpTrigger.TryParse("8000mb", out var a);
            EngineDumpTrigger.TryParse("8000mb", out var b);
            EngineDumpTrigger.TryParse("600s", out var c);

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.NotEqual(a, c);
            Assert.True(a != c);
            Assert.NotEqual(a, EngineDumpTrigger.Disabled);
        }

        [Theory]
        [InlineData(" 8000mb ", EngineDumpTriggerKind.MemoryMb, 8000)]
        [InlineData("  600s  ", EngineDumpTriggerKind.TimeSec, 600)]
        [InlineData(" 50pct ", EngineDumpTriggerKind.BuildPercentage, 50)]
        public void TryParseTrimsWhitespace(string input, EngineDumpTriggerKind expectedKind, int expectedValue)
        {
            Assert.True(EngineDumpTrigger.TryParse(input, out var trigger));
            Assert.Equal(expectedKind, trigger.Kind);
            Assert.Equal(expectedValue, trigger.Value);
        }

        [Theory]
        [InlineData("8000mb", "process memory exceeded 8000 MB")]
        [InlineData("600s", "600 seconds elapsed since execution start")]
        [InlineData("50pct", "build reached 50% completion")]
        public void TriggerReasonDescribesKind(string input, string expectedReason)
        {
            EngineDumpTrigger.TryParse(input, out var trigger);
            Assert.Equal(expectedReason, trigger.TriggerReason);
        }

        [Fact]
        public void TriggerReasonDisabledReturnsUnknown()
        {
            Assert.Equal("unknown", EngineDumpTrigger.Disabled.TriggerReason);
        }
    }
}
