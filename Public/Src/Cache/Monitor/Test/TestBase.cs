// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.InterfacesTest;
using Xunit.Abstractions;

namespace BuildXL.Cache.Monitor.Test
{
    public abstract class TestBase : TestWithOutput
    {
        protected TestBase(ITestOutputHelper output) : base(output)
        {
        }
    }
}
