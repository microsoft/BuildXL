// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "globals.h"

#pragma once

extern volatile LONG64 g_detoursMaxAllocatedMemoryInBytes;
extern volatile LONG64 g_detoursHeapAllocatedMemoryInBytes;

#define BUILDXL_DETOURS_MEMORY_ALLOC_FLAGS HEAP_ZERO_MEMORY

// This file defines a memory interface for BuildXL Detours, using the dd_ prefix.
// The general allocation APIs are stubbed out and one should call only the dd_* methods.
// The memory allocation done from the BuildXL Detours library happens on a private heap.

// malloc and free versions for this DLL.
inline void* dd_malloc(size_t size)
{
    assert(g_hPrivateHeap != nullptr);
    void* ret = HeapAlloc(g_hPrivateHeap, BUILDXL_DETOURS_MEMORY_ALLOC_FLAGS, size);

    if (ShouldLogProcessData())
    {
        // Get the size since alignment matters and the actual allocated bytes can be a bit moe than size.
        LONG64 allocatedSize = (LONG64)HeapSize(g_hPrivateHeap, BUILDXL_DETOURS_MEMORY_ALLOC_FLAGS, ret);
        allocatedSize = InterlockedAdd64(&g_detoursHeapAllocatedMemoryInBytes, allocatedSize);
        LONG64 localMax = InterlockedAdd64(&g_detoursMaxAllocatedMemoryInBytes, 0);

        // Update the global MaxAllocated heap only if the current allocated heap is bigger than what is recorded.
        while (allocatedSize > localMax)
        {
            InterlockedCompareExchange64(&g_detoursMaxAllocatedMemoryInBytes, allocatedSize, localMax);
            localMax = InterlockedAdd64(&g_detoursMaxAllocatedMemoryInBytes, 0);
        }
    }

    return ret;
}

inline void dd_free(void* pMem)
{
    assert(g_hPrivateHeap != nullptr);
    if (pMem == nullptr)
    {
        return;
    }

    if (ShouldLogProcessData())
    {
        // Get the size since alignment matters and the actual allocated bytes can be a bit moe than size.
        LONG64 deallocatedSize = (LONG64)HeapSize(g_hPrivateHeap, BUILDXL_DETOURS_MEMORY_ALLOC_FLAGS, pMem);
        InterlockedAdd64(&g_detoursHeapAllocatedMemoryInBytes, -(deallocatedSize));
    }

    HeapFree(g_hPrivateHeap, HEAP_ZERO_MEMORY, pMem);
}

// New news and deletes operators that call the private heap.
inline void* operator new(size_t count)
{
    return dd_malloc(count);
}

inline void* operator new[](size_t count)
{
    return dd_malloc(count);
}

inline void operator delete(void* ptr)
{
    dd_free(ptr);
}

inline void operator delete[](void* ptr)
{
    dd_free(ptr);
}

// Make sure noone calls malloc and free directly.
inline __declspec(restrict) void* malloc(size_t size)
{
    UNREFERENCED_PARAMETER(size);
    assert(!"Use dd_malloc method instead.");
}

inline void free(void* pMem)
{
    UNREFERENCED_PARAMETER(pMem);
    assert(!"Use dd_free method instead.");
}

inline __declspec(restrict) void* _aligned_malloc(size_t size, size_t alignment)
{
    UNREFERENCED_PARAMETER(size);
    UNREFERENCED_PARAMETER(alignment);
    assert(!"Use dd_aligned_malloc method instead.");
}

inline void* _aligned_free(size_t size, size_t alignment)
{
    UNREFERENCED_PARAMETER(size);
    UNREFERENCED_PARAMETER(alignment);
    assert(!"Use dd_aligned_free method instead.");
}

// Functions for allocating and freeing aligned memory.
inline void* _dd_aligned_malloc(size_t size, size_t alignment)
{
    assert(!(alignment & alignment - 1)); // We support only power of 2 aligned allocations.

    size_t allocPaddingSize = sizeof(void *);
    void *memoryWithPadding = dd_malloc(size + allocPaddingSize + alignment - 1);
    if (!memoryWithPadding)
    {
        return nullptr;
    }

    void *alignedMemory = reinterpret_cast<void *>(
        (reinterpret_cast<uintptr_t>(memoryWithPadding) + allocPaddingSize + alignment - 1) & ~(alignment - 1));
    void **memoryWithPaddingAddress = reinterpret_cast<void **>(alignedMemory);
    memoryWithPaddingAddress[-1] = memoryWithPadding;
    return alignedMemory;
}

inline void _dd_aligned_free(void *alignedMemory)
{
    void **memoryWithPaddingAddress = reinterpret_cast<void **>(alignedMemory); // This is the "aligned" pointer. 
    void *memoryWithPadding = memoryWithPaddingAddress[-1];
    dd_free(memoryWithPadding);
}
