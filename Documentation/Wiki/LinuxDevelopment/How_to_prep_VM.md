# How to prepare a fresh Linux VM

## Install Prerequisites
### Ubuntu 22.04 and 24.04
```bash
# install packages
sudo apt-get update 
sudo apt-get install –y git build-essential libc6-dev openssh-server curl dotnet9 clang libelf-dev libnuma-dev

# link libdl.so
sudo ln -vs /lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so
```

### Install .NET
```
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get install -y dotnet-sdk-6.0 
```

## Install/configure git credentials provider

* follow https://github.com/GitCredentialManager/git-credential-manager#linux
* run `git config --global credential.credentialStore secretservice` (might require GUI)
* run `git config --global credential.helper cache && git config --global credential.helper 'cache --timeout=600000`

## Device code authentication
In order to do device code authentication in a Linux headless terminal, a security exception needs to be requested. Check this [link](https://eng.ms/docs/microsoft-security/ciso-organization/iamprotect/enterprise-iam/productivity-environment/tsgs/devicecodeflowdcfrestrictions) for details. Summarizing:
* Read the TSG linked above
* Watch the recommended training [video](https://microsoft.sharepoint.com/:v:/t/MicrosoftSecurityDayofLearningArchive/IQC2eIuoiE0rT7HzOObKnIjvATSBclBLrhVsuyypm9cAyBQ?e=lcPgIP)
* Request an exception [here](https://aka.ms/dcfexception) (this has to be renewed every 30 days)
* Connect to a Microsoft network (AzVPN, GSA, CorpNet, or Azure) before running any DCF-based sign-in.