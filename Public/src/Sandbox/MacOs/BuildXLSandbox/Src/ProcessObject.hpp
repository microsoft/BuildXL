//
//  ProcessObject.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef ProcessObject_hpp
#define ProcessObject_hpp

#include <IOKit/IOLib.h>
#include <IOKit/IOService.h>
#include <IOKit/IOSharedDataQueue.h>
#include <sys/proc.h>
#include <sys/vnode.h>

#include "BuildXLSandboxShared.hpp"
#include "FileAccessManifestParser.hpp"
#include "PolicyResult.h"
#include "ConcurrentDictionary.hpp"
#include "ThreadLocal.hpp"

#define kPathLookupCacheSize 1024
#define kThreadLocalLookupSize 10

class ProcessObject : public OSObject
{
    OSDeclareDefaultStructors(ProcessObject);

private:

    pid_t clientPid_;
    pid_t processId_;
    void *payload_;
    unsigned int payloadSize_;
    FileAccessManifestParseResult fam_;
    const OSSymbol *hashCode_;
    int processTreeCount_;
    ConcurrentDictionary *reportCache_;
    ThreadLocal *lastPathLookup_;

    uint32_t numCacheHits_;
    uint32_t numCacheMisses_;

public:

    bool init(pid_t clientPid, pid_t processPid, void *payload, unsigned int payloadSize);
    void free() override;

    const OSSymbol* getHashCode() const { return hashCode_; }
    pid_t getClientPid() const          { return clientPid_; }
    pid_t getProcessId() const          { return processId_; }
    void *getPayload() const            { return payload_; }
    int getPayloadSize() const          { return payloadSize_; }
    pipid_t getPipId() const            { return fam_.GetPipId()->PipId; }

    FileAccessManifestParseResult getFAM() const { return fam_; }
    FileAccessManifestFlag getFamFlags() const   { return fam_.GetFamFlags(); }
    
    void setLastLookedUpPath(const OSSymbol *path)
    {
        lastPathLookup_->insert(path);
    }
    
    const char* getLastLookedUpPath()
    {
        OSSymbol *value = OSDynamicCast(OSSymbol, lastPathLookup_->get());
        return value != nullptr ? value->getCStringNoCopy() : nullptr;
    }
    
    PipInfo Introspect() const;

#pragma mark Process Tree Tracking

    int getProcessTreeCount() const  { return processTreeCount_; }
    bool hasEmptyProcessTree() const { return OSCompareAndSwap(0, 0, &processTreeCount_); }
    /*! Returns the value before increment. */
    int incrementProcessTreeCount() { return OSIncrementAtomic(&processTreeCount_); }
    /*! Returns the value before decrement. */
    int decrementProcessTreeCount() { return OSDecrementAtomic(&processTreeCount_); }

#pragma mark Report Caching

    // All report caching operations happen on the same process, but
    // they could happen on different threads (double check) hence locking is required

    bool isAlreadyReported(const OSSymbol *key) const;
    bool addToReportCache(const OSSymbol *key) const;

#pragma mark Static Methods

    /*! Factory method. The caller is responsible for releasing the returned symbol */
    static ProcessObject* withPayload(pid_t clientPid, pid_t processPid, void *payload, unsigned int payloadSize);

    /*! The caller is responsible for releasing the returned symbol */
    static const OSSymbol* computeHashCode(const ProcessObject *process);

    /*! The caller is responsible for releasing the returned symbol */
    static const OSSymbol* computePidHashCode(pid_t pid);
    
    /*! The caller is responsible for releasing the returned symbol */
    static const OSSymbol* computeTidHashCode(uint64_t tid);
    
    /*! The caller is responsible for releasing the returned symbol */
    static const OSSymbol* computeCurrentTidHashCode();

    /*! Given a PID it returns its parent's PID or -1 if not found. */
    static pid_t getParentProcessPid(pid_t pid);
};

#endif /* ProcessObject_hpp */
