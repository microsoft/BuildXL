The HelloWorld example defines a build that prints "Hello World" to a file.

To execute the example:
1. cd Example\HelloWorld
2. Set the environment variable "BUILDXL_BIN" to the the directory where BuildXL binaries and "bxl.exe" can be found
3. Execute "<Path to BUILDXL_BIN>\bxl.exe /c:config.dsc"

This will produce an output file underneath "Out\Bin\file.out"

Build organization:
Configuration ("config.dsc")
└───'HelloWorld' Module ("module.config.dsc")
    └───Project ("Hello.World.Project.dsc")
└───'Sdk.Transformers' Module (defined by SDK)