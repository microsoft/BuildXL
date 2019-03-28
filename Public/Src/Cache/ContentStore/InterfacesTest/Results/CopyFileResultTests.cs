// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class CopyFileResultTests : ResultTests<CopyFileResult>
    {
        protected override CopyFileResult CreateFrom(Exception exception)
        {
            return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, exception);
        }

        protected override CopyFileResult CreateFrom(string errorMessage)
        {
            return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, errorMessage);
        }

        protected override CopyFileResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            const CopyFileResult.ResultCode code = CopyFileResult.ResultCode.SourcePathError;
            var other = new BoolResult("error");
            Assert.Equal(code, new CopyFileResult(code, other, "message").Code);
        }

        [Fact]
        public void SucceededTrue()
        {
            Assert.True(new CopyFileResult().Succeeded);
        }

        [Fact]
        public void SucceededFalse()
        {
            Assert.False(CreateFrom("error").Succeeded);
        }
    }
}
