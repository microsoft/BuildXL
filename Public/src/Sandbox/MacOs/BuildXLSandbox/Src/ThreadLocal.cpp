//
//  ConcurrentDictionary.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "ThreadLocal.hpp"
#include "BuildXLSandboxShared.hpp"
#include "ProcessObject.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ThreadLocal, OSObject)

ThreadLocal* ThreadLocal::withCapacity(uint capacity, const char *name)
{
    ThreadLocal *instance = new ThreadLocal;
    if (instance)
    {
        bool initialized = instance->init(capacity, name);
        if (!initialized)
        {
            instance->release();
            instance = nullptr;
        }
    }
    
    if (!instance)
    {
        log_error("Failed to initialize '%s' ThreadLocal with capacity %d", name, capacity);
    }
    
    return instance;
}

bool ThreadLocal::init(uint capacity, const char *name)
{
    if (!super::init())
    {
        return false;
    }

    dict_ = ConcurrentDictionary::withCapacity(capacity, name);
    
    return dict_ != nullptr;
}

void ThreadLocal::free()
{
    if (dict_)
    {
        dict_->flushCollection();
        OSSafeReleaseNULL(dict_);
    }

    super::free();
}

#pragma mark count/insert/remove/get methods

uint ThreadLocal::getCount()
{
    return dict_->getCount();
}

bool ThreadLocal::insert(const OSObject *value)
{
    const OSSymbol *key = ProcessObject::computeCurrentTidHashCode();
    bool result = dict_->insert(key, value);
    OSSafeReleaseNULL(key);
    return result;
}

bool ThreadLocal::remove()
{
    const OSSymbol *key = ProcessObject::computeCurrentTidHashCode();
    bool result = dict_->remove(key);
    OSSafeReleaseNULL(key);
    return result;
}

OSObject* ThreadLocal::get() const
{
    const OSSymbol *key = ProcessObject::computeCurrentTidHashCode();
    OSObject *result = dict_->get(key);
    OSSafeReleaseNULL(key);
    return result;
}
