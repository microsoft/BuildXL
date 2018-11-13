// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#pragma once

#include "FileAccessHelpers.h"

// Immutable, typed, and canonical path string. The represented path is absolute, free of .. and . traversals, redundant path separators, etc.
// A canonicalized path is indepenent of the current directory (which is mutable and process global).
// Since the path is immutable, the underlying storage for the path string is shared among instances under copy construction and assignment.
struct CanonicalizedPath {
    CanonicalizedPath()
        : m_value(nullptr), Type(PathType::Null)
    { }

    CanonicalizedPath(PathType type, wchar_t const* value, size_t valuePrefixLength)
        : m_value(std::make_shared<std::wstring>(value, valuePrefixLength)), Type(type)
    { }

    CanonicalizedPath(CanonicalizedPath&& other)
        : m_value(std::move(other.m_value)), Type(other.Type)
    {
        other.Type = PathType::Null;
    }

    CanonicalizedPath(const CanonicalizedPath& other) = default;
    CanonicalizedPath& operator=(const CanonicalizedPath&) = default;

    CanonicalizedPath Extend(wchar_t const* additionalComponents, size_t* extensionStartIndex = nullptr) const;
    CanonicalizedPath RemoveLastComponent() const;

    bool IsNull() const { return Type == PathType::Null; }

    size_t Length() const {
        return m_value ? m_value->length() : 0;
    }

    wchar_t const* GetPathString() const {
        return m_value ? m_value->c_str() : nullptr;
    }

    // Returns the path string with the type prefix (\\?\, \??\, or \\.\) omitted if present.
    wchar_t const* GetPathStringWithoutTypePrefix() const {
        switch (Type) {
        case PathType::Null:
            return nullptr;
        case PathType::Win32:
            return GetPathString();
        case Win32Nt:
        case LocalDevice:
            return GetPathString() + 4;
        default:
            assert(false);
            return nullptr;
        }
    }

    // Returns the suffix of the path string corresponding to the last component in the path.
    wchar_t const* GetLastComponent() const;

    // Attempts to canonicalize the given path. On failure, returns a path with IsNull() == true.
    static CanonicalizedPath Canonicalize(wchar_t const* noncanonicalPath);

    PathType Type;

private:
    // Private constructor for Canonicalize, which creates a wstring for m_value.
    // This constructor allocates a new shared_ptr managed string but moves the contents rather than copying them.
    CanonicalizedPath(PathType type, std::wstring&& value)
        : m_value(std::make_shared<std::wstring>(std::move(value))), Type(type)
    { }

    std::shared_ptr<std::wstring> m_value;
};