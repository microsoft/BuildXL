// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.Host.Configuration;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.Host.Test
{
    public class TestConfigurationValidation
    {
        [Fact]
        public void DisabledIsValid()
        {
            var errors = DistributedContentSettings.CreateDisabled().Validate();
            errors.Should().BeEmpty();
        }

        [Fact]
        public void EnumsAreChecked()
        {
            var settings = DistributedContentSettings.CreateDisabled();
            settings.ProactiveCopyMode = "Some invalid string";

            var errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(ProactiveCopyMode));
        }

        [Fact]
        public void PositivesAreChecked()
        {
            var settings = DistributedContentSettings.CreateDisabled();

            settings.ProactiveCopyLocationsThreshold = -1;
            var errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.ProactiveCopyLocationsThreshold));

            settings.ProactiveCopyLocationsThreshold = 0;
            errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.ProactiveCopyLocationsThreshold));
        }

        [Fact]
        public void PositiveOrZerosAreChecked()
        {
            var settings = DistributedContentSettings.CreateDisabled();

            settings.ProactiveReplicationDelaySeconds = -1;
            var errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.ProactiveReplicationDelaySeconds));

            settings.ProactiveReplicationDelaySeconds = 0;
            errors = settings.Validate();
            errors.Should().BeEmpty();
        }

        [Fact]
        public void RangeExlusivesAreChecked()
        {
            var settings = DistributedContentSettings.CreateDisabled();

            settings.EvictionRemovalFraction = -1;
            var errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.EvictionRemovalFraction));

            settings.EvictionRemovalFraction = 1.1F;
            errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.EvictionRemovalFraction));


            settings.EvictionRemovalFraction = 1;
            errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.EvictionRemovalFraction));

            settings.EvictionRemovalFraction = 0;
            errors = settings.Validate();
            errors.Should().BeEmpty();
        }

        [Fact]
        public void RangesAreChecked()
        {
            var settings = DistributedContentSettings.CreateDisabled();

            settings.ContentLocationDatabaseFlushPreservePercentInMemory = -1;
            var errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.ContentLocationDatabaseFlushPreservePercentInMemory));

            settings.ContentLocationDatabaseFlushPreservePercentInMemory = 1.1F;
            errors = settings.Validate();
            errors.Count.Should().Be(1);
            errors[0].Should().Contain(nameof(settings.ContentLocationDatabaseFlushPreservePercentInMemory));

            settings.ContentLocationDatabaseFlushPreservePercentInMemory = 0;
            errors = settings.Validate();
            errors.Should().BeEmpty();

            settings.ContentLocationDatabaseFlushPreservePercentInMemory = 1;
            errors = settings.Validate();
            errors.Should().BeEmpty();
        }
    }
}
