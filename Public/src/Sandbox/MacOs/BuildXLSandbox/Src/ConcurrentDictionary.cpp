//
//  ConcurrentDictionary.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "ConcurrentDictionary.hpp"
#include "ProcessObject.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ConcurrentDictionary, OSObject)

ConcurrentDictionary* ConcurrentDictionary::withCapacity(uint capacity, const char *name)
{
    ConcurrentDictionary *instance = new ConcurrentDictionary;
    if (instance)
    {
        bool initialized = instance->init(capacity, name);
        if (!initialized)
        {
            OSSafeReleaseNULL(instance);
        }
    }

    if (!instance)
    {
        log_error("Failed to initialize '%s' dictionary with capacity %d", name, capacity);
    }

    return instance;
}

bool ConcurrentDictionary::init(uint capacity, const char *name)
{
    if (!super::init())
    {
        return false;
    }
    
    name_ = name;
    rwLock_ = IORWLockAlloc();
    dict_ = OSDictionary::withCapacity(capacity);

    return rwLock_ && dict_;
}

void ConcurrentDictionary::free()
{
    if (dict_)
    {
        dict_->flushCollection();
        OSSafeReleaseNULL(dict_);
    }

    if (rwLock_)
    {
        IORWLockFree(rwLock_);
        rwLock_ = nullptr;
    }

    super::free();
}

void ConcurrentDictionary::forEach(void *data, for_each_fn callback)
{
    EnterReadMonitor

    OSCollectionIterator *iterator = OSCollectionIterator::withCollection(dict_);
    iterator->reset();
    OSSymbol *key;
    int index = 0;
    while ((key = OSDynamicCast(OSSymbol, iterator->getNextObject())))
    {
        callback(data, index++, key, get(key));
    }
    iterator->release();
}

#define log_trace_dict_info(str, key, dict)
    // log_debug("%s :: %s :: key = '%s', dict count = %d", name_, str, key, dict->getCount())

bool ConcurrentDictionary::insert(const OSSymbol *key, const OSObject *value)
{
    if (!key) return false;

    EnterWriteMonitor

    log_trace_dict_info("before insert", key->getCStringNoCopy(), dict_);
    int oldCount = dict_->getCount();
    bool inserted = dict_->setObject(key, value);
    int newCount = dict_->getCount();
    log_trace_dict_info("after insert", key->getCStringNoCopy(), dict_);

    if (!inserted)
    {
        log_error("Failed to insert into '%s' dict (key: '%s', count: %d)", name_, key->getCStringNoCopy(), getCount());
    }

    return newCount > oldCount;
}

bool ConcurrentDictionary::remove(const OSSymbol *key)
{
    if (!key) return false;

    EnterWriteMonitor

    log_trace_dict_info("before remove", key->getCStringNoCopy(), dict_);
    int oldCount = dict_->getCount();
    dict_->removeObject(key);
    int newCount = dict_->getCount();
    log_trace_dict_info("after remove", key->getCStringNoCopy(), dict_);

    return newCount < oldCount;
}

void ConcurrentDictionary::flushCollection()
{
    EnterWriteMonitor

    dict_->flushCollection();
}

OSObject* ConcurrentDictionary::get(const OSSymbol *key) const
{
    if (!key || dict_->getCount() == 0) return nullptr;

    EnterReadMonitor

    return dict_->getObject(key);
}

bool ConcurrentDictionary::insertProcess(const ProcessObject *process)
{
    if (!process) return false;
    return insert(process->getHashCode(), process);
}

bool ConcurrentDictionary::removeProcess(pid_t pid)
{
    const OSSymbol *key = ProcessObject::computePidHashCode(pid);
    bool result = remove(key);
    OSSafeReleaseNULL(key);
    return result;
}

ProcessObject* ConcurrentDictionary::getProcess(pid_t pid)
{
    if (dict_->getCount() == 0) return nullptr;

    const OSSymbol *key = ProcessObject::computePidHashCode(pid);
    ProcessObject *result = OSDynamicCast(ProcessObject, get(key));
    OSSafeReleaseNULL(key);
    return result;
}

#undef super
