// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Utils;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Utils
{
    public class ConcurrentDictionaryExtensionsTests : TestWithOutput
    {
        public ConcurrentDictionaryExtensionsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void AddOrGetReturnsFirstResult()
        {
            var cd = new ConcurrentDictionary<int, int>();
            var result = ConcurrentDictionaryExtensions.GetOrAdd(cd, 42,
                (key, args) =>
                {
                    return 42;
                }, factoryArgument: string.Empty);
            result.Should().Be(42);
        }

        [Fact]
        public async Task CallbackIsCalled()
        {
            int attemptCount = 100;

            for (int i = 0; i < attemptCount; i++)
            {
                await checkConcurrentGetOrAdd();
            }
            
            async Task checkConcurrentGetOrAdd()
            {
                var cd = new ConcurrentDictionary<int, int>();
                const int key = 42;
                bool factoryIsCalled = false;
                Task<bool> addTask = Task.Run(() => cd.TryAdd(key, 1));
                Task<int> addOrUpdateTask = Task.Run(
                    () =>
                    {
                        return ConcurrentDictionaryExtensions.GetOrAdd(
                            cd,
                            key,
                            (k, state) =>
                            {
                                factoryIsCalled = true;
                                return 2;
                            },
                            factoryArgument: string.Empty);
                    });

                await Task.WhenAll(addTask, addOrUpdateTask);

                var addOrUpdateResult = await addOrUpdateTask;

                if (factoryIsCalled)
                {
                    // If factory was called, the result can still be obtained from the TryAdd call
                    addOrUpdateResult.Should().BeInRange(1, 2);
                }
                else
                {
                    addOrUpdateResult.Should().Be(1);
                }

                // Printing the outcome just for debugging purposes.
                if (factoryIsCalled && addOrUpdateResult == 1)
                {
                    Output.WriteLine("Factory was called, but the race was lost.");
                }

                if (factoryIsCalled && addOrUpdateResult == 2)
                {
                    Output.WriteLine("Factory was called, and the race was won.");
                }

                if (!factoryIsCalled)
                {
                    Output.WriteLine("Factory was not called");
                }    
            }    
        }
    }
}
