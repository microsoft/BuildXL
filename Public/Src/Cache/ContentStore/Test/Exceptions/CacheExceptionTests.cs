// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Exceptions;
using Xunit;

namespace ContentStoreTest.Exceptions
{
    [Trait("Category", "QTestSkip")]
    public class CacheExceptionTests : CacheExceptionBaseTests
    {
        protected override CacheException Construct()
        {
            return new CacheException();
        }
    }
}
