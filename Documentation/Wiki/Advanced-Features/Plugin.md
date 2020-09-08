# Plugin Mode

Plugin mode is a way of providing the extensibilty of changing the default behavior in BuildXL. Plugin is running in a separate process. BuildXL commnuicate with plugin over grpc thus user can define rpc methods for their own plugins. 

## How does it works
* Pass `/enablePlugins+` command arguments to BuildXL to allow plugin mode
* Pass `/pluginPaths:<list of paths>` (each path can be seperated by `;`) command arguments to BuildXL to tell where to find plugins.
* BuildXL will load plugins one by one
* BuildXL will shutdown all plugins when build is done
* BuildXL will choose the first plugin that can handle the request
* BuildXL will have plugin client to commnuicate with each plugin over grpc(one client per plugin)

## Required Operations
Plugin implementation should conform a set of rpc operations:
1. `Start`: instruct plugin to start and load any necessary resources
1. `Stop`:  instruct plugin to stop and clean up
1. `SupportedOperation`: get operations that plugin support. This is used to register in BuildXL thus BuildXL can dispatch message accordingly. 
1. `Send`: send `PluginMessage` to plugin and expect to receive `PluginMessageResponse`. Both `PluginMessage` and `PluginMessageResponse` contain dynamic payload. Based on payload type in message received by plugin, it can infer type of request and repond accordingly. 
__Note:__ we don't support two plugins have overlapped `supportedMessageType`, one messageType per plugin. This restriction may be subject to change in future

## How to implement new plugin

### plugin client 
plugin client implmentation is in BuildXL codebase and it is supposed to bootstrap any plugin if you follow the requirements. Generally speaking, you don't need to make change to it, unless:
*a new type of plugin message 
*add a new type of plugin supported operation
*add new extensibile point

### plugin
Your plugin implementation should have implementations for handle those requrired rpc operations, see `LogParsePluginExample.cs` as an example