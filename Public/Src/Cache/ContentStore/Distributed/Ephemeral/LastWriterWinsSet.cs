// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public readonly record struct Stamped<T>(ChangeStamp ChangeStamp, T Value)
{
    public bool HappensBefore(Stamped<T> rhs)
    {
        return ChangeStamp < rhs.ChangeStamp;
    }
}

/// <summary>
/// Implements a Last-Writer Wins Element-Set. There's a single value for each element, and the last writer wins. Who
/// is the last writer is determined by the <see cref="ChangeStamp"/>. The <see cref="ChangeStamp"/> is totally ordered
/// and monotonically increasing, so there's never any ambiguity about who is the last writer.
/// </summary>
public class LastWriterWinsSet<T>
    where T : IComparable<T>
{
    private readonly List<Stamped<T>> _operations = new();

    /// <summary>
    /// The entries in the set, ordered by value.
    /// </summary>
    public IReadOnlyList<Stamped<T>> Operations => _operations;

    /// <summary>
    /// Add operations into the set by means of merging delta-updates
    /// </summary>
    public void Merge(Stamped<T> operation)
    {
        if (_operations.Count == 0)
        {
            _operations.Add(operation);
            return;
        }

        int index = FindIndex(operation.Value);
        if (index >= 0)
        {
            if (_operations[index].HappensBefore(operation))
            {
                _operations[index] = operation;
            }
        }
        else
        {
            // This is essentially equivalent to an insertion sort. We don't expect the list to be very long, so this
            // is likely better than using a tree.
            _operations.Insert(~index, operation);
        }
    }

    /// <summary>
    /// Try to get the <see cref="ChangeStamp"/> for a given value.
    /// </summary>
    public bool TryGetChangeStamp(T value, out ChangeStamp changeStamp)
    {
        if (_operations.Count == 0)
        {
            changeStamp = default;
            return false;
        }

        var index = FindIndex(value);
        if (index >= 0)
        {
            changeStamp = _operations[index].ChangeStamp;
            return true;
        }

        changeStamp = default;
        return false;
    }

    /// <summary>
    /// Check whether the set contains a given value.
    /// </summary>
    public bool Contains(T value)
    {
        if (_operations.Count == 0)
        {
            return false;
        }

        var index = FindIndex(value);
        return index >= 0 && _operations[index].ChangeStamp.Operation == ChangeStampOperation.Add;
    }

    private int FindIndex(T value)
    {
        return _operations.BinarySearch(new Stamped<T>(ChangeStamp.Invalid, value), StampedValueComparer.Instance);
    }

    private class StampedValueComparer : IComparer<Stamped<T>>
    {
        public static readonly StampedValueComparer Instance = new();

        public int Compare(Stamped<T> lhs, Stamped<T> rhs)
        {
            return lhs.Value.CompareTo(rhs.Value);
        }
    }
}
