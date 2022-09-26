#!/bin/bash

arg_InstallOptional=""
arg_HyperV=""

debugprint() {
    local message=$@
    NoColour='\033[0m'
    Green='\033[0;32m'
    echo -e "${Green}[INFO]${NoColour} ${message}"
}

parseArgs() {
    while [[ $# -gt 0 ]]; do
        cmd="$1"
        case $cmd in
        --install-optional | -o)
            arg_InstallOptional="1"
            shift
            ;;
        --hyperv | -v)
            arg_HyperV="1"
            shift
            ;;
        *)
            shift
            ;;
        esac
    done
}

parseArgs $@

# On hyper-v machines, we want to auto expand the root partition
if [[ -n "$arg_HyperV" ]]; then
    # /dev/sda1 should be the root partition on the hyper-v quick created machines
    debugprint "Expanding /dev/sda1 to take up remaining unallocated space"
    debugprint "Installing cloud-guest-utils"
    sudo apt install -y cloud-guest-utils

    debugprint "Running growpart on /dev/sda1"
    sudo growpart /dev/sda 1

    debugprint "Running resize2fs on /dev/sda1"
    sudo resize2fs /dev/sda1
fi

mkdir ~/git
cd ~/git

# Required packages
debugprint "Installing required packages"
sudo apt update -y
sudo apt install -y git build-essential mono-devel mono-complete libc6-dev openssh-server curl

# When using [DllImport("libdl")] in C#, the C# runtime can't find the libdl library
# if there is only libdl.so.2 file there on disk. So we help it out by creating this symlink.
. /etc/lsb-release
if [[ "$DISTRIB_RELEASE" == "22.04" ]]; then
    debugprint "Attempting to symlink libdl.so"
    sudo ln -vs /lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so
    sudo apt install -y dotnet6
fi

if [[ "$DISTRIB_RELEASE" == "20.04" ]]; then
    wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    sudo dpkg -i packages-microsoft-prod.deb
    rm packages-microsoft-prod.deb

    sudo apt-get update -y
    sudo apt-get install -y dotnet-sdk-6.0
fi

# Git credential manager
debugprint "Installing Git Credential Manager"
wget https://github.com/GitCredentialManager/git-credential-manager/releases/download/v2.0.785/gcm-linux_amd64.2.0.785.deb
sudo apt install -y ./gcm-linux_amd64.2.0.785.deb
rm gcm-linux_amd64.2.0.785.deb

debugprint "Configuring Git Credential Manager to use Secret Service for credential caching and setting timeout to 600000"
git-credential-manager-core configure
git config --global credential.credentialStore secretservice --replace-all
git config --global credential.helper 'cache --timeout=600000' --replace-all

# Clone the BuildXL repo
debugprint "Cloning BuildXL.Internal repository to ~/git/BuildXL.Internal"
debugprint "Please sign into your Microsoft account when the interactive authentication Window opens in Firefox"
git clone https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.Internal

# Optional quality of life tweaks
if [[ -n "$arg_InstallOptional" ]]; then
    debugprint "Installing zsh and ohmyzsh"
    sudo apt install -y zsh
    sh -c "$(curl -fsSL https://raw.github.com/ohmyzsh/ohmyzsh/master/tools/install.sh)" "" --unattended

    # Set zsh to the default shell
    chsh -s $(which zsh)

    # bindkey for ctrl + backspace to delete an entire word
    echo "bindkey '^H' backward-kill-word" >> ~/.zshrc

    debugprint "Installing Visual Studio Code"
    wget -qO- https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > packages.microsoft.gpg
    sudo install -D -o root -g root -m 644 packages.microsoft.gpg /etc/apt/keyrings/packages.microsoft.gpg
    sudo sh -c 'echo "deb [arch=amd64,arm64,armhf signed-by=/etc/apt/keyrings/packages.microsoft.gpg] https://packages.microsoft.com/repos/code stable main" > /etc/apt/sources.list.d/vscode.list'
    rm -f packages.microsoft.gpg

    sudo apt install -y apt-transport-https
    sudo apt update -y
    sudo apt install -y code

    debugprint "Installing glogg"
    sudo apt install -y glogg
fi