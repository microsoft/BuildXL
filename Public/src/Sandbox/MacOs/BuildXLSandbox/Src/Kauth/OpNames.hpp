//
//  OpNames.hpp
//  BuildXLSandbox
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef OpNames_h
#define OpNames_h

// This file is used to automatically generate a .cs file for the Windows builds.
//
// Every line that starts with "#define" which when split by " " yields 3 values
// (empty values being ignored) is picked up and turned into a public const C# definition.
//
// That said:
//   (1) don't add any macros to this file that do not correspond to ReportedFileOperation names,
//   (2) don't have any operation names that contains the " " character

#define OpProcessTreeCompleted      "ProcessTreeCompletedAck"

#define OpMacLookup                 "MAC_LOOKUP"
#define OpMacReadlink               "MAC_READLINK"
#define OpMacVNodeCreate            "MAC_VNODE_CREATE"

#define OpKAuthMoveSource           "FILEOP_RENAME_SOURCE"
#define OpKAuthMoveDest             "FILEOP_RENAME_DEST"
#define OpKAuthCreateHardlinkSource "FILEOP_LINK_SOURCE"
#define OpKAuthCreateHardlinkDest   "FILEOP_LINK_DEST"
#define OpKAuthCopySource           "FILEOP_EXCHANGE_SOURCE"
#define OpKAuthCopyDest             "FILEOP_EXCHANGE_DEST"
#define OpKAuthDeleteDir            "FILEOP_DELETE_DIR"
#define OpKAuthDeleteFile           "FILEOP_DELETE_FILE"
#define OpKAuthOpenDir              "FILEOP_OPEN_DIR"
#define OpKAuthReadFile             "FILEOP_OPEN_FILE"
#define OpKAuthCreateDir            "FILEOP_WRITE_DIR"
#define OpKAuthWriteFile            "FILEOP_WRITE_FILE"

#define OpKAuthVNodeExecute         "VNODE_EXECUTE"
#define OpKAuthVNodeWrite           "VNODE_WRITE"
#define OpKAuthVNodeRead            "VNODE_READ"
#define OpKAuthVNodeProbe           "VNODE_PROBE"

#endif /* OpNames_h */
