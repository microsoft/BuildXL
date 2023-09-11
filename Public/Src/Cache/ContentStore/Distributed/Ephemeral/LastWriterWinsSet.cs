// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

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
    private List<Stamped<T>> _operations;

    /// <summary>
    /// The entries in the set, ordered by value.
    /// </summary>
    public IReadOnlyList<Stamped<T>> Operations => _operations;

    public int Count => _operations.Count;

    private LastWriterWinsSet(List<Stamped<T>> operations)
    {
        _operations = operations;
    }

    public static LastWriterWinsSet<T> Empty()
    {
        return new LastWriterWinsSet<T>(new());
    }

    public static LastWriterWinsSet<T> From(List<Stamped<T>> operations, bool sorted = false)
    {
        if (!sorted)
        {
            InPlaceSortAndKeepHighestStampedOperations(operations);
        }

        return new LastWriterWinsSet<T>(operations);
    }

    private static void InPlaceSortAndKeepHighestStampedOperations(List<Stamped<T>> operations)
    {
        operations.Sort((op1, op2) =>
                        {
                            int valueComparison = op1.Value.CompareTo(op2.Value);
                            if (valueComparison != 0)
                            {
                                return valueComparison;
                            }

                            return op2.ChangeStamp.CompareTo(op1.ChangeStamp);
                        });

        int resultCount = 0;
        Stamped<T>? currentOperation = null;

        for (int i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];

            if (currentOperation == null || !currentOperation.Value.Value.Equals(operation.Value))
            {
                if (currentOperation != null)
                {
                    operations[resultCount] = currentOperation.Value;
                    resultCount++;
                }
                currentOperation = operation;
            }
        }

        if (currentOperation != null)
        {
            operations[resultCount] = currentOperation.Value;
            resultCount++;
        }

        operations.RemoveRange(resultCount, operations.Count - resultCount);
    }

    private static List<Stamped<T>> Merge(IReadOnlyList<Stamped<T>> lhs, IReadOnlyList<Stamped<T>> rhs)
    {
        int ilhs = 0;
        int irhs = 0;
        var merged = new List<Stamped<T>>(capacity: lhs.Count + rhs.Count);

        while (ilhs < lhs.Count && irhs < rhs.Count)
        {
            var olhs = lhs[ilhs];
            var orhs = rhs[irhs];

            int cmp = olhs.Value.CompareTo(orhs.Value);
            if (cmp < 0)
            {
                merged.Add(olhs);
                ilhs++;
            }
            else if (cmp > 0)
            {
                merged.Add(orhs);
                irhs++;
            }
            else
            {
                merged.Add(olhs.HappensBefore(orhs) ? orhs : olhs);

                ilhs++;
                irhs++;
            }
        }

        while (ilhs < lhs.Count)
        {
            merged.Add(lhs[ilhs]);
            ilhs++;
        }

        while (irhs < rhs.Count)
        {
            merged.Add(rhs[irhs]);
            irhs++;
        }

        return merged;
    }

    public void MergePreSorted(IReadOnlyList<Stamped<T>> operations)
    {
        if (operations.Count == 0)
        {
            return;
        }

        // This is a very common case, so we optimize for it.
        if (operations.Count == 1)
        {
            Merge(operations[0]);
            return;
        }

        _operations = Merge(_operations, operations);
    }

    public void Merge(LastWriterWinsSet<T> other)
    {
        if (other.Count == 0)
        {
            return;
        }

        // This is a very common case, so we optimize for it.
        if (other.Count == 1)
        {
            Merge(other._operations[0]);
            return;
        }

        _operations = Merge(_operations, other._operations);
    }

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
