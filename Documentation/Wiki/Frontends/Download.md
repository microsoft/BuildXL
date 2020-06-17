The download frontend takes care of downloading files and extracting them.

## Configuration
You configure the download resolver by specifying a list of downloads:
An example is:
```
{
  kind: "Download",
  downloads: [
    // PowerShell.Core
    {
      moduleName: "PowerShell.Core.win-x64",
      url: "https://github.com/PowerShell/PowerShell/releases/download/v6.1.3/PowerShell-6.1.3-win-x64.zip",
      hash: "VSO0:E8E98155383EDFE3CA6D06854638560EAB57C8225880B5308547A916DBE9A9A900",
      archiveType: "zip",
    },
  ],
},
```

Each download has two required fields: `moduleName` and `url`. 
The `moduleName` is the name of the module available to other frontends like DScript. 
The `url` is the url to download from.

There are several optional fields:
* `hash`: This is the VSO-Hash we use internally in BuildXL. It is highly recommended to specify this one. See the section on Security why. If you are adding a new download you can use: `VSO0:000000000000000000000000000000000000000000000000000000000000000000` and the error message will say there is no match and gives you the value you can put in.
* `archiveType`: This specifies the type of file. valid values are:
  * `file`: This implies the download file is not extracted
  * `zip`: This implies the file is a zip archive
  * `gzip`: This is the Gzip algorithm. Note this assumes a single file as gzip doesn't support multiple files
  * `tgz`: This is a gziped version of the tar archive. The file will be un-g-zipped and then the tar file will be extracted into individual files
  * `tar`: This is a Tape Archive format where there is no compression but all the files are concatenated with file structure metadata. 
* `filename`: By default the filename will be inferred from the URL, but sometimes the URL is not a valid filename or plain ugly like: http://go.microsoft.com/fwlink/p/?LinkID=2033686

## Using the output.
If you want to use the file (or the extracted) archive the download will expose a module with two fields:
* `file` which if of type `File`
* `extracted` which is of type `StaticDirectory`. 
The extracted field is a Partial Sealed Directory.
When the archiveType is `file` the extracted folder will contain just the downloaded file.

An example consumption of the one above would be:
```
const pwshExecutable = importFrom("PowerShell.Core.win-x64").extracted.getFile(r`pwsh.exe`);
```


##Motivation for the download resolver
Not all a builds dependencies are nicely placed in a packaging system. Very frequently there are downloads that users have to install or downloads with binaries. 
Often people go with the quick and dirty way of depending on just all developers installing it.
You then end up with large wiki pages with instructions of things to install. The problems with installing are that users are often run into things like: "It works on my machine, why doesn't it work in the lab" or "I pulled in the latest changes and someone broke the build". Where the lab just had a slightly different version than they did. The other problem is that sometimes tools can only have a single version installed. So if you work on codebase X you have to have version 1.2.3 and any other version breaks codebase X. Now if you want to switch to codebase Y it has a dependency on 2.3.4 or higher. Each time you switch codebases you are now forced to uninstall and reinstall tools because they don't work side by side.

It is therefore highly discourage to have a build that depends on as little installed state as possible. Depending on the level of constraint you can of course decide where the boundary lies for your team. If you only have 5 developers, all in the same time zone that breath the codebase every day you can go with installed state since breaks are discovered fast and can be communicated quickly. So you can even choose to have things like compilers, SDKs etc installed on the system. 
But the moment your team gets spaced out geographically, you have more than 30 contributors or you have infrequent contributors like in an OSS project, the build should be as self-contained as possible as there will always be a developer that is not fully in sync. Especially if you are an OSS project. If you have a very long list of instructions to build in your `contribute.md` file you can loose a lot of potential contributions.

The BuildXL team made the call that for our need depending on an installed Operating System and installed .Net Framework is the right boundary. We want everything else to be pulled in by the build system with specific versions that are validated to be correct. Examples where we use this is the download of PowerShell.Core release. It is not part of the OS and there is no published version in any package management system. There are only downloadable installers and downloadable binaries.


## Security and reliability consideration
It is highly advised to encode the hash of the download in the configuration. It is optional and the engine will download the file and use it which is very convenient, but you are at the mercy of the source where you are downloading from to guarantee they will forever and ever return the same bits you verified works with your code and does not contain potential virus' or malware.

The second would be longevity. Package managers like Nuget, Npm, Maven etc try to provide some guarantees that all the packages and all their versions are archived for ever. Download sites have no such convention and the download might be taken down at any point in the future. There is also no hard convention that newer version will never be placed behind the same URL instead of a different one.
This has to be take into consideration in case you have to build an old version of your project. Be it for patching an issue to an old release. Bisecting your codebase to hunt down where a bug was introduced etc. If the download is no longer available or the same the code will not build. So make sure you can live without the resiliency and guidance of package managers, you have implicit trust in the source you are downloading from, or have a backup/fallback plan ready when the download becomes no longer available. 
A good use case study what can happen when something is no longer available is the an [npm left-pad incident](https://www.bing.com/search?q=npm+left-pad+incident&qs=LS&pq=npm+left-pad&sc=2-12&cvid=7906908C8D87435B854D750347356F2D&FORM=QBRE&sp=1)