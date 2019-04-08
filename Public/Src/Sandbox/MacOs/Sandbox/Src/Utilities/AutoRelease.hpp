// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef AutoRelease_hpp
#define AutoRelease_hpp

#include <IOKit/IOLib.h>

class AutoRelease
{
private:

    OSObject *obj_;

public:

    /*!
     * Constructor: remembers a given object to be released in this object's destructor.
     */
    AutoRelease(OSObject *obj)
    {
        obj_   = obj;
    }

    /*!
     * Destructor: releases the object supplied in the constructor
     */
    ~AutoRelease()
    {
        OSSafeReleaseNULL(obj_);
    }
};

#endif /* AutoRelease_hpp */
