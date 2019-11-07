// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Alloc_hpp
#define Alloc_hpp

#include <IOKit/IOLib.h>
#include <IOKit/IOService.h>
#include "BuildXLSandboxShared.hpp"

class Alloc
{
private:

    static int64_t currentAllocBytes_;

    Alloc() {}

public:

    template <class Type>
    static Type* New(size_t count)
    {
        Type *result = IONew(Type, count);
        if (result)
        {
            OSAddAtomic64(sizeof(Type) * count, &currentAllocBytes_);
        }
        return result;
    }

    template <class Type>
    static void Delete(Type* ptr, size_t count)
    {
        IODelete(ptr, Type, count);
        OSAddAtomic64(-(sizeof(Type) * count), &currentAllocBytes_);
    }

    /*!
     * Returns the number of currently allocated bytes.
     */
    static int64_t numCurrentlyAllocatedBytes()
    {
        return currentAllocBytes_;
    }
};

#endif /* Alloc_hpp */
