// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef AutoIncDec_hpp
#define AutoIncDec_hpp

#include "BuildXLSandboxShared.hpp"

class AutoIncDec
{
private:

    volatile SInt32 *cnt_;
    SInt32 valueBeforeTheIncrement_;

public:

    /*!
     * Constructor: atomically increments a given int pointer and remembers the value before the increment.
     * The pointer is also remembers and is automatically decremented in the distructor.
     */
    AutoIncDec(volatile SInt32 *cnt)
    {
        cnt_ = cnt;
        valueBeforeTheIncrement_ = OSIncrementAtomic(cnt_);
    }

    /*! Returns the value before the increment. */
    SInt32 ValueBeforeTheIncrement()
    {
        return valueBeforeTheIncrement_;
    }

    /*!
     * Destructor: atomically decrements the remembered int pointer.
     */
    ~AutoIncDec()
    {
        OSDecrementAtomic(cnt_);
    }
};

#endif /* AutoIncDec_hpp */
