// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Tracing
{
    public class ContextTests
    {
        [Fact]
        public void Info()
        {
            var context = new Context(NullLogger.Instance);
            context.Info("Referencing this method", component: nameof(ContextTests));
        }

        [Fact]
        public async Task NestedContextHasTheSameIdAsParent()
        {
            try
            {
                Context.UseHierarchicalIds = true;
                int attempCount = 100;

                var parent = new Context(NullLogger.Instance);
                int childCount = 20;

                var nested1 = parent.CreateNested(componentName: "MyComponent");
                Assert.Contains(parent.TraceId, nested1.TraceId);
                
                for (int i = 0; i < attempCount; i++)
                {
                    var childContexts = await Task.WhenAll(
                        Enumerable
                            .Range(1, childCount)
                            .Select(n => Task.Run(() => parent.CreateNested(componentName: "MyComponent"))));

                    var uniqueContexts = childContexts.Select(c => c.TraceId).ToHashSet();
                    // All ids must be unique.
                    Assert.Equal(childContexts.Length, uniqueContexts.Count);
                    
                    foreach (var c in childContexts)
                    {
                        // Every child contexts should have a parent Id as part of their TraceIds
                        Assert.Contains(parent.TraceId, c.TraceId);
                    }
                }
            }
            finally
            {
                Context.UseHierarchicalIds = false;
            }
        }
    }
}
