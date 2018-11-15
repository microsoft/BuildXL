//
//  Checkers.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef Checkers_hpp
#define Checkers_hpp

#include "ProcessObject.hpp"

typedef void (*CheckFunc)(PolicyResult policy, bool isDirectory, AccessCheckResult *result);

class Checkers
{
private:
    Checkers() {}
    
public:
    static CheckFunc CheckExecute;
    static CheckFunc CheckRead;
    static CheckFunc CheckReadNonexistent;
    static CheckFunc CheckWrite;
    static CheckFunc CheckProbe;
    static CheckFunc CheckReadWrite;
    static CheckFunc CheckEnumerateDir;
    static CheckFunc CheckCreateSymlink;
    static CheckFunc CheckCreateDirectory;
};

#endif /* Checkers_hpp */
