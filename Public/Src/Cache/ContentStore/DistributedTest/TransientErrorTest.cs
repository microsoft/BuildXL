using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test
{
    /// <summary>
    /// There is worry that, because TransientFaultHandling.Core only builds agains net40, there will be runtime errors.
    /// </summary>
    public class TransientErrorTest
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Test(bool usePolly)
        {
            RetryPolicyFactory.UsePolly = usePolly;
            var policy = RetryPolicyFactory.GetLinearPolicy(shouldRetry: _ => true, retries: 3, retryInterval: TimeSpan.FromMilliseconds(1));
            var c = 0;
            (await policy.ExecuteAsync(() => c++ == 3 ? Task.FromResult(true) : throw new Exception(), CancellationToken.None)).Should().BeTrue();
        }
    }
}
