#import <Foundation/Foundation.h>
#include "XPCConstants.hpp"

// This is useful when manually injecting the detours dylib or wanting to inspect ES client observation events
// on arbitrary processes. Just change the 'mode' setting below.

void start_xpc_server(void)
{
    int mode = xpc_set_detours_connection;

    char queue_name[PATH_MAX] = { '\0' };
    sprintf(queue_name, "com.microsoft.buildxl.xpctester.eventqueue_%d", getpid());

    dispatch_queue_t eventQueue_ = dispatch_queue_create(queue_name, dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INTERACTIVE, -1
    ));

    xpc_connection_t listener = xpc_connection_create_mach_service("com.microsoft.buildxl.sandbox", NULL, 0);
    xpc_connection_t tester = xpc_connection_create(NULL, NULL);

    xpc_connection_set_event_handler(listener, ^(xpc_object_t peer)
    {
        xpc_type_t type = xpc_get_type(peer);
        if (type == XPC_TYPE_ERROR)
        {
            const char *desc = xpc_copy_description(peer);
            NSLog(@"%s", desc);
            exit(EXIT_FAILURE);
        }
    });

    xpc_connection_set_event_handler(tester, ^(xpc_object_t peer)
    {
        xpc_connection_set_event_handler((xpc_connection_t)peer, ^(xpc_object_t message)
        {
           xpc_type_t type = xpc_get_type(message);
           if (type == XPC_TYPE_DICTIONARY)
           {
               const char *msg = xpc_dictionary_get_string(message, "IOEvent");
               const uint64_t msg_length = xpc_dictionary_get_uint64(message, "IOEvent::Length");

               NSLog(@"%.*s\n",(int)msg_length, msg);

               xpc_object_t reply = xpc_dictionary_create_reply(message);
               xpc_dictionary_set_uint64(reply, "response", xpc_response_success);
               xpc_connection_send_message((xpc_connection_t) peer, reply);

           }
           else if (type == XPC_TYPE_ERROR)
           {
               const char *desc = xpc_copy_description(message);
               
               if (message == XPC_ERROR_CONNECTION_INTERRUPTED)
               {
                   NSLog(@"Connection interrupted: %s", desc);
                   exit(EXIT_FAILURE);
               }
               else if (message == XPC_ERROR_CONNECTION_INVALID)
               {
                   NSLog(@"Client disconnected: %s", desc);
               }
           }
       });
       xpc_connection_resume((xpc_connection_t)peer);
    });

    xpc_connection_set_target_queue(tester, eventQueue_);
    xpc_connection_resume(tester);

    xpc_connection_resume(listener);

    xpc_object_t post = xpc_dictionary_create(NULL, NULL, 0);
    xpc_dictionary_set_uint64(post, "command", mode);
    xpc_dictionary_set_connection(post, "connection", tester);

    xpc_object_t respone = xpc_connection_send_message_with_reply_sync(listener, post);
    xpc_type_t type = xpc_get_type(respone);

    if (type == XPC_TYPE_ERROR)
    {
        exit(EXIT_FAILURE);
    }

    // Won't exit - in xpc test mode BuildXL has to be force quit
    dispatch_main();
}
