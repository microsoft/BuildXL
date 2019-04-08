// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"
#include "HandleOverlay.h"
#include <map>
#include "buildXL_mem.h"

// A pre-allocated list with entries to be used to accumulate the closed handles by NtClose.
// During testing there were never more than 2 entries in this list on SelfHost and Office builds.
// Allocating 1000 should be safe we are not skipping handles. If we need more than 1000,
// a warning will be issued and the handle will not removed from the fie handle map.
// In such case we will behave exactly as we behave now - without this change we don't remove any handles from the map.
// If the list is full we will not remove the handle from the map. If that happens, increase the preallocated list size.
#define CLOSED_HANDLES_POOL_ENTRIES 2000
#define NT_CLOSE_CLEANUP_THRESHOLD 500
#define LARGE_LIST_MULTIPLIER 20

bool g_initialized;
CRITICAL_SECTION g_handleOverlayLock;

class HandleOverlayMap;
HandleOverlayMap* g_handleOverlayMap;
PSLIST_HEADER g_pClosedHandles = nullptr;

// Used to pre-create entries for closed handles in NtClose, 
// so we can clean them from the overlay map when it is safe to get the lock.
PSLIST_HEADER g_pClosedHandlesPool = nullptr;

extern volatile LONG g_detoursAllocatedNoLockConcurentPoolEntries;
extern volatile LONG64 g_detoursMaxHandleHeapEntries;
extern volatile LONG64 g_detoursHandleHeapEntries;

static volatile LONG g_usedPoolEntries = 0;

typedef struct _HANDLE_TO_CLOSE {
    SLIST_ENTRY ItemEntry;
    HANDLE Handle;
} HANDLE_TO_CLOSE, *PHANDLE_TO_CLOSE;

class HandleOverlayMap {
public:
    void MapRegisterHandleOverlay(HANDLE handle, HandleOverlayRef& newRef) {
        
        // Now, insert (move-assign to empty) or replace (destruct then move-assign). Note that despite perhaps
        // holding g_handleOverlayLock, we require here that shared_ptr is thread safe for refcount changes (as documented).
        // When destructing, we need to atomically decrement the ref-count ; some other routine may still be using another ref to the same overlay.
        m_map[handle] = std::move(newRef);

        // If we are tracking process data, track also the HandleOverlay map entries.
        if (ShouldLogProcessData())
        {
            LONG64 entriesCount = InterlockedIncrement64(&g_detoursHandleHeapEntries);
            LONG64 localMax = InterlockedAdd64(&g_detoursMaxHandleHeapEntries, 0);

            // Update the global g_detoursMaxHandleHeapEntries heap only if the current allocated entries is bigger than what is recorded max.
            while (entriesCount > localMax)
            {
                InterlockedCompareExchange64(&g_detoursMaxHandleHeapEntries, entriesCount, localMax);
                localMax = InterlockedAdd64(&g_detoursMaxHandleHeapEntries, 0);
            }
        }
    }

    HandleOverlayRef TryLookupHandleOverlay(HANDLE handle) {
        auto iter = m_map.find(handle);
        if (iter == m_map.end()) {
            return HandleOverlayRef();
        }
        else {
            // Create a new ref (refcount increases) via copy-construction of the existing one.
            return HandleOverlayRef(iter->second);
        }
    }

    void CloseHandleOverlay(HANDLE handle) {
        size_t removed = m_map.erase(handle);
        if (removed != 0 && ShouldLogProcessData())
        {
            InterlockedAdd64(&g_detoursHandleHeapEntries, -((LONG64)removed));
        }
    }

private:
    std::map<HANDLE, HandleOverlayRef> m_map;
};

// Holds g_handleOverlayLock
struct HandleOverlayLockGuard {
    HandleOverlayLockGuard() {
        assert(g_initialized);
        EnterCriticalSection(&g_handleOverlayLock);
    }

    ~HandleOverlayLockGuard() {
        LeaveCriticalSection(&g_handleOverlayLock);
    }

    // This is a member function to make sure we always get the map inside a lock.
    inline HandleOverlayMap* GetGlobalOverlayMap() {
        assert(g_handleOverlayMap != nullptr);
        return g_handleOverlayMap;
    }
};

static void PopulateNtCloseListPool()
{
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
   ULONGLONG startTime = GetTickCount64();
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT
    unsigned allocationSize = CLOSED_HANDLES_POOL_ENTRIES;
    if (UseLargeNtClosePreallocatedList())
    {
        // Allocate a large list.
        allocationSize *= LARGE_LIST_MULTIPLIER;
    }

    for (unsigned i = 0; i < allocationSize; i++)
    {
        PHANDLE_TO_CLOSE pPoolHandleEntry = (PHANDLE_TO_CLOSE)_dd_aligned_malloc(sizeof(HANDLE_TO_CLOSE), MEMORY_ALLOCATION_ALIGNMENT);
        if (pPoolHandleEntry == nullptr)
        {
            Dbg(L"Memory alloc failed for g_pClosedHandlesPool node 0x%p", g_pClosedHandlesPool);
        }
        else
        {
            pPoolHandleEntry->Handle = INVALID_HANDLE_VALUE;

            // Populate the pool
            InterlockedPushEntrySList(g_pClosedHandlesPool, &(pPoolHandleEntry->ItemEntry));
            InterlockedIncrement(&g_detoursAllocatedNoLockConcurentPoolEntries);
        }
    }
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
    ULONGLONG endTime = GetTickCount64();
    InterlockedExchangeAdd(&g_msTimeToPopulatePoolList, (LONG)(endTime - startTime));
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT

}

// This is a routine that creates a background thread to close any NtClose accumulated handles.
// The prebuild step of Office uses Perl and tons of pipe logging, without opening a file, so the
// NtClose list drain logic doesn't kick in, thus creating a potential problem of having invalid 
// handles in the map (handles that have been reused, but never cleaned up).
DWORD WINAPI CleanupNtClosedHandles(LPVOID lpParam)
{
    UNREFERENCED_PARAMETER(lpParam);
    if (UseExtraThreadToDrainNtClose())
    {
        RemoveClosedHandles();
    }

    return 0;
}

void StartCleanupNtClosedHandlesThread()
{
    HANDLE threadHandle = CreateThread(
        NULL,
        0,
        CleanupNtClosedHandles,
        nullptr,
        0,
        nullptr);
    
    if (threadHandle == INVALID_HANDLE_VALUE)
    {
        Dbg(L"Warning: Could not create CleanupNtClosedHandlesThread.");
    }
    else
    {
        SetThreadPriority(threadHandle, THREAD_PRIORITY_HIGHEST);
    }
}

void InitializeHandleOverlay() {

    assert(!g_initialized);
    InitializeCriticalSection(&g_handleOverlayLock);
    // Always create the OverlayMap.This is called from DllAttach, so it is inside alock already.
    // Doing it here, we save check and creating the map inside the GetOverlayMap.
    g_handleOverlayMap = new HandleOverlayMap();

    // The NtClose(d) handles are in the g_pClosedHandles. (It is a lock free list.)
    // Since allocation of memory is unsafe inside the NtClose execution path (there should
    // not be locks on this path), preallocate a list of nodes to be used as a pool - g_pClosedHandlesPool.
    g_pClosedHandles = (PSLIST_HEADER)_dd_aligned_malloc(sizeof(SLIST_HEADER), MEMORY_ALLOCATION_ALIGNMENT);
    if (g_pClosedHandles == nullptr)
    {
        Dbg(L"Allocation for g_pClosedHandles failed");
    }

    assert(g_pClosedHandles != nullptr);
    InitializeSListHead(g_pClosedHandles);

    g_pClosedHandlesPool = (PSLIST_HEADER)_dd_aligned_malloc(sizeof(SLIST_HEADER), MEMORY_ALLOCATION_ALIGNMENT);
    if (g_pClosedHandlesPool == nullptr)
    {
        Dbg(L"Allocation for g_pClosedHandlesPool failed");
    }

    assert(g_pClosedHandlesPool != nullptr);
    InitializeSListHead(g_pClosedHandlesPool);
    PopulateNtCloseListPool();

    g_initialized = true;
}

void RegisterHandleOverlay(HANDLE handle, AccessCheckResult const& accessCheck, PolicyResult const& policy, HandleType type) {
    if (UseExtraThreadToDrainNtClose())
    {
        RemoveClosedHandles();
    }

    // First we create a shared_ptr for a new HandleOverlay (ref count 1).
    // Note: This must be created outside of the HandleOverlayLockGuard lock below,
    //       because otherwise we will get a deadlock - the HandleMap lock and the RtlAllocHeap in the OS heap allocator lock.
    //       
    HandleOverlayRef newRef = std::make_shared<HandleOverlay>(accessCheck, policy, type);

    // Get an extra reference to the handle. This way the shared_ptr is not deleted when removed from the map.
    //
    // The issue of destroying the object when removing from the map is that there is a potential for a deadlock.
    // The removal from the map happens while holding the HandleOverlayLockGuard lock 
    // (in the MapRegisterHandleOverlay method we do std::move that can call an object destruction and RtlFreeHeap).
    // The freeing of memory happens while a heap lock is held - so if destruction happens,
    // the order of lock aquisition is HandleMapLock--> HeapLock.
    // RtlFreeHeap also calls NtClose, while holding the heap lock, so it is possible to try to get the locks in 
    // order HeapLock-->HandleMapLock.
    // These two clearly point to a deadlock due to inverted lock aquisition.
    HandleOverlayRef overlay = TryLookupHandleOverlay(handle, false);

    {
        HandleOverlayLockGuard lock;
        HandleOverlayMap* map = lock.GetGlobalOverlayMap();
        map->MapRegisterHandleOverlay(handle, newRef);
    }
}

