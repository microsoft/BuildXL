// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef MemoryStreams_h
#define MemoryStreams_h

struct imemorybuffer : std::streambuf
{
    imemorybuffer(const char* backing_store, size_t size)
    {
        char* buffer(const_cast<char*>(backing_store));
        this->setg(buffer, buffer, buffer + size);
    }
};

struct imemorystream final : virtual imemorybuffer, std::istream
{
    using std::istream::getloc;
    using std::istream::imbue;

    imemorystream(const char* mem, size_t size) : imemorybuffer(mem, size), std::istream(static_cast<std::streambuf*>(this)) { }
};

struct omemorybuffer : std::streambuf
{
    omemorybuffer(char* backing_store, size_t size)
    {
        this->setp(backing_store, backing_store + size);
    }
};

struct omemorystream final : virtual omemorybuffer, std::ostream
{
    omemorystream(char* backing_store, size_t size) : omemorybuffer(backing_store, size), std::ostream(static_cast<std::streambuf*>(this)) { }
};

struct PipeDelimiter final : std::ctype<char>
{
    PipeDelimiter() : std::ctype<char>(get_table()) {}
    
    static mask const* get_table()
    {
        static mask rc[table_size];
        
        // Use '|' and '\n' as delimiters instead of the default ' ' (space)
        rc['|'] = std::ctype_base::space;
        rc['\n'] = std::ctype_base::space;
        
        return &rc[0];
    }
};

#endif /* MemoryStreams_h */
