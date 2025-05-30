// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __EVENT_CACHE_H
#define __EVENT_CACHE_H

#include "vmlinux.h"

#include "bpf/bpf_core_read.h"
#include "bpf/bpf_helpers.h"
#include "bpf/bpf_tracing.h"
#include "ebpfcommon.h"
#include "ebpfutilities.h"

__attribute__((always_inline)) static inline void report_event_cache_not_found(pid_t runner_pid);

// We keep a LRU map so we do not send out events that are considered equivalent. Sending too many events can cause the ring buffer to not be able
// to keep up and allocations will start to fail. Please refer to https://docs.kernel.org/bpf/map_hash.html for details.
// We don't really care about having accurate eviction or across-CPU duplication, we just need a way to avoid sending events for very repetitive operations on the same set of paths in
// a short period of time.
struct event_cache {
    __uint(type, BPF_MAP_TYPE_LRU_HASH);
    // We want to keep a balance between not sending repetitive paths and keeping this map small enough so eviction is not that expensive.
    // We could bump this up if we see allocation problems for repetitive paths
    __uint(max_entries, 65536);
    __type(key, cache_event_key);
    // We don't really care about the value, we use this map as a set
    __type(value, short);
} event_cache SEC(".maps");

/**
 * Similar to file_access_per_pip, holds one event cache per pip. Cached events shouldn't be shared cross-pips
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
    __array(values, struct event_cache);
} event_cache_per_pip SEC(".maps");

/**
 * The constant we use as map values. We are using the map as a set, so the value is not important.
 */
static const short NO_VALUE = 0;

__attribute__((always_inline)) static inline unsigned long ptr_to_long(const void *ptr)
{
    return (unsigned long) ptr;
}

/**
 * Whether the operation + path has been sent before. This operation returns whether the event is not found in the cache and, as a side
 * effect, adds it to the cache if it wasn't there.
 * Consider that behind scenes a LRU cache is used, so whether an element is kept in the cache depends on usage/frequency
 */
__attribute__((always_inline)) static bool should_send_path(pid_t runner_pid, operation_type operation, const struct path* path)
{
    struct dentry *dentry = BPF_CORE_READ(path, dentry);
    struct vfsmount *vfsmount = BPF_CORE_READ(path, mnt);

    // Just get the memory address of dentry and mount to build the key
    struct cache_event_key key = {.dentry = ptr_to_long(dentry), .vfsmount = ptr_to_long(vfsmount), .op_type = operation};

    void *event_cache = bpf_map_lookup_elem(&event_cache_per_pip, &runner_pid);
    if (event_cache == NULL) {
        report_event_cache_not_found(runner_pid);
        return 0;
    }

    // If the key is not there, we should send the event and add the key as well
    // We could use BPF_NOEXIST and save one lookup operation, but it looks like this flag is not working properly in some circumstances and
    // the lookup comes back with a successful error code when the element exists.
    if (bpf_map_lookup_elem(event_cache, &key) == NULL)
    {
        bpf_map_update_elem(event_cache, &key, &NO_VALUE, BPF_ANY);
        return true;
    }

    // If the lookup found the key, don't send the event
    return false;
}

#endif // __EVENT_CACHE_H