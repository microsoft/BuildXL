// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Checkers_hpp
#define Checkers_hpp

#include "PolicyResult.h"

typedef void (*CheckFunc)(PolicyResult policy, bool isDirectory, AccessCheckResult *result);

class Checkers
{
private:
    Checkers() {}
    
public:
    static CheckFunc CheckExecute;
    static CheckFunc CheckRead;
    static CheckFunc CheckLookup;
    static CheckFunc CheckWrite;
    static CheckFunc CheckProbe;
    static CheckFunc CheckReadWrite;
    static CheckFunc CheckEnumerateDir;
    static CheckFunc CheckCreateSymlink;
    static CheckFunc CheckCreateDirectory;
};

#endif /* Checkers_hpp */
