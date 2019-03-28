// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "CacheRecord.hpp"
#include "Monitor.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(CacheRecord, OSObject)

CacheRecord* CacheRecord::create()
{
    CacheRecord *instance = new CacheRecord;
    if (instance != nullptr)
    {
        if (!instance->init())
        {
            OSSafeReleaseNULL(instance);
        }
    }

    return instance;
}

bool CacheRecord::init()
{
    if (!super::init())
    {
        return false;
    }

    lock_ = IOLockAlloc();
    if (lock_ == nullptr)
    {
        return false;
    }

    requestedAccess_ = RequestedAccess::None;
    return true;
}

void CacheRecord::free()
{
    if (lock_ != nullptr)
    {
        IOLockFree(lock_);
        lock_ = nullptr;
    }

    super::free();
}

bool CacheRecord::Check(const AccessCheckResult *result) const
{
    // It's a cache hit if we've previously seen all the requested accesses.
    return HasAllFlags(requestedAccess_, result->RequestedAccess);
}

static const RequestedAccess LookupProbe     = RequestedAccess::Lookup | RequestedAccess::Probe;
static const RequestedAccess LookupProbeRead = LookupProbe | RequestedAccess::Read;
static const RequestedAccess ReadWrite       = RequestedAccess::Read | RequestedAccess::Write;
static const RequestedAccess ProbeReadWrite  = RequestedAccess::Probe | ReadWrite;

// CODESYNC: keep this the inverse of the below 'impliedBy' function
inline static RequestedAccess implies(RequestedAccess access)
{
    RequestedAccess result = RequestedAccess::None;

    // Probe implies Lookup
    if (HasAllFlags(access, RequestedAccess::Probe))
    {
        result |= RequestedAccess::Lookup;
    }

    // Read implies Probe (and, transitively, Lookup)
    if (HasAllFlags(access, RequestedAccess::Read))
    {
        result |= LookupProbe;
    }

    // Write implies both Read (and, transitively, Probe and Lookup)
    if (HasAllFlags(access, RequestedAccess::Write))
    {
        result |= LookupProbeRead;
    }

    return result;
}

// CODESYNC: keep this the inverse of the above 'implies' function
inline static RequestedAccess impliedBy(RequestedAccess access)
{
    // Lookup is implied by Probe, Read, Write
    if (access == RequestedAccess::Lookup)
    {
        return ProbeReadWrite;
    }

    // Probe is implied by Read and Write
    if (access == RequestedAccess::Probe)
    {
        return ReadWrite;
    }

    // Read is implied by Write
    if (access == RequestedAccess::Read)
    {
        return RequestedAccess::Write;
    }

    return RequestedAccess::None;
}

bool CacheRecord::HasStrongerRequestedAccess(RequestedAccess access, int *outCacheAccess) const
{
    RequestedAccess accessesThatImplyGiveAccess = impliedBy(access);
    int cachedAccess = (int)requestedAccess_;
    if (outCacheAccess) *outCacheAccess = cachedAccess;
    return
        accessesThatImplyGiveAccess != RequestedAccess::None &&
        HasAnyFlags(cachedAccess, (int)accessesThatImplyGiveAccess);
}

void CacheRecord::Update(const AccessCheckResult *result)
{
    // Update requested access:
    //   - whenever Probe is seen, add Lookup as well;
    //   - whenever Read is seen, add Probe and Lookup as well;
    //   - whenever Write is seen, add Read, Probe, and Lookup as well.
    RequestedAccess access = result->RequestedAccess;
    requestedAccess_ |= access | implies(access);
}

bool CacheRecord::CheckAndUpdate(const AccessCheckResult *checkResult)
{
    bool isHit;
    IOLockLock(lock_);
    {
        isHit = Check(checkResult);
        if (!isHit)
        {
            Update(checkResult);
        }
    }
    IOLockUnlock(lock_);
    return isHit;
}
