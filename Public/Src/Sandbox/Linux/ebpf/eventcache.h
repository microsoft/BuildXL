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

/**
 * An event key represents an operation + path, and used as a way to identify 'equivalent' events and prevent sending duplicates to user space.
 * For identifying the path, we use a combination of its dentry and vfsmount pair, and just use their memory
 * location (as unsigned long) to identify them. The rationale is that a dentry + mount pair is already pointing to a univocally
 * determined object in memory representing the path (which assumes that when the kernel lookup calls resolve a given path-as-string
 * it always ends up with the same dentry+mount instances for the same string). Even if this is not the case in all possible contexts, that it is
 * true in *most* contexts is enough to avoid sending too many equivalent events to user space.
 * Consider that using path-as-strings for the key is probably not a great idea, as the lookup logic for bpf maps use bitwise equality and there is no good way to represent
 * a PATH_MAX long string in the key and make that efficient. Luckily, most operations we care about give us access to the corresponding dentry and mount.
 */
struct cache_event_key {
    unsigned long dentry;
    unsigned long vfsmount;
    operation_type operation_type;
};

// We keep a LRU map per CPU so we do not send out events that are considered equivalent. Sending too many events can cause the ring buffer to not be able 
// to keep up and allocations will start to fail. We use BPF_MAP_TYPE_LRU_PERCPU_HASH maps and BPF_F_NO_COMMON_LRU so we keep evictions as fast as possible. 
// Please refer to https://docs.kernel.org/bpf/map_hash.html for details.
// We don't really care about having accurate eviction or across-CPU duplication, we just need a way to avoid sending events for very repetitive operations on the same set of paths in 
// a short period of time.
struct {
    __uint(type, BPF_MAP_TYPE_LRU_PERCPU_HASH);
    // We want to keep a balance between not sending repetitive paths and keeping this map small enough so eviction is not that expensive.
    // We could bump this up if we see allocation problems for repetitive paths
    __uint(max_entries, 32);
    __type(key, struct cache_event_key);
    // We don't really care about the value, we use this map as a set
    __type(value, short);
    __uint(map_flags, BPF_F_NO_COMMON_LRU);
} event_map SEC(".maps");

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
__attribute__((always_inline)) static bool should_send_path(operation_type operation, const struct path* path) 
{
    struct dentry *dentry = BPF_CORE_READ(path, dentry);
    struct vfsmount *vfsmount = BPF_CORE_READ(path, mnt);

    // Just get the memory address of dentry and mount to build the key
    struct cache_event_key key = {.dentry = ptr_to_long(dentry), .vfsmount = ptr_to_long(vfsmount), .operation_type = operation};

    // If the key is not there, we should send the event and add the key as well
    // We could use BPF_NOEXIST and save one lookup operation, but it looks like this flag is not working properly in some circumstances and
    // the lookup comes back with a successful error code when the element exists.
    if (bpf_map_lookup_elem(&event_map, &key) == NULL)
    {
        bpf_map_update_elem(&event_map, &key, &NO_VALUE, BPF_ANY);
        return true;
    }
    
    // If the lookup found the key, don't send the event
    return false;
}

#endif // __EVENT_CACHE_H