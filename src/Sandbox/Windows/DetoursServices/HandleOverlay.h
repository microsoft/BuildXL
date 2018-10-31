// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

// Facility for associating extra data to a HANDLE, without actually replacing the handle itself.
// This allows associating key data such as normalized path and effective policy at the time of handle open,
// for use in HANDLE-only APIs such as GetFileInformationByHandle.
//
// HANDLE is effectively typed as void*. Why not indirect handles by returning a pointer to some structure wrapping the real handle?
// That's only viable so long as ALL HANDLE-consuming APIs are detoured (even boring things like GetHandleInformation); otherwise
// any missing API would reject our fake HANDLEs, or crash.
//
// Instead, we define a process-global HANDLE -> overlay map and return all HANDLEs unmodified.

#include "FileAccessHelpers.h"
#include "PolicyResult.h"

enum class HandleType {
    File,
    Directory,
    // Pseudo-handle as used by FindFirstFile
    Find
};

// Per-handle overlay data.
struct HandleOverlay {
    // Constructs a handle overlay for a handle, wrapping the creating operation's policy / access check.
    // The policy represents what operations should be allowed via operations on this handle.
    HandleOverlay(AccessCheckResult const& accessCheck, PolicyResult const& policy, HandleType type)
        : Policy(policy), AccessCheck(accessCheck), EnumerationHasBeenReported(false), Type(type) { }

    HandleOverlay(const HandleOverlay& other) = default;
    HandleOverlay& operator=(const HandleOverlay&) = default;

    PolicyResult Policy;
    AccessCheckResult AccessCheck;
    HandleType Type;

    // This flag is set when a directory handle enumeration is reported to BuildXL
    // by NtQueryDirectoryFile. It prevents multiple reports for the same directory
    // (some big enumerations require multiple calls to NtQueryDirectoryFile).
    bool EnumerationHasBeenReported;
};

// Sets up structures for recording handle overlays.
// This function is suitable for DllMain - it does not assume that CRT memory allocation is available.
void InitializeHandleOverlay();

// Thread-safe, counted reference to a HandleOverlay. Since disposal of a handle (e.g. CloseHandle) may run concurrently with some access to the handle
// (though that may result in downstream failures), we do not assume that finding an overlay (by valid HANDLE) guarantees lifetime for the duration
// of the calling (HANDLE-using) function. Instead, looking up a handle creates a new HandleOverlayRef (atomically), and so a HandleOverlay is not
// deallocated until all uses of it are complete.
typedef std::shared_ptr<HandleOverlay> HandleOverlayRef;

// Creates or replaces an overlay for the given handle (intended for the time at which a handle is created).
// The new overlays wraps the policy / access check determined for the handle so far.
// The policy represents what operations should be allowed via operations on this handle.
void RegisterHandleOverlay(HANDLE handle, AccessCheckResult const& accessCheck, PolicyResult const& policy, HandleType type);

// Tries to look up an existing overlay for the given handle. The returned ref may wrap nullptr in the event that there was no overlay found.
HandleOverlayRef TryLookupHandleOverlay(HANDLE handle, bool drain = true);

// If an overlay exists for the given handle, disassociates it from the handle. Future calls to TryLookupHandleOverlay for the handle will no
// longer succeed. Concurrent users that already have a ref to the overlay may continue to use it safely.
void CloseHandleOverlay(HANDLE handle, bool inRecursion = false);

// Adds a closed handle to the closed handle list.
void AddClosedHandle(HANDLE handle);

// Remove all closed handlefrom the overlay map.
void RemoveClosedHandles();