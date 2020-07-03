using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Logging.External;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.Logging.Test
{
    /// <summary>
    /// The tests in this class can be seen redundant because they contain duplicate information.
    /// But this is intentional.
    /// We don't want to change message properties by an accident because this will break Kusto ingestion.
    /// It means that in order to change the message properties that are important for Kusto ingestion
    /// two places must be changed: the code itself and these tests.
    /// </summary>
    public class NLogAdapterTests
    {
        [Fact]
        public void TestCreateLogEventInfoForOperationResult()
        {
            var operationResult = new OperationResult(
                message: "my message",
                operationName: "operation name",
                tracerName: "tracer name",
                status: OperationStatus.Failure,
                duration: TimeSpan.FromSeconds(1),
                operationKind: OperationKind.Startup,
                exception: new Exception("Message"),
                operationId: "42",
                severity: Severity.Error);

            var log = NLogAdapter.CreateLogEventInfo(operationResult);

            log.Properties.All(p => WellKnownProperties.Contains(p.Key.ToString())).Should().BeTrue();
        }

        [Fact]
        public void TestCreateLogEventInfoForOperationStarted()
        {
            var operationStarted = new OperationStarted(
                message: "my message",
                operationName: "operation name",
                tracerName: "tracer name",
                operationKind: OperationKind.Startup,
                operationId: "42",
                severity: Severity.Error);

            var log = NLogAdapter.CreateLogEventInfo(operationStarted);

            log.Properties.All(p => WellKnownProperties.Contains(p.Key.ToString())).Should().BeTrue();
        }

        private static readonly HashSet<string> WellKnownProperties = new HashSet<string>(new[]
                                                      {
                                                          "CorrelationId", "OperationComponent", "OperationName", "OperationArguments",
                                                          "OperationResult", "OperationDuration",
                                                      });
    }
}
