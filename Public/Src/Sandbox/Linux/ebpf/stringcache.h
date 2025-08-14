// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __STRING_CACHE_H
#define __STRING_CACHE_H

#include "vmlinux.h"

#include "bpf/bpf_core_read.h"
#include "bpf/bpf_helpers.h"
#include "bpf/bpf_tracing.h"
#include "ebpfcommon.h"
#include "ebpfutilities.h"

// Just for statistics
static int string_cache_hit, string_cache_miss, string_cache_uncacheable = 0;

__attribute__((always_inline)) static inline void report_string_cache_not_found(pid_t runner_pid);

/** Equivalent to event_cache, but used to cache path-as-strings for the cases where we don't have a struct path available.
 * In particular, absent probes don't have a dentry available, they are just plain strings from a filesystem standpoint and usually 
 * represent a significant amount of accesses.
 */
struct string_cache {
    __uint(type, BPF_MAP_TYPE_LRU_HASH);
    // We want to keep a balance between not sending repetitive paths and keeping this map small enough so eviction is not that expensive.
    // We could bump this up if we see allocation problems for repetitive paths
    __uint(max_entries, STRING_CACHE_MAP_SIZE);
    // Observe that we use a char array shorter than PATH_MAX. The rationale is that most paths are way shorter than PATH_MAX, and we want to avoid
    // allocating too much memory for the cache.
    __type(key, char[STRING_CACHE_PATH_MAX]);
    // We don't really care about the value, we use this map as a set
    __type(value, short);
} string_cache SEC(".maps");

/**
 * Similar to file_access_per_pip, holds one string cache per pip. Cached string shouldn't be shared cross-pips
 * We set max entries dynamically at creation time
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // This map value is not really that big, so it is not completely clear whether preallocation will actually increase memory footprint significantly.
    // We can revisit this if we see performance problems.
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct string_cache);
} string_cache_per_pip SEC(".maps");

/*
* Whether the path is cacheable. This is used to avoid caching paths that are too long, as they are unlikely to show up and it would force us to allocate a lot of memory for the cache.
 * Returns true if the path is cacheable, false otherwise.
 */
__attribute__((always_inline)) static bool is_cacheable(int path_length)
{
    if (path_length >= STRING_CACHE_PATH_MAX) {
        __sync_fetch_and_add(&string_cache_uncacheable, 1);
        return false;
    }

    return true;
}

/**
 * Whether the absent lookup has been sent before. This operation returns whether the event is not found in the cache and, as a side
 * effect, adds it to the cache if it wasn't there.
 * Consider that behind scenes a LRU cache is used, so whether an element is kept in the cache depends on usage/frequency
 */
__attribute__((always_inline)) static bool should_send_absent_lookup(pid_t runner_pid, const char* path, const int path_length)
{
    if (!is_cacheable(path_length))
    {
        return false;
    }

    // Retrieve the string cache for the current runner
    void *string_cache = bpf_map_lookup_elem(&string_cache_per_pip, &runner_pid);
    if (string_cache == NULL)
    {
        report_string_cache_not_found(runner_pid);
        return false;
    }

    // If the key is not there, we should send the event and add the key as well
    // We could use BPF_NOEXIST and save one lookup operation, but it looks like this flag is not working properly in some circumstances and
    // the lookup comes back with a successful error code when the element exists.
    if (bpf_map_lookup_elem(string_cache, path) == NULL)
    {
        bpf_map_update_elem(string_cache, path, &NO_VALUE, BPF_ANY);
        __sync_fetch_and_add(&string_cache_miss, 1);
        return true;
    }

    __sync_fetch_and_add(&string_cache_hit, 1);
    // If the lookup found the key, don't send the event
    return false;
}

#endif // __STRING_CACHE_H