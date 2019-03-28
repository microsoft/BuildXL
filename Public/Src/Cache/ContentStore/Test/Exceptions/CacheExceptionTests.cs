// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
