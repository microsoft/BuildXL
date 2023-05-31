// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

public class LastWriterWinsSetTests
{
    [Fact]
    public void MergeAddsNewValue()
    {
        var set = new LastWriterWinsSet<int>();
        var t1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.UtcNow, ChangeStampOperation.Add);
        set.Merge(new Stamped<int>(t1, 42));
        Assert.True(set.Contains(42));
    }

    [Fact]
    public void MergeReplacesValueWithLaterTimestamp()
    {
        var set = new LastWriterWinsSet<int>();
        var t1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.UtcNow, ChangeStampOperation.Add);
        var t2 = t1.Next(ChangeStampOperation.Add, DateTime.UtcNow);

        set.Merge(new Stamped<int>(t1, 42));
        set.Merge(new Stamped<int>(t2, 42));

        Assert.True(set.TryGetChangeStamp(42, out var seen));
        Assert.Equal(t2, seen);
        Assert.True(set.Contains(42));
    }

    [Fact]
    public void TryGetTimestampReturnsTrueAndValidTimestamp()
    {
        var set = new LastWriterWinsSet<string>();
        var t1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.UtcNow, ChangeStampOperation.Add);
        var t2 = t1.Next(ChangeStampOperation.Delete, DateTime.UtcNow);
        set.Merge(new Stamped<string>(t1, "apple"));
        set.Merge(new Stamped<string>(t1, "banana"));
        set.Merge(new Stamped<string>(t2, "apple"));

        var found = set.TryGetChangeStamp("banana", out var timestamp);
        Assert.True(found);
        Assert.Equal(t1, timestamp);
    }

    [Fact]
    public void TryGetTimestampReturnsFalseForNonexistentValue()
    {
        var set = new LastWriterWinsSet<string>();
        var t1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.UtcNow, ChangeStampOperation.Add);
        set.Merge(new Stamped<string>(t1, "apple"));
        set.Merge(new Stamped<string>(t1, "banana"));

        var found = set.TryGetChangeStamp("orange", out _);
        Assert.False(found);
    }

    [Fact]
    public void MergeSameTimestampAddsBothValues()
    {
        var set = new LastWriterWinsSet<int>();
        var t1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.UtcNow, ChangeStampOperation.Add);

        set.Merge(new Stamped<int>(t1, 42));
        set.Merge(new Stamped<int>(t1, 24));

        Assert.True(set.Contains(42));
        Assert.True(set.Contains(24));
    }

    [Fact]
    public void MergeDeleteTimestampRemovesValueAndKeepsChangeStamp()
    {
        var set = new LastWriterWinsSet<string>();
        var t1 = ChangeStamp.Create(new SequenceNumber(1), DateTime.UtcNow, ChangeStampOperation.Add);
        var t2 = t1.Next(ChangeStampOperation.Delete, DateTime.UtcNow);

        set.Merge(new Stamped<string>(t1, "apple"));
        set.Merge(new Stamped<string>(t1, "banana"));
        set.Merge(new Stamped<string>(t2, "apple"));

        Assert.True(set.Contains("banana"));
        Assert.False(set.Contains("apple"));

        var found = set.TryGetChangeStamp("apple", out var timestamp);
        Assert.True(found);
        Assert.Equal(t2, timestamp);
    }
}

