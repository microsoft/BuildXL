// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL
{
    public class StatisticsEventListenerTests
    {
        [Fact]
        public void FinalStatisticsCollection()
        {
            XAssert.IsTrue(StatisticsEventListener.ShouldSendStatisticToFinalStatistics("Category.Some_Name_With_Underscores"));
            XAssert.IsTrue(StatisticsEventListener.ShouldSendStatisticToFinalStatistics("FileContentTable.NumContentMismatch"));
            XAssert.IsFalse(StatisticsEventListener.ShouldSendStatisticToFinalStatistics("PipCaching_HistoricWeakFingerprintLoadedCount"));
            XAssert.IsFalse(StatisticsEventListener.ShouldSendStatisticToFinalStatistics("PipCaching_HistoricWeakFingerprintLoaded_Count"));
        }
    }
}
