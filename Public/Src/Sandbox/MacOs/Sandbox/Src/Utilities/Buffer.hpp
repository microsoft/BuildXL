// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Buffer_hpp
#define Buffer_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>
#include "BuildXLSandboxShared.hpp"

#define Buffer BXL_CLASS(Buffer)

/*!
 * A reference-counted buffer
 */
class Buffer : public OSObject
{
    OSDeclareDefaultStructors(Buffer);

private:

    char *buffer_;
    size_t size_;

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result True if successful, False otherwise.
     */
    bool init(size_t size);

protected:

    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;

public:

    char* getBytes() const { return buffer_; }
    size_t getSize() const { return size_; }

#pragma mark Static Methods

    /*!
     * Factory method, following the OSObject pattern.
     *
     * First creates an object (by calling 'new'), then invokes 'init' on the newly create object.
     *
     * If either of the steps fails, nullptr is returned.
     *
     * When object creation succeeds but initialization fails, 'release' is called on the created
     * object and nullptr is returned.
     */
    static Buffer* create(size_t size);
};

#endif /* Buffer_hpp */
