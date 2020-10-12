using System;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Redis
{
    public class OperationThrottleTests
    {
        [Fact]
        public void OperationCountLimitTest()
        {
            var clock = new MemoryClock();
            clock.UtcNow = DateTime.Today;
            var operationLimit = 2;
            var span = TimeSpan.FromSeconds(1);
            var halfSpan = new TimeSpan(span.Ticks / 2);

            var limit = new OperationThrottle(span, operationLimit, clock);

            for (var iteration = 0; iteration < 3; iteration++)
            {
                // Use all available operations
                for (var i = 0; i < operationLimit; i++)
                {
                    limit.CheckAndRegisterOperation().ShouldBeSuccess();
                }

                // Doing more operations should fail.
                limit.CheckAndRegisterOperation().ShouldBeError();

                // ... even if time passes, as long as we don't cross the span limit.
                clock.Increment(halfSpan);
                limit.CheckAndRegisterOperation().ShouldBeError();

                // Once the span has been completed, operations should succeed again.
                clock.Increment(halfSpan);
            }
        }
    }
}
