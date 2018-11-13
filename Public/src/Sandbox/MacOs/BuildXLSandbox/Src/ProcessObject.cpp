//
//  ProcessObject.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "ProcessObject.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ProcessObject, OSObject)

bool ProcessObject::init(pid_t clientPid, pid_t processPid, void *payload, unsigned int payloadSize)
{
    if (!super::init())
    {
        return false;
    }

    clientPid_        = clientPid;
    payloadSize_      = payloadSize;
    processId_        = processPid;
    hashCode_         = ProcessObject::computePidHashCode(processPid);
    processTreeCount_ = 1;

    fam_.init((BYTE*)payload, payloadSize);
    if (fam_.HasErrors())
    {
        log_error("Could not parse FileAccessManifest: %s", fam_.Error());
        return false;
    }

    reportedPathLookups_ = ConcurrentDictionary::withCapacity(kPathLookupCacheSize, "PathLookupCache");
    if (!reportedPathLookups_)
    {
        return false;
    }
    
    // Must not assign 'payload' to 'payload_' unless init succeeds; the reason:
    //   - payload is assigned to payload_
    //   - fam parsing fails, init returns false
    //   - withCapacity calls release on this object which calls IODelete on 'payload_'
    //   - the caller of withCapacity get back nullptr, thinks allocation failed, and calls IODelete on 'paylod' again
    // None of this would matter if payload was some kind of OSObject with reference counting.
    payload_ = payload;
    
    return true;
}

void ProcessObject::free()
{
    if (payload_)
    {
        IODelete(payload_, char, payloadSize_);
        payload_ = nullptr;
    }

    OSSafeReleaseNULL(hashCode_);
    OSSafeReleaseNULL(reportedPathLookups_);
    
    super::free();
}

bool ProcessObject::isAlreadyReported(const OSSymbol *key) const
{
    return reportedPathLookups_->get(key) != nullptr;
}

bool ProcessObject::addToReportCache(const OSSymbol *key)  const
{
    if (key == nullptr) return false;
    return reportedPathLookups_->insert(key, key);
}

const OSSymbol* ProcessObject::computeHashCode(const ProcessObject *process)
{
    if (!process) return nullptr;
    return ProcessObject::computePidHashCode(process->getProcessId());
}

const OSSymbol* ProcessObject::computePidHashCode(const pid_t pid)
{
    char key[10] = {0};
    snprintf(key, sizeof(key), "%d", pid);
    return OSSymbol::withCString(key);
}

ProcessObject* ProcessObject::withPayload(pid_t clientPid, pid_t processPid, void *payload, unsigned int payloadSize)
{
    ProcessObject *instance = new ProcessObject;
    if (instance == nullptr)
    {
        log_error("Failed to create a new ProcessObject (PID: %d) for Client (PID: %d)", processPid, clientPid);
        return nullptr;
    }
    
    bool initialized = instance->init(clientPid, processPid, payload, payloadSize);
    if (!initialized)
    {
        // init already logged an error message describing what failed
        OSSafeReleaseNULL(instance);
        return nullptr;
    }
    
    return instance;
}

pid_t ProcessObject::getParentProcessPid(pid_t pid)
{
    const int NOT_FOUND_PID = -1;

    // if root process or invalid --> return NOT_FOUND_PID
    if (pid <= 1) return NOT_FOUND_PID;

    proc_t proc = proc_find(pid);

    // if for whatever reason the process doesn't exist return NOT_FOUND_PID
    if (proc == nullptr) return NOT_FOUND_PID;

    pid_t parent_pid = proc_ppid(proc);
    proc_rele(proc);
    return parent_pid;
}

#undef super
