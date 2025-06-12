#!/bin/bash

arg_InstallOptional=""
arg_HyperV=""

. /etc/lsb-release
operatingSystem=$(echo "$DISTRIB_ID" | tr '[:upper:]' '[:lower:]')

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

installPackages() {
    debugprint "Installing $@"
    case "$operatingSystem" in
        "ubuntu")
            sudo apt update -y
            sudo apt install -y $@
        ;;
        "mariner" | "azurelinux")
            sudo dnf install -y -v $@
        ;;
    esac
}

parseArgs $@

# On hyper-v machines, we want to auto expand the root partition
# currently not necessary to do this on AzureLinux because prebuilt vhds aren't provided
if [[ -n "$arg_HyperV" && "$operatingSystem" == "ubuntu" ]]; then
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
debugprint "Required packages:"
    case "$operatingSystem" in
        "ubuntu")            
            if [[ "$DISTRIB_RELEASE" == "20.04" ]]; then
                wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
                sudo dpkg -i packages-microsoft-prod.deb
                rm packages-microsoft-prod.deb
            fi

            installPackages "build-essential" "libc6-dev" "openssh-server" "curl" "dotnet-sdk-8.0" "libelf1" "libelf-dev" "zlib1g-dev" "git" "clang"
        ;;
        "mariner" | "azurelinux")
            installPackages "rsync" "glibc-static.x86_64" "time" "dotnet-sdk-8.0" "clang" "elfutils-libelf" "elfutils-libelf-devel" "zlib-devel"

        ;;
    esac

# Git credential manager
debugprint "Installing Git Credential Manager"
source ~/.bashrc
sudo dotnet workload update
dotnet tool install -g git-credential-manager
echo 'export PATH="$PATH:~/.dotnet/tools"' >>~/.bashrc
source ~/.bashrc

debugprint "Configuring Git Credential Manager to use git credential cache and use oauth authentication"
git-credential-manager configure
git config --global credential.credentialStore cache --replace-all
git config --global credential.cacheOptions "--timeout 600000" --replace-all
git config --global credential.azreposCredentialType oauth --replace-all


# Clone the BuildXL repo
debugprint "Cloning BuildXL.Internal repository to ~/git/BuildXL.Internal"
git clone https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.Internal

# Optional quality of life tweaks
if [[ -n "$arg_InstallOptional" ]]; then
    installPackages "fish"
    
    # Set fish to the default shell
    chsh -s $(which fish)

    if [[ "$operatingSystem" == "ubuntu" ]]; then
        # no GUI on Mariner so we can skip this
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
fi