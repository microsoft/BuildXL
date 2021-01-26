# Rush performance tips

* Cache hits are one of the main sources of good performance. Being able to reuse outputs from previous builds is key. Take a look at section [Getting cache hits in a distributed scenario](js-cachehits.md) for additional guidance about this.
* [Filters](../How-To-Run-BuildXL/Filtering.md) are another way to scope down what is actually needed. 
     * Rush-based builds support regular spec filtering. E.g. ```bxl /f:spec='./spfx-tools/sp-build-node'``` will request a build where only `sp-build-node` project will be built, and its required dependencies.
     * Every script command is exposed as a build tag. This means that a pip executing a `'build'` script command will tagged with `'build'`, and equivalently for any arbitrary script command. This enables to easily filter by it. For example, if we want to exclude tests, we can run ```bxl /f:~(tag='test')```