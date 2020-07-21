// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class CopyFileResultTests : ResultTests<CopyFileResult>
    {
        protected override CopyFileResult CreateFrom(Exception exception)
        {
            return new CopyFileResult(CopyResultCode.UnknownServerError, exception);
        }

        protected override CopyFileResult CreateFrom(string errorMessage)
        {
            return new CopyFileResult(CopyResultCode.UnknownServerError, errorMessage);
        }

        protected override CopyFileResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new CopyFileResult(CopyResultCode.UnknownServerError, errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            const CopyResultCode code = CopyResultCode.UnknownServerError;
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
