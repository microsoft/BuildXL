// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using Xunit;

namespace BuildXL.Cache.Logging.Test
{
    public class MdmOperationLoggerTests
    {
        [Fact]
        public void MetricOperationsAreNotFailing()
        {
            // We can't check if the metric is uploaded successfully, because a special agent should be running on the machine,
            // but we can check that all the required dependencies are available.
            // And this test just checks that all the managed and native dlls are presented and all the types can be loaded successfully.
            var context = new Context(new Logger());
            var logger = MdmOperationLogger.Create(context, "CloudBuildCBTest", new List<DefaultDimension>());
            logger.OperationFinished(new OperationResult("CustomMessage", "Test OperationName", "TestTracer", OperationStatus.Success, TimeSpan.FromSeconds(42), OperationKind.None, exception: null, operationId: "42", severity: Severity.Debug));
        }
    }
}
