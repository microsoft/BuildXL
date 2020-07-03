// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
            context.Info("Referencing this method");
        }
    }
}
