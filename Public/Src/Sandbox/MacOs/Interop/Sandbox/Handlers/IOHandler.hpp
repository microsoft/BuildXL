// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef IOHandler_hpp
#define IOHandler_hpp

#include "AccessHandler.hpp"

#define NO_ERROR 0

class PathInfo
{
private:
    
    typedef struct {
        char data[PATH_MAX] = { '\0' };
        size_t length;
    } Buffer;
    
    struct stat64 st_;
    Buffer buffer_;

    PathInfo(const char *data, const size_t length)
    {
        assert(length < PATH_MAX);
        char *end = (char *) memccpy(buffer_.data, data, '\0', length);
        if (end == NULL) buffer_.data[length] = '\0';
        buffer_.length = strlen(buffer_.data);
    }
    
public:

    PathInfo() = delete;
    PathInfo(es_string_token_t token) : PathInfo(token.data, token.length) {}
    PathInfo(es_file_t *file) : PathInfo(file->path.data, file->path.length)
    {
        st_ = file->stat;
    }

    // Creates a PathInfo that concatenates directory and filename with a directory seperator char into a buffer
    PathInfo(es_file_t *file, es_string_token_t token) : st_(file->stat)
    {
        size_t fileLength = file->path.length;
        bool filePathIsRootOnly = fileLength == 1 && file->path.data[0] == '/';
    
        size_t tokenLength = token.length;
        size_t totalLength = fileLength + tokenLength + (filePathIsRootOnly ? 0 : 1);

        assert(totalLength < PATH_MAX);

        char *end = (char *) memccpy(buffer_.data, file->path.data, '\0', fileLength);
        if (!filePathIsRootOnly)
        {
            if (end == NULL)
            {
                buffer_.data[fileLength] = '/';
            }
            else
            {
                *(end - 1) = '/';
                fileLength = end - buffer_.data;
            }
        }
        
        end = (char *) memccpy(buffer_.data + fileLength + (filePathIsRootOnly ? 0 : 1), token.data, '\0', token.length);
        if (end == NULL)
        {
            buffer_.data[totalLength] = '\0';
        }
        
        buffer_.length = strlen(buffer_.data);
    }

    ~PathInfo() {}

    inline const char *Path() { return buffer_.data; }
    inline const size_t PathLength() { return buffer_.length; }
    inline const struct stat64 Stat() { return st_; }
};

struct IOHandler : public AccessHandler
{
public:

    IOHandler(ESSandbox *sandbox) : AccessHandler(sandbox) { }

#pragma mark Process life cycle
    
    void HandleProcessFork(const es_message_t *msg);

    void HandleProcessExec(const es_message_t *msg);

    void HandleProcessExit(const es_message_t *msg);

    void HandleProcessUntracked(const pid_t pid);
    
#pragma mark Process I/O observation
    
    void HandleLookup(const es_message_t *msg);
    
    void HandleOpen(const es_message_t *msg);
    
    void HandleClose(const es_message_t *msg);

    void HandleCreate(const es_message_t *msg);
    
    void HandleLink(const es_message_t *msg);
    
    void HandleUnlink(const es_message_t *msg);
    
    void HandleReadlink(const es_message_t *msg);
    
    void HandleRename(const es_message_t *msg);
    
    void HandleClone(const es_message_t *msg);

    void HandleExchange(const es_message_t *msg);
    
    void HandleGenericWrite(const es_message_t *msg);
    
    void HandleGenericRead(const es_message_t *msg);
};

#endif /* IOHandler_hpp */
