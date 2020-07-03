// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef XPCConstants_h
#define XPCConstants_h

enum XPCCommands : unsigned int
{
    xpc_response_error = 0,
    xpc_response_success = 0xFA,
    xpc_response_failure,
    xpc_response_mute_process,
    
    xpc_get_detours_connection,
    xpc_set_detours_connection,
    xpc_kill_detours_connection,
    
    xpc_get_es_connection,
    xpc_set_es_connection,
    xpc_kill_es_connection,
};

#endif /* XPCConstants_h */
