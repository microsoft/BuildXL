// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_PERCPUSTACK_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_PERCPUSTACK_H

#include "vmlinux.h"
#include "kernelconstants.h"
#include "ebpfcommon.h"

const int EMPTY_STACK_INDEX = -1;

/**
 * There is no PERCPU stack map type, so we implement an int stack using a PERCPU array for values, plus a PERCPU array for the current index.
 */
struct per_cpu_int_stack
{
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(int));
    __uint(value_size, sizeof(int));
    __uint(max_entries, PATH_MAX);
} per_cpu_int_stack SEC(".maps");

/**
 * Index of the top element in the per_cpu_int_stack.
 * We use a PERCPU array with a single element to store the index.
 */
struct per_cpu_int_stack_index
{
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(int));
    __uint(value_size, sizeof(int));
    __uint(max_entries, 1);
} per_cpu_int_stack_index SEC(".maps");

/**
 * Pushes an element onto the per-CPU int stack.
 * Returns the popped value on success, -1 on failure (e.g., empty stack).
 */
__attribute__((always_inline)) static int pop_elem() {
    int *index = bpf_map_lookup_elem(&per_cpu_int_stack_index, &ZERO);
    if (index == NULL || *index < 0) {
        return -1; // Stack is empty or error
    }

    int *value = bpf_map_lookup_elem(&per_cpu_int_stack, index);
    if (value == NULL) {
        return -1; // Error retrieving value
    }
    
    // Decrement the index to "remove" the top element
    (*index)--;
   
    return *value;
}

/**
 * Pushes an element onto the per-CPU int stack.
 * Returns 0 on success, -1 on failure (e.g., stack full).
 */
__attribute__((always_inline)) static int push_elem(int value) {
    int *index = bpf_map_lookup_elem(&per_cpu_int_stack_index, &ZERO);
    if (index == NULL) {
        return -1; // Error retrieving index
    }

    if (*index >= PATH_MAX - 1) {
        return -1; // Stack full
    }

    // Increment the index and add the new value
    (*index)++;
    if (bpf_map_update_elem(&per_cpu_int_stack, index, &value, BPF_ANY)) {
        return -1; // Error updating stack
    }

    return 0; // Success
}

/**
 * Empties the per-CPU int stack.
 * Should also be called for initializing the stack before use.
 */
__attribute__((always_inline)) static void empty_stack() {
    // We don't need to actually clear the values in the stack, just reset the index
    bpf_map_update_elem(&per_cpu_int_stack_index, &ZERO, &EMPTY_STACK_INDEX, BPF_ANY);
}

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_PERCPUSTACK_H
