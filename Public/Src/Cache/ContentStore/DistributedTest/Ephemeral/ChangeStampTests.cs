// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using Xunit;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

public class ChangeStampTests
{
    [Fact]
    public void OperationsAreAlwaysLessThanMaximumValue()
    {
        foreach (var value in Enum.GetValues(typeof(ChangeStampOperation)).Cast<ChangeStampOperation>())
        {
            Assert.True((int)value <= ChangeStamp.MaxOperation);
        }
    }

    [Theory]
    [InlineData(12345, "2023-05-01T12:30:45Z", ChangeStampOperation.Delete)]
    [InlineData(12345, "2023-05-01T12:30:45Z", ChangeStampOperation.Add)]
    [InlineData(12346, "2023-05-01T12:30:45Z", ChangeStampOperation.Delete)]
    [InlineData(12347, "2023-05-01T12:30:45Z", ChangeStampOperation.Delete)]
    [InlineData(67890, "2023-09-15T18:45:30Z", ChangeStampOperation.Add)]
    [InlineData(0, "2023-01-02T00:00:00Z", ChangeStampOperation.Add)]
    public void VerifyPackSanity(uint sequenceNumber, string timestampUtcString, ChangeStampOperation operation)
    {
        var timestampUtc = DateTime.Parse(timestampUtcString).ToUniversalTime();
        var packedValue = ChangeStamp.Create(new SequenceNumber(sequenceNumber), timestampUtc, operation);

        Assert.Equal(sequenceNumber, packedValue.SequenceNumber);
        Assert.Equal(timestampUtc, packedValue.TimestampUtc);
        Assert.Equal(operation, packedValue.Operation);
    }

    [Fact]
    public void InvalidIsFixed()
    {
        Assert.Equal(ChangeStamp.Invalid.RawData, 0);
    }

    [Fact]
    [Trait("DisableFailFast", "true")]
    public void InvalidSequenceNumberThrowsException()
    {
        Assert.Throws<ContractException>(() => ChangeStamp.Create(new SequenceNumber((uint)(ChangeStamp.SequenceNumberMask + 1)), ChangeStamp.EpochUtc, ChangeStampOperation.Add));
    }

    [Fact]
    [Trait("DisableFailFast", "true")]
    public void TimestampBeforeEpochThrowsException()
    {
        Assert.Throws<ContractException>(() => ChangeStamp.Create(new SequenceNumber(0), ChangeStamp.EpochUtc - TimeSpan.FromMilliseconds(1), ChangeStampOperation.Add));
    }

    [Fact]
    [Trait("DisableFailFast", "true")]
    public void TimestampAfterMaxValueThrowsException()
    {
        Assert.Throws<ContractException>(() => ChangeStamp.Create(new SequenceNumber(0), ChangeStamp.MaxTimestampUtc + TimeSpan.FromHours(1), ChangeStampOperation.Add));
    }

    [Fact]
    public void CompareToReturnsNegativeWhenCurrentTimestampIsSmaller()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:01Z"), ChangeStampOperation.Add);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.True(result < 0);
    }

    [Fact]
    public void CompareToReturnsPositiveWhenCurrentTimestampIsGreater()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:01Z"), ChangeStampOperation.Add);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.True(result > 0);
    }

    [Fact]
    public void CompareToReturnsZeroWhenCurrentTimestampIsEqual()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareToReturnsNegativeWhenSequenceNumberIsSmaller()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(2), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.True(result < 0);
    }

    [Fact]
    public void CompareToReturnsPositiveWhenSequenceNumberIsGreater()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(2), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.True(result > 0);
    }

    [Fact]
    public void CompareToReturnsZeroWhenSequenceNumberIsEqual()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompareToReturnsNegativeWhenOperationIsDeleteAndCurrentTimestampIsAdd()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Delete);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.True(result < 0);
    }

    [Fact]
    public void CompareToReturnsPositiveWhenOperationIsAddAndCurrentTimestampIsDelete()
    {
        var timestamp1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Delete);
        var timestamp2 = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);

        int result = timestamp1.CompareTo(timestamp2);

        Assert.True(result > 0);
    }


    [Fact]
    public void CompareToThrowsArgumentExceptionWhenObjectIsNotTimestamp()
    {
        var timestamp = ChangeStamp.Create(new SequenceNumber(1), DateTime.Parse("2023-01-02T00:00:00Z"), ChangeStampOperation.Add);
        var invalidObject = new object();

        Assert.Throws<ArgumentException>(() => timestamp.CompareTo(invalidObject));
    }
}

