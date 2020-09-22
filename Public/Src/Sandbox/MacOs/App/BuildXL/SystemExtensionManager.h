// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#import <SystemExtensions/SystemExtensions.h>

typedef enum
{
    RegisterSystemExtension,
    UnregisterSystemExtension,
    TestXPCConnection,
    None,
} SystemExentsionAction;

static NSString *systemExtensionIdentifier = @"com.microsoft.buildxl.extension";

@interface SystemExtensionManager : NSObject <OSSystemExtensionRequestDelegate>

- (bool)executeSystemExtensionOperationFor:(SystemExentsionAction)action;

@end
