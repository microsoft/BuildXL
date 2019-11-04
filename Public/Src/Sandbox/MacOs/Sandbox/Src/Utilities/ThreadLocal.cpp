// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "ThreadLocal.hpp"
#include "BuildXLSandboxShared.hpp"
#include "SandboxedPip.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ThreadLocal, OSObject)

ThreadLocal* ThreadLocal::create()
{
    ThreadLocal *instance = new ThreadLocal;
    if (instance)
    {
        bool initialized = instance->init();
        if (!initialized)
        {
            instance->release();
            instance = nullptr;
        }
    }

    return instance;
}

bool ThreadLocal::init()
{
    if (!super::init())
    {
        return false;
    }

    dict_ = Trie::createUintTrie();
    if (dict_ == nullptr)
    {
        return false;
    }

    return true;
}

void ThreadLocal::free()
{
    if (dict_)
    {
        OSSafeReleaseNULL(dict_);
    }

    super::free();
}

#pragma mark count/insert/remove/get methods

bool ThreadLocal::insert(const OSObject *value)
{
    auto result = dict_->replace(self_tid(), value);
    return
        result == Trie::TrieResult::kTrieResultInserted ||
        result == Trie::TrieResult::kTrieResultReplaced;
}

bool ThreadLocal::remove()
{
    auto result = dict_->remove(self_tid());
    return result == Trie::TrieResult::kTrieResultRemoved;
}

OSObject* ThreadLocal::get() const
{
    return dict_->get(self_tid());
}
