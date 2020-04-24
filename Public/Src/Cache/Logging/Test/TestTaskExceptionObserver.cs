using System;
using System.Collections.Generic;
using BuildXL.Cache.Logging.External;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.Logging.Test
{
    public class TestTaskExceptionObserver
    {
        [Fact]
        public void UnkownExceptionType()
        {
            var knownException = new Exception("KNOWN");
            var unknownException = new Exception("RANDOM");
            var exceptionObserver = new TaskExceptionObserver();

            exceptionObserver.AddException(knownException);
            exceptionObserver.IsWellKnownException(knownException).Should().BeTrue();
            exceptionObserver.IsWellKnownException(unknownException).Should().BeFalse();
        }

        [Fact]
        public void WellKnownAggregateException()
        {
            var testMsg = "TEST_MSG";
            var innerException = new Exception(testMsg);
            var innerExceptions = new List<Exception>() {innerException};
            var aggException = new AggregateException(innerExceptions);
            var exceptionObserver = new TaskExceptionObserver();

            exceptionObserver.AddException(aggException);

            exceptionObserver.IsWellKnownException(aggException).Should().BeTrue();
            exceptionObserver.IsWellKnownException(innerException).Should().BeTrue();
        }

        [Fact]
        public void MultipleWellKnownInnerExceptions()
        {
            var innerExceptions = new List<Exception>();
            for (var i = 0; i<3; i++)
            {
                innerExceptions.Add(new Exception($"TEST_{i}"));
            }

            var aggException = new AggregateException(innerExceptions);
            var exceptionObserver = new TaskExceptionObserver();
            exceptionObserver.AddException(aggException);

            exceptionObserver.IsWellKnownException(aggException).Should().BeTrue();
            foreach (var innerException in innerExceptions)
            {
                exceptionObserver.IsWellKnownException(innerException).Should().BeTrue();
            }
        }

        [Fact]
        public void RedundantWellKnownExceptions()
        {
            var exception = new Exception("REDUNDANT");
            var exceptionObserver = new TaskExceptionObserver();
            exceptionObserver.AddException(exception);
            exceptionObserver.AddException(exception);

            exceptionObserver.IsWellKnownException(exception).Should().BeTrue();
        }
    }
}
