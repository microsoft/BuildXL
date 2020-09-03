using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tasks;
using Xunit;

namespace BuildXL.Cache.ContentStore.Test.Utils
{
    public class GateExtensionsTests
    {
        [Fact]
        public async Task DeduplicateBasicScenario()
        {
            var gate = TaskUtilities.CreateMutex();

            var winningLatch = new SemaphoreSlim(0, 1);
            var losingLatch = new SemaphoreSlim(0, 1);
            var completionLatch = new SemaphoreSlim(0, 1);

            var win = winningTask();
            var lose = losingTask();
            await Task.WhenAll(win, lose);

            Contract.Assert(1 == await win);
            Contract.Assert(4 == await lose);

            async Task<int> winningTask()
            {
                await Task.Yield();
                await winningLatch.WaitAsync();
                return await gate.DeduplicatedOperationAsync(async (w, c) =>
                {
                    losingLatch.Release();
                    await completionLatch.WaitAsync();
                    return 1;
                }, (w, c) =>
                {
                    Contract.Assert(false, "This should never happen");
                    return Task.FromResult(2);
                });
            }

            async Task<int> losingTask()
            {
                await Task.Yield();
                winningLatch.Release();
                await losingLatch.WaitAsync();
                return await gate.DeduplicatedOperationAsync((w, c) =>
                {
                    Contract.Assert(false, "This should never happen");
                    return Task.FromResult(3);
                }, (w, c) => {
                    completionLatch.Release();
                    return Task.FromResult(4);
                });
            }
        }
    }
}
