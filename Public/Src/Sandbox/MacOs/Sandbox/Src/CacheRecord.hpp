// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef CacheRecord_hpp
#define CacheRecord_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>
#include "BuildXLSandboxShared.hpp"
#include "FileAccessHelpers.h"

#define CacheRecord BXL_CLASS(CacheRecord)

/*!
 * A cache record where we keep track of already reported accesses for a given path.
 */
class CacheRecord : public OSObject
{
private:

    OSDeclareDefaultStructors(CacheRecord)

    IOLock *lock_;

    /*!
     * A bitwise disjunction of reported accesses.
     */
    RequestedAccess requestedAccess_;
    
    /*!
     * Determines if the given 'checkResult' should be deemed a cache hit (and thus not reported).
     *
     * It is a cache hit if this cache record's 'Access' property already contains all the
     * requested accesses contained in the given 'checkResult' ('checkResult.RequestedAccess' field).
     */
    bool Check(const AccessCheckResult *checkResult) const;
    
    /*!
     * Updates this record w.r.t. a given 'checkResult' (so that subsequently, given the same
     * 'checkResult', 'Check' returns True)
     */
    void Update(const AccessCheckResult *checkResult);

protected:

    bool init() override;
    void free() override;

public:
    
    inline RequestedAccess Access() const  { return requestedAccess_; }

    bool HasStrongerRequestedAccess(RequestedAccess access, int *outCacheAccess = nullptr) const;
    
    /*!
     * Atomically:
     *   (1) determines if the given 'checkResult' should be deemed a cache hit, and
     *   (2) if not, updates this record so that subsequently, the same 'checkResult' becomes a cache hit.
     *
     * @return Whether 'checkResult' was a cache hit.
     */
    bool CheckAndUpdate(const AccessCheckResult *checkResult);
    
#pragma mark Static Methods
    
    /*!
     * Factory method, following the OSObject pattern.
     *
     * First creates an object (by calling 'new'), then invokes 'init' on the newly create object.
     *
     * If new object cannot not be created, nullptr is returned.
     */
    static CacheRecord* create();
};

#endif /* CacheRecord_hpp */
