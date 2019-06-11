// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

/// Checks whether substitute process injection is enabled and injects the shim process
/// if this child process matches requirements. Returns true in injectedShim if the
/// substitution was performed and the caller should avoid running the real child process.
BOOL WINAPI MaybeInjectSubstituteProcessShim(
    _In_opt_    LPCWSTR               lpCommandLine,
    _In_opt_    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    _In_opt_    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    _In_        BOOL                  bInheritHandles,
    _In_        DWORD                 dwCreationFlags,
    _In_opt_    LPVOID                lpEnvironment,
    _In_opt_    LPCWSTR               lpCurrentDirectory,
    _In_        LPSTARTUPINFOW        lpStartupInfo,
    _Out_       LPPROCESS_INFORMATION lpProcessInformation,
    _Out_       bool&                 injectedShim);
