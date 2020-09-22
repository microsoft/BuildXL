// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef PathExtractor_h
#define PathExtractor_h

#include <vector>
#include <EndpointSecurity/EndpointSecurity.h>

class PathExtractor final
{

private:

    std::vector<char> buffer_;

    PathExtractor(const char *data, const size_t length)
    {
        assert(length < PATH_MAX);
        buffer_.insert(buffer_.begin(), data, data + length);
    }

public:

    PathExtractor() = delete;
    PathExtractor(es_string_token_t token) : PathExtractor(token.data, token.length) {}
    PathExtractor(es_file_t *file) : PathExtractor(file->path.data, file->path.length) {}

    ~PathExtractor()
    {
        buffer_.clear();
        std::vector<char>().swap(buffer_);
    }

    // Creates a PathInfo that concatenates directory and filename with a directory seperator char into a buffer
    PathExtractor(es_file_t *file, es_string_token_t token)
    {
        size_t fileLength = file->path.length;
        bool filePathIsRootOnly = fileLength == 1 && file->path.data[0] == '/';

        size_t tokenLength = token.length;
        size_t totalLength = fileLength + tokenLength + (filePathIsRootOnly ? 0 : 1);

        assert(totalLength < PATH_MAX);
        buffer_.insert(buffer_.begin(), file->path.data, file->path.data + fileLength);

        if (!filePathIsRootOnly)
        {
            buffer_.push_back('/');
        }

        buffer_.insert(buffer_.end(), token.data, token.data + tokenLength);
    }

    inline const std::string Path() { return std::string(buffer_.begin(), buffer_.end()); }
    inline const size_t PathLength() { return buffer_.size(); }
};

#endif /* PathExtractor_h */
