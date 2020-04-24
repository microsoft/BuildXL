// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#import "SystemExtensionManager.h"
#include "XPCTester.h"

@interface SystemExtensionManager ()

@property SystemExentsionAction action;
@property BOOL OSSystemExtensionRequestFinished;

@end

@implementation SystemExtensionManager

- (OSSystemExtensionReplacementAction)request:(OSSystemExtensionRequest *)request
                  actionForReplacingExtension:(OSSystemExtensionProperties *)old
                                withExtension:(OSSystemExtensionProperties *)new
{
    // Always replace the system extension when requested - this covers the updating mechanism
    return OSSystemExtensionReplacementActionReplace;
}

- (void)requestNeedsUserApproval:(OSSystemExtensionRequest *)request
{
    NSLog(@"OSSystemExtensionRequest needs Transparency, Consent, and Control (TCC) approval, please check your settings:\n%@", request.identifier);
}

- (void)request:(OSSystemExtensionRequest *)request didFailWithError:(NSError *)error
{
    NSLog(@"OSSystemExtensionRequest: %@, failed with error: %@", request.identifier, error);
    _OSSystemExtensionRequestFinished = YES;
}

- (void)request:(OSSystemExtensionRequest *)request didFinishWithResult:(OSSystemExtensionRequestResult)result
{
    NSLog(@"OSSystemExtensionRequest successfully finished for action: %@", [self descriptionForCurrentAction]);
    _OSSystemExtensionRequestFinished = YES;
}

- (NSString *)descriptionForCurrentAction
{
    switch (self.action)
    {
        case RegisterSystemExtension:
            return @"RegisterSystemExtension";
        case UnregisterSystemExtension:
            return @"UnregisterSystemExtension";
        default:
            return @"Unsupported action";
    }
}

- (bool)executeSystemExtensionOperationFor:(SystemExentsionAction)action
{
    if (action == None)
    {
        return NO;
    }
    
    _OSSystemExtensionRequestFinished = NO;
    
    if (@available(macOS 10.15, *))
    {
        dispatch_queue_t actionQueue = dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_HIGH, 0);
        OSSystemExtensionRequest *request = nil;
         
        switch (action)
        {
            case TestXPCConnection:
            case RegisterSystemExtension:
            {
                request = [OSSystemExtensionRequest activationRequestForExtension:systemExtensionIdentifier
                                                                            queue:actionQueue];
                break;
            }
            case UnregisterSystemExtension:
            {
                request = [OSSystemExtensionRequest deactivationRequestForExtension:systemExtensionIdentifier
                                                                              queue:actionQueue];
                break;
            }
            default:
                break;
        }
        
        if (request)
        {
            request.delegate = self;
            [[OSSystemExtensionManager sharedManager] submitRequest:request];
    
            do
            {
                [[NSRunLoop currentRunLoop] runUntilDate:[NSDate dateWithTimeIntervalSinceNow:1.0]];
            }
            while(!_OSSystemExtensionRequestFinished);
            
            if (action == TestXPCConnection)
            {
                // Does not return
                start_xpc_server();
            }
        }
        
        return _OSSystemExtensionRequestFinished;
    }
    else
    {
        NSLog(@"SystemExtension facilities are only available on macOS Catalina or newer (10.15+), exiting!");
        exit(EXIT_FAILURE);
    }
}

@end
