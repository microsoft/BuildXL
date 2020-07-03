// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BuildXLException_hpp
#define BuildXLException_hpp

#include <exception>
#include <string>

class BuildXLException : public std::exception
{

private:

    std::string message_;
    
public:

    BuildXLException() = delete;
    BuildXLException(const std::string& message) : message_(message) { }
    
    const char* what() const noexcept
    {
        return message_.c_str();
    }
};

#endif /* BuildXLException_hpp */
