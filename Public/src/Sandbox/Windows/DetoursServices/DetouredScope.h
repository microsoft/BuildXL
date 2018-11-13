// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

/*
Implements APIs involving ignoring contexts for detouring.
*/

#pragma once

// ----------------------------------------------------------------------------
// CLASSES
// ----------------------------------------------------------------------------

// DetouredScope
//
// Create a detouring scope.
// The goal of the scope is not detour any Windows APIs which are called as a result
// of already detoured APIs. There is no need to spend additional resources
// on applying BuildXL's access policy more than once.
class DetouredScope
{
private:
    static __declspec(thread) size_t gt_DetouredCount;

public:
    DetouredScope()
    {
        ++gt_DetouredCount;
    }

    ~DetouredScope()
    {
        --gt_DetouredCount;
    }

    // This function returns false except for the top level scope.
    // NOTE: This function is not static to ensure we always declare a scope.
    inline bool Detoured_IsDisabled() { return gt_DetouredCount != 1; }

private:
    // make copy-safe by explicitly deleting copy constructors
    DetouredScope(const DetouredScope &) = delete;
    DetouredScope& operator=(const DetouredScope &) = delete;
};
