// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    public class DefaultPriorityAssignerTests
    {
        private static readonly CopyReason[] CopyReasons = Enum
                .GetValues(typeof(CopyReason))
                .Cast<CopyReason>()
                .OrderBy(reason => (int)reason)
                .ToArray();

        private static readonly ProactiveCopyLocationSource[] ProactiveCopyLocationSources = Enum
                .GetValues(typeof(ProactiveCopyLocationSource))
                .Cast<ProactiveCopyLocationSource>()
                .OrderBy(reason => (int)reason)
                .ToArray();

        [Fact]
        public void PriorityIsBoundedAsExpected()
        {
            foreach (var copyReason in CopyReasons)
            {
                for (var attempt = 0; attempt <= DefaultPriorityAssigner.MaxAttempt; attempt++)
                {
                    foreach (var proactiveCopyLocationSource in ProactiveCopyLocationSources)
                    {
                        var priority = DefaultPriorityAssigner.GetPriority(copyReason, attempt, proactiveCopyLocationSource);
                        priority.Should().BeInRange(0, DefaultPriorityAssigner.MaxPriorityStatic);
                    }
                }
            }
        }

        [Fact]
        public void HigherReasonHasHigherPriority()
        {
            for (var i = 0; i < CopyReasons.Length - 1; i++)
            {
                var current = DefaultPriorityAssigner.GetPriority(CopyReasons[i], 0, ProactiveCopyLocationSource.None);
                var next = DefaultPriorityAssigner.GetPriority(CopyReasons[i + 1], 0, ProactiveCopyLocationSource.None);
                Assert.True(current < next);
            }
        }

        [Fact]
        public void LowerAttemptHasHigherPriority()
        {
            for (var i = 0; i < CopyReasons.Length; i++)
            {
                for (var attempt = 0; attempt < DefaultPriorityAssigner.MaxAttempt; attempt++)
                {
                    var current = DefaultPriorityAssigner.GetPriority(CopyReasons[i], attempt, ProactiveCopyLocationSource.None);
                    var next = DefaultPriorityAssigner.GetPriority(CopyReasons[i], attempt + 1, ProactiveCopyLocationSource.None);
                    Assert.True(next < current);
                }
            }
        }

        [Fact]
        public void MoreSpecificLocationHasHigherPriority()
        {
            for (var i = 0; i < CopyReasons.Length; i++)
            {
                for (var attempt = 0; attempt < DefaultPriorityAssigner.MaxAttempt; attempt++)
                {
                    var none = DefaultPriorityAssigner.GetPriority(CopyReasons[i], attempt, ProactiveCopyLocationSource.None);
                    var random = DefaultPriorityAssigner.GetPriority(CopyReasons[i], attempt, ProactiveCopyLocationSource.Random);
                    var designated = DefaultPriorityAssigner.GetPriority(CopyReasons[i], attempt, ProactiveCopyLocationSource.DesignatedLocation);

                    Assert.True(none < random);
                    Assert.True(random < designated);
                }
            }
        }

        [Fact]
        public void MostSpecificLocationStillLessThanNextReason()
        {
            for (var i = 0; i < CopyReasons.Length - 1; i++)
            {
                for (var attempt = 0; attempt < DefaultPriorityAssigner.MaxAttempt; attempt++)
                {
                    var current = DefaultPriorityAssigner.GetPriority(CopyReasons[i], attempt, ProactiveCopyLocationSource.DesignatedLocation);
                    var next = DefaultPriorityAssigner.GetPriority(CopyReasons[i + 1], attempt, ProactiveCopyLocationSource.None);

                    Assert.True(current < next);
                }
            }
        }
    }
}
