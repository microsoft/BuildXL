It can be painful to have to build a list of source files to create a [Sealed Directory](../Sealed-Directories.md). This can be because the list is large or simply because it changes frequently. BuildXL therefore has a feature called Source Sealed Directories.

For all intents and purposes they behave almost exactly the same as Full Sealed Directories with the caveat that you don't have to list all the files in the directory.

For this to be safe it means that this can only be a source directory. In other words, no pip is allowed to write output files into that directory.

For performance reasons Source Sealed Directories do not enumerate all the files from the file system at graph construction time, it does this as the pips read files from that folder. As a result the `contents` field of the `SealedDirectory` object is empty for these just like for [Opaque Sealed Directories](./Opaque-Sealed-Directories.md).

If you want access to the list of files at graph construction time, you will need to use a regular fully sealed directory and produce the list of files using  [`glob`](/BuildXL/User-Guide/Script/Globbing) and [`globR`](/BuildXL/User-Guide/Script/Globbing) .

Users can create two types of Source Sealed Directories, one that only allows the files in the directory by using the `topDirectoryOnly` option. If all files recursively should be part of this Sealed Directory you can use the `allDirectories` option.

```ts
// Source: These seal all the files in the source tree. No pip is allowed to write to that folder. The contents field is empty after creation.
const sourceTop = Transformer.sealSourceDirectory(d`dir1`, SealSourceDirectoryOption.topDirectoryOnly);
const sourceAll = Transformer.sealSourceDirectory(d`dir2`, SealSourceDirectoryOption.allDirectories);
```
