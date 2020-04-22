using System;
using FluentAssertions;
using Microsoft.Practices.TransientFaultHandling;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test
{
    /// <summary>
    /// There is worry that, because TransientFaultHandling.Core only builds agains net40, there will be runtime errors.
    /// </summary>
    public class TransientErrorTest : ITransientErrorDetectionStrategy
    {
        public bool IsTransient(Exception ex) => true;

        [Fact]
        public void Test()
        {
            var r = new RetryPolicy(this, retryCount: 3, retryInterval: TimeSpan.FromMilliseconds(1));
            var c = 0;
            r.ExecuteAction(() => c++ == 3 ? true : throw new Exception()).Should().BeTrue();
        }
    }
}
