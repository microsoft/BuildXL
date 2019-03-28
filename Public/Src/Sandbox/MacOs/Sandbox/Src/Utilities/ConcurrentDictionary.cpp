// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "ConcurrentDictionary.hpp"

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

    return instance;
}

bool ConcurrentDictionary::init(uint capacity, const char *name)
{
    if (!super::init())
    {
        return false;
    }
    
    name_             = name;
    rwLock_           = IORWLockAlloc();
    dict_             = OSDictionary::withCapacity(capacity);
    onChangeData_     = nullptr;
    onChangeCallback_ = nullptr;

    return rwLock_ && dict_;
}

void ConcurrentDictionary::free()
{
    if (dict_)
    {
        flushCollection();
        OSSafeReleaseNULL(dict_);
    }

    if (rwLock_)
    {
        IORWLockFree(rwLock_);
        rwLock_ = nullptr;
    }
    
    onChangeData_     = nullptr;
    onChangeCallback_ = nullptr;

    super::free();
}

uint ConcurrentDictionary::getCount() const
{
    EnterReadMonitor
    return dict_->getCount();
}

void ConcurrentDictionary::forEach(void *data, for_each_fn callback) const
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

void ConcurrentDictionary::TriggerOnChange(int oldCount, int newCount) const
{
    if (onChangeCallback_ && oldCount != newCount)
    {
        onChangeCallback_(onChangeData_, oldCount, newCount);
    }
}

bool ConcurrentDictionary::onChange(void *data, on_change_fn callback)
{
    EnterWriteMonitor

    if (onChangeCallback_) return false;
    onChangeData_ = data;
    onChangeCallback_ = callback;
    return true;
}

bool ConcurrentDictionary::insert(const OSSymbol *key, const OSObject *value)
{
    if (!key) return false;

    EnterWriteMonitor

    log_trace_dict_info("before insert", key->getCStringNoCopy(), dict_);
    int oldCount = dict_->getCount();
    dict_->setObject(key, value);
    int newCount = dict_->getCount();
    log_trace_dict_info("after insert", key->getCStringNoCopy(), dict_);

    TriggerOnChange(oldCount, newCount);

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

    TriggerOnChange(oldCount, newCount);

    return newCount < oldCount;
}

void ConcurrentDictionary::flushCollection()
{
    EnterWriteMonitor

    int oldCount = dict_->getCount();
    dict_->flushCollection();
    int newCount = dict_->getCount();
    
    TriggerOnChange(oldCount, newCount);
}

OSObject* ConcurrentDictionary::get(const OSSymbol *key) const
{
    EnterReadMonitor

    if (key == nullptr || dict_ == nullptr || dict_->getCount() == 0) return nullptr;
    return dict_->getObject(key);
}

#undef super
