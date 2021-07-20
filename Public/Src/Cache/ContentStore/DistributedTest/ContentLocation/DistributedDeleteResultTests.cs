using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    public class DistributedDeleteResultTests
    {
        [Fact]
        public void Success()
        {
            var contentSize = 9;
            var contentHash = ContentHash.Random();
            var deleteResult = new DeleteResult(DeleteResult.ResultCode.Success, contentHash, contentSize);
            var deleteResultMapping = new Dictionary<string, DeleteResult> {{"TEST_MACHINE_LOCATION", deleteResult}};

            var distributedDeleteResult = new DistributedDeleteResult(contentHash, contentSize, deleteResultMapping);
            distributedDeleteResult.ToString().Should().Contain("ContentSize");
            distributedDeleteResult.ToString().Should().Contain("Success");
        }

        [Fact]
        public void ContentNotFound()
        {
            var contentSize = 0;
            var contentHash = ContentHash.Random();
            var deleteResult = new DeleteResult(DeleteResult.ResultCode.ContentNotFound, contentHash, contentSize);
            var deleteResultMapping = new Dictionary<string, DeleteResult>();
            deleteResultMapping.Add("TEST_MACHINE_LOCATION", deleteResult);

            var distributedDeleteResult = new DistributedDeleteResult(contentHash, contentSize, deleteResultMapping);
            distributedDeleteResult.ToString().Should().Contain("size could not be determined");
            distributedDeleteResult.ToString().Should().Contain("ContentNotFound");

        }

        [Fact]
        public void Error()
        {
            var contentSize = 0;
            var contentHash = ContentHash.Random();
            var deleteResult = new DeleteResult(DeleteResult.ResultCode.Error, "errorMsg", "reason");
            var deleteResultMapping = new Dictionary<string, DeleteResult> {{"TEST_MACHINE_LOCATION", deleteResult}};

            var distributedDeleteResult = new DistributedDeleteResult(contentHash, contentSize, deleteResultMapping);
            distributedDeleteResult.ToString().Should().Contain("errorMsg");
            distributedDeleteResult.ToString().Should().Contain("reason");

        }

        [Fact]
        public void Exception()
        {
            var contentSize = 0;
            var contentHash = ContentHash.Random();
            var innerException = new NullReferenceException("innerMsg");
            var outerException = new NullReferenceException("outerMsg", innerException);
            var deleteResult = new DeleteResult(DeleteResult.ResultCode.ContentNotDeleted, outerException);
            var deleteResultMapping = new Dictionary<string, DeleteResult> {{"TEST_MACHINE_LOCATION", deleteResult}};

            var distributedDeleteResult = new DistributedDeleteResult(contentHash, contentSize, deleteResultMapping);

            distributedDeleteResult.ToString().Should().Contain($"{outerException.GetType().Name}");
            distributedDeleteResult.ToString().Should().Contain($"{outerException.Message}");
            distributedDeleteResult.ToString().Should().Contain($"{innerException.GetType().Name}");
            distributedDeleteResult.ToString().Should().Contain($"{innerException.Message}");
        }

        [Fact]
        public void SuccessAndError()
        {
            var contentSize = 8;
            var contentHash = ContentHash.Random();
            var successResult = new DeleteResult(contentHash, contentSize);
            var errorResult = new DeleteResult(DeleteResult.ResultCode.Error, "errorMsg");
            var deleteResultMapping = new Dictionary<string, DeleteResult> { { "TEST_MACHINE_LOCATION", successResult }, { "TEST_MACHINE_LOCATION2", errorResult } };

            var distributedDeleteResult = new DistributedDeleteResult(contentHash, contentSize, deleteResultMapping);

            distributedDeleteResult.ToString().Should().Contain(errorResult.ErrorMessage);
            distributedDeleteResult.ToString().Should().Contain("ContentSize");
            distributedDeleteResult.ToString().Should().Contain("Status=Success");

        }
    }
}
