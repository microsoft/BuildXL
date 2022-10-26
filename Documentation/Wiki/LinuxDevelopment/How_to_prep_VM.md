# How to prepare a fresh Linux VM

## Install Prerequisites
### Ubuntu 22.04
```bash
# install packages
sudo apt-get update 
sudo apt-get install –y git build-essential mono-devel mono-complete libc6-dev openssh-server curl dotnet6

# link libdl.so
sudo ln -vs /lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so
```

### Ubuntu 20.04
```bash
# install packages
sudo apt-get update 
sudo apt-get install –y git build-essential mono-devel mono-complete libc6-dev openssh-server curl 

# install .NET
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get install -y dotnet-sdk-6.0 
```

## Install/configure git credentials provider

* follow https://github.com/GitCredentialManager/git-credential-manager#linux
* run `git config --global credential.credentialStore secretservice` (might require GUI)
* run `git config --global credential.helper cache && git config --global credential.helper 'cache --timeout=600000`