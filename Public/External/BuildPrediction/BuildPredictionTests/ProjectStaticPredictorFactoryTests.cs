// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Build.Prediction.Tests
{
    public class ProjectStaticPredictorFactoryTests
    {
        [Fact]
        public void CreateStandardPredictorsSucceeds()
        {
            IReadOnlyCollection<IProjectStaticPredictor> predictors =
                ProjectStaticPredictorFactory.CreateStandardPredictors(TestHelpers.GetAssemblyLocation());
            Assert.True(predictors.Count > 0, "Reflection should have found instances of IProjectStaticPredictor in Microsoft.Build.Prediction.dll");
        }
    }
}
