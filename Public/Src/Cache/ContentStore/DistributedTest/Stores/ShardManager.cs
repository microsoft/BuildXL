// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Blob;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Stores;

public class Entry : ILocation<int>
{
    public int Location { get; }

    public bool Available { get; set; }

    public Entry(int location, bool available)
    {
        Location = location;
        Available = available;
    }
}

public class ShardManager : IShardManager<int>
{
    private readonly List<Entry> _entries;

    public IReadOnlyList<ILocation<int>> Locations => _entries;

    public event EventHandler? OnResharding;

    public ShardManager(IEnumerable<int> locations)
    {
        _entries = locations.Select(location => new Entry(location, available: true)).ToList();
    }

    public Entry AddOrGet(int location)
    {
        var index = _entries.FindIndex(l => l.Location == location);
        if (index != -1)
        {
            return _entries[index];
        }

        var entry = new Entry(location, true);
        _entries.Add(entry);
        OnResharding?.Invoke(this, EventArgs.Empty);

        return entry;
    }

    public void Remove(int location)
    {
        var index = _entries.FindIndex(l => l.Location == location);
        if (index != -1)
        {
            _entries.RemoveAt(index);
            OnResharding?.Invoke(this, EventArgs.Empty);
        }
    }
}
