// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef OpNames_h
#define OpNames_h

// This file is used to automatically generate a .cs file for the Windows builds.
//
// Every line that matches the 'macro_to_apply\((.*),\s*(.*)\)' regular expression (assuming the C# regex syntax)
// is picked up and turned into a public const string C# definition.

#define FOR_ALL_OPERATIONS(macro_to_apply)                               \
  macro_to_apply(OpProcessStart,              "Process")                 \
  macro_to_apply(OpProcessExit,               "ProcessExit")             \
  macro_to_apply(OpProcessTreeCompleted,      "ProcessTreeCompletedAck") \
  macro_to_apply(OpMacLookup,                 "MAC_LOOKUP")              \
  macro_to_apply(OpMacReadlink,               "MAC_READLINK")            \
  macro_to_apply(OpMacVNodeCloneSource,       "MAC_VNODE_CLONE_SOURCE")  \
  macro_to_apply(OpMacVNodeCloneDest,         "MAC_VNODE_CLONE_DEST")    \
  macro_to_apply(OpMacVNodeCreate,            "MAC_VNODE_CREATE")        \
  macro_to_apply(OpMacVNodeWrite,             "MAC_VNODE_WRITE")        \
  macro_to_apply(OpKAuthMoveSource,           "FILEOP_RENAME_SOURCE")    \
  macro_to_apply(OpKAuthMoveDest,             "FILEOP_RENAME_DEST")      \
  macro_to_apply(OpKAuthCreateHardlinkSource, "FILEOP_LINK_SOURCE")      \
  macro_to_apply(OpKAuthCreateHardlinkDest,   "FILEOP_LINK_DEST")        \
  macro_to_apply(OpKAuthCopySource,           "FILEOP_EXCHANGE_SOURCE")  \
  macro_to_apply(OpKAuthCopyDest,             "FILEOP_EXCHANGE_DEST")    \
  macro_to_apply(OpKAuthDeleteDir,            "FILEOP_DELETE_DIR")       \
  macro_to_apply(OpKAuthDeleteFile,           "FILEOP_DELETE_FILE")      \
  macro_to_apply(OpKAuthOpenDir,              "FILEOP_OPEN_DIR")         \
  macro_to_apply(OpKAuthReadFile,             "FILEOP_OPEN_FILE")        \
  macro_to_apply(OpKAuthCreateDir,            "FILEOP_WRITE_DIR")        \
  macro_to_apply(OpKAuthWriteFile,            "FILEOP_WRITE_FILE")       \
  macro_to_apply(OpKAuthClose,                "FILEOP_CLOSE")            \
  macro_to_apply(OpKAuthCloseModified,        "FILEOP_CLOSE_MODIFIED")   \
  macro_to_apply(OpKAuthVNodeExecute,         "VNODE_EXECUTE")           \
  macro_to_apply(OpKAuthVNodeWrite,           "VNODE_WRITE")             \
  macro_to_apply(OpKAuthVNodeRead,            "VNODE_READ")              \
  macro_to_apply(OpKAuthVNodeProbe,           "VNODE_PROBE")

#define GEN_ENUM_CONST(name, value) k ## name,
enum FileOperation : char
{
    FOR_ALL_OPERATIONS(GEN_ENUM_CONST)
    kOpMax
};

// defined in OpNames.cpp
extern const char *OpNames[];

#endif /* OpNames_h */