HandleOverlayRef TryLookupHandleOverlay(HANDLE handle, bool drain) {
    if (drain && UseExtraThreadToDrainNtClose())
    {
        RemoveClosedHandles();
    }

    HandleOverlayLockGuard lock;
    HandleOverlayMap* map = lock.GetGlobalOverlayMap();
    return map->TryLookupHandleOverlay(handle);
}

void CloseHandleOverlay(HANDLE handle, bool inRecursion) {
    if (!inRecursion)
    {
        // Call this from here to relieve pressure on the pre-allocated SList entry pool.
        if (UseExtraThreadToDrainNtClose())
        {
            RemoveClosedHandles();
        }
    }
    
    // Get an extra reference to the handle. This way the shared_ptr is not deleted when removed from the 
    // map.
    // The issue of destroying the object when removing from the map is that there is a potential for a deadlock.
    // The removal from the map happens while holding the HandleOverlayLockGuard lock (see below).
    // If the map holds the last ref to the shared_ptr, when removing it, the destructor of the object will be called,
    // thus triggering deletion of the object from the OS heap - RtlFreeHeap. The freeing of memory happens
    // while a heap lock is held - so if destruction happens, the order of lock aquisition is HandleMapLock--> HeapLock.
    // RtlAllocateHeap also calls NtClose, while holding the heap lock, so it is possible to try to get the locks in 
    // order HeapLock-->HandleMapLock.
    // These two clearly point to a deadlock due to inverted lock aquisition.
    HandleOverlayRef overlay = TryLookupHandleOverlay(handle, false);
    
    {
        // Extra scope here to make sure the lock is destroied before the overlay above goes out of scope
        // and releases the last ref to the object pointer.
        HandleOverlayLockGuard lock;
        HandleOverlayMap* map = lock.GetGlobalOverlayMap();
        map->CloseHandleOverlay(handle);
    }
}

void AddClosedHandle(HANDLE handle) {
    // Be safe and check all the list pointers as well since a NtClose (where this method is called from)
    // can come very early in the execution of a process.
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
    ULONGLONG startAdd = GetTickCount64();
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT
    // Cleaup any pending NtClose handles, if the remaining unused are less than NT_CLOSE_CLEANUP_THRESHOLD
    if ((g_detoursAllocatedNoLockConcurentPoolEntries - g_usedPoolEntries) < NT_CLOSE_CLEANUP_THRESHOLD)
    {
        // When below threshold start a new thread. It will be with higher priority to drain the list.
        // The thread routine is completely thread safe and we might create multiple threads and that is just fine.
        // The expectation is that multiple threads creation will happen very rarely.
        StartCleanupNtClosedHandlesThread();
    }

    if (g_initialized && g_pClosedHandles != nullptr && g_pClosedHandlesPool != nullptr) {
        PSLIST_ENTRY pEntry = InterlockedPopEntrySList(g_pClosedHandlesPool);

        if (pEntry == nullptr)
        {
            Dbg(L"Warning: No available entries in g_pClosedHandlesPool list.");
        }
        else
        {
            ((PHANDLE_TO_CLOSE)pEntry)->Handle = handle;
            InterlockedPushEntrySList(g_pClosedHandles, &(((PHANDLE_TO_CLOSE)pEntry)->ItemEntry));
            InterlockedIncrement(&g_usedPoolEntries);
        }
    }
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
    InterlockedIncrement(&g_maxClosedListCount);
    ULONGLONG endAdd = GetTickCount64();
    InterlockedExchangeAdd(&g_msTimeInAddClosedList, (LONG)(endAdd - startAdd));
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT
}

// Note: It is potentially possible to call these method while an entry is added to the non-locking list.
// In such case the entry will be removed from the overlay map on the next iteration.
void RemoveClosedHandles() {
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
    ULONGLONG startAdd = GetTickCount64();
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT
    if (g_initialized && g_pClosedHandles != nullptr && g_pClosedHandlesPool != nullptr) {
        PSLIST_ENTRY pEntry = InterlockedPopEntrySList(g_pClosedHandles);
        while (pEntry != NULL)
        {
            CloseHandleOverlay(((PHANDLE_TO_CLOSE)pEntry)->Handle, true);
            ((PHANDLE_TO_CLOSE)pEntry)->Handle = INVALID_HANDLE_VALUE;
            InterlockedPushEntrySList(g_pClosedHandlesPool, &(((PHANDLE_TO_CLOSE)pEntry)->ItemEntry));
            pEntry = InterlockedPopEntrySList(g_pClosedHandles);
            InterlockedDecrement(&g_usedPoolEntries);
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
            InterlockedDecrement(&g_maxClosedListCount);
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT        
        }

        // Grow the list if needed.
        if ((g_detoursAllocatedNoLockConcurentPoolEntries - g_usedPoolEntries) < NT_CLOSE_CLEANUP_THRESHOLD)
        {
            PopulateNtCloseListPool();
        }
    }
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
    ULONGLONG endAdd = GetTickCount64();
    InterlockedExchangeAdd(&g_msTimeInRemoveClosedList, (LONG)(endAdd - startAdd));
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT
}
