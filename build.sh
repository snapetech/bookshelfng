#! /usr/bin/env bash
set -e

outputFolder='_output'
testPackageFolder='_tests'

#Artifact variables
artifactsFolder="_artifacts";

# Additional arguments passed through to the backend MSBuild invocation.
MSBUILD_ARGS=()

ProgressStart()
{
    echo "Start '$1'"
}

ProgressEnd()
{
    echo "Finish '$1'"
}

UpdateVersionNumber()
{
    if [ "$READARRVERSION" != "" ]; then
        echo "Updating Version Info"
        ESCAPED_BRANCH_NAME=${BUILD_SOURCEBRANCHNAME//\//\\/}
        sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$READARRVERSION<\/AssemblyVersion>/g" src/Directory.Build.props
        sed -i'' -e "s/<AssemblyConfiguration>[\$()A-Za-z-]\+<\/AssemblyConfiguration>/<AssemblyConfiguration>${ESCAPED_BRANCH_NAME}<\/AssemblyConfiguration>/g" src/Directory.Build.props
        sed -i'' -e "s/<string>10.0.0.0<\/string>/<string>$READARRVERSION<\/string>/g" distribution/osx/Readarr.app/Contents/Info.plist
    fi
}

PrepareFreeBSDRuntimePacks()
{
    local packageFolder="_temp/freebsd-nuget"
    local archive="_temp/freebsd-dotnet-10.0.12-source-built.tar.gz"
    local url="https://github.com/Thefrank/dotnet-freebsd-crossbuild/releases/download/v10.0.112-amd64-freebsd-14/Private.SourceBuilt.Artifacts.10.0.112-servicing.26422.108.freebsd-x64.tar.gz"
    local sha256="7e032c2024cfb17ac3bb27ab8d35234a6e74f5ede16e66ca8420bcf66028d978"
    local packages=(
        "Microsoft.NETCore.App.Host.freebsd-x64.10.0.12.nupkg"
        "Microsoft.NETCore.App.Runtime.freebsd-x64.10.0.12.nupkg"
        "Microsoft.AspNetCore.App.Runtime.freebsd-x64.10.0.12.nupkg"
    )

    mkdir -p "$packageFolder"

    local package
    local packagesReady=YES
    for package in "${packages[@]}"; do
        if [ ! -s "$packageFolder/$package" ]; then
            packagesReady=NO
            break
        fi
    done

    if [ "$packagesReady" = "NO" ]; then
        mkdir -p "$(dirname "$archive")"
        echo "Downloading the pinned community-built .NET 10.0.12 FreeBSD runtime packs"
        curl --fail --location --retry 3 --silent --show-error --output "$archive" "$url"

        if command -v sha256sum >/dev/null 2>&1; then
            if ! printf '%s  %s\n' "$sha256" "$archive" | sha256sum --check --status; then
                echo "FreeBSD runtime pack archive SHA-256 verification failed" >&2
                exit 1
            fi
        elif command -v shasum >/dev/null 2>&1; then
            if ! printf '%s  %s\n' "$sha256" "$archive" | shasum -a 256 --check --status; then
                echo "FreeBSD runtime pack archive SHA-256 verification failed" >&2
                exit 1
            fi
        else
            echo "No SHA-256 checksum utility is installed" >&2
            exit 1
        fi

        tar -xzf "$archive" -C "$packageFolder" "${packages[@]}"
        rm -f "$archive"
    fi

    for package in "${packages[@]}"; do
        if [ ! -s "$packageFolder/$package" ]; then
            echo "Missing FreeBSD runtime package: $package" >&2
            exit 1
        fi
    done
}

EnableFreeBSD()
{
    if ! grep -q 'freebsd-x64</RuntimeIdentifiers>' src/Directory.Build.props; then
        sed -i'' -e "s^<RuntimeIdentifiers>\(.*\)</RuntimeIdentifiers>^<RuntimeIdentifiers>\1;freebsd-x64</RuntimeIdentifiers>^" src/Directory.Build.props
    fi

    PrepareFreeBSDRuntimePacks
}

LintUI()
{
    ProgressStart 'ESLint'
    yarn lint
    ProgressEnd 'ESLint'

    ProgressStart 'Stylelint'
    if [ "$os" = "windows" ]; then
        yarn stylelint-windows
    else
        yarn stylelint-linux
    fi
    ProgressEnd 'Stylelint'
}

Build()
{
    ProgressStart 'Build'

    rm -rf $outputFolder
    rm -rf $testPackageFolder

    slnFile=src/Readarr.sln

    if [ $os = "windows" ]; then
        platform=Windows
    else
        platform=Posix
    fi

    local msbuild_args=(-restore "$slnFile" "-p:Configuration=Release" "-p:Platform=$platform")
    if [[ -n "$RID" && -n "$FRAMEWORK" ]];
    then
        msbuild_args+=("-p:RuntimeIdentifiers=$RID")
    fi

    dotnet msbuild "${msbuild_args[@]}" "${MSBUILD_ARGS[@]}" -t:PublishAllRids

    ProgressEnd 'Build'
}

YarnInstall()
{
    ProgressStart 'yarn install'
    yarn install --frozen-lockfile --network-timeout 120000
    ProgressEnd 'yarn install'
}

RunWebpack()
{
    ProgressStart 'Running webpack'
    yarn run build --env production
    ProgressEnd 'Running webpack'
}

PackageFiles()
{
    local folder="$1"
    local framework="$2"
    local runtime="$3"

    rm -rf $folder
    mkdir -p $folder
    cp -r $outputFolder/$framework/$runtime/publish/* $folder
    cp -r $outputFolder/Readarr.Update/$framework/$runtime/publish $folder/Readarr.Update
    cp -r $outputFolder/UI $folder

    echo "Adding LICENSE"
    cp LICENSE.md $folder
}

PackageLinux()
{
    local framework="$1"
    local runtime="$2"

    ProgressStart "Creating $runtime Package for $framework"

    local folder=$artifactsFolder/$runtime/$framework/Readarr

    PackageFiles "$folder" "$framework" "$runtime"

    echo "Removing Service helpers"
    rm -f $folder/ServiceUninstall.*
    rm -f $folder/ServiceInstall.*

    echo "Removing Readarr.Windows"
    rm $folder/Readarr.Windows.*

    echo "Adding Readarr.Mono to UpdatePackage"
    cp $folder/Readarr.Mono.* $folder/Readarr.Update
    if [ "$framework" = "net10.0" ]; then
        cp $folder/Mono.Posix.NETStandard.* $folder/Readarr.Update
        cp $folder/libMonoPosixHelper.* $folder/Readarr.Update
    fi

    ProgressEnd "Creating $runtime Package for $framework"
}

PackageMacOS()
{
    local framework="$1"
    local runtime="$2"

    ProgressStart "Creating MacOS Package for $framework $runtime"

    local folder=$artifactsFolder/$runtime/$framework/Readarr

    PackageFiles "$folder" "$framework" "$runtime"

    echo "Removing Service helpers"
    rm -f $folder/ServiceUninstall.*
    rm -f $folder/ServiceInstall.*

    echo "Removing Readarr.Windows"
    rm $folder/Readarr.Windows.*

    echo "Adding Readarr.Mono to UpdatePackage"
    cp $folder/Readarr.Mono.* $folder/Readarr.Update
    if [ "$framework" = "net10.0" ]; then
        cp $folder/Mono.Posix.NETStandard.* $folder/Readarr.Update
        cp $folder/libMonoPosixHelper.* $folder/Readarr.Update
    fi

    ProgressEnd 'Creating MacOS Package'
}

PackageMacOSApp()
{
    local framework="$1"
    local runtime="$2"

    ProgressStart "Creating macOS App Package for $framework $runtime"

    local folder="$artifactsFolder/$runtime-app/$framework"

    rm -rf $folder
    mkdir -p $folder
    cp -r distribution/osx/Readarr.app $folder
    mkdir -p $folder/Readarr.app/Contents/MacOS

    echo "Copying Binaries"
    cp -r $artifactsFolder/$runtime/$framework/Readarr/* $folder/Readarr.app/Contents/MacOS

    echo "Removing Update Folder"
    rm -r $folder/Readarr.app/Contents/MacOS/Readarr.Update

    ProgressEnd 'Creating macOS App Package'
}

PackageWindows()
{
    local framework="$1"
    local runtime="$2"

    ProgressStart "Creating $runtime Package for $framework"

    local folder=$artifactsFolder/$runtime/$framework/Readarr

    PackageFiles "$folder" "$framework" "$runtime"
    cp -r $outputFolder/$framework-windows/$runtime/publish/* $folder

    echo "Removing Readarr.Mono"
    rm -f $folder/Readarr.Mono.*
    rm -f $folder/Mono.Posix.NETStandard.*
    rm -f $folder/libMonoPosixHelper.*

    echo "Adding Readarr.Windows to UpdatePackage"
    cp $folder/Readarr.Windows.* $folder/Readarr.Update

    ProgressEnd "Creating $runtime Package for $framework"
}

Package()
{
    local framework="$1"
    local runtime="$2"
    local SPLIT

    IFS='-' read -ra SPLIT <<< "$runtime"

    case "${SPLIT[0]}" in
        linux|freebsd*)
            PackageLinux "$framework" "$runtime"
            ;;
        win)
            PackageWindows "$framework" "$runtime"
            ;;
        osx)
            PackageMacOS "$framework" "$runtime"
            PackageMacOSApp "$framework" "$runtime"
            ;;
    esac
}

BuildInstaller()
{
    local framework="$1"
    local runtime="$2"

    ./_inno/ISCC.exe distribution/windows/setup/readarr.iss "//DFramework=$framework" "//DRuntime=$runtime"
}

InstallInno()
{
    ProgressStart "Installing portable Inno Setup"

    rm -rf _inno
    curl -s --output innosetup.exe "https://files.jrsoftware.org/is/6/innosetup-${INNOVERSION:-6.2.0}.exe"
    mkdir _inno
    ./innosetup.exe //portable=1 //silent //currentuser //dir=.\\_inno
    rm innosetup.exe

    ProgressEnd "Installed portable Inno Setup"
}

RemoveInno()
{
    rm -rf _inno
}

PackageTests()
{
    local framework="$1"
    local runtime="$2"

    cp test.sh "$testPackageFolder/$framework/$runtime/publish"

    rm -f $testPackageFolder/$framework/$runtime/*.log.config

    ProgressEnd 'Creating Test Package'
}

# Use mono or .net depending on OS
case "$(uname -s)" in
    CYGWIN*|MINGW32*|MINGW64*|MSYS*)
        # on windows, use dotnet
        os="windows"
        ;;
    *)
        # otherwise use mono
        os="posix"
        ;;
esac

POSITIONAL=()
MSBUILD_ARGS=()

if [ $# -eq 0 ]; then
    echo "No arguments provided, building everything"
    BACKEND=YES
    FRONTEND=YES
    PACKAGES=YES
    INSTALLER=NO
    LINT=YES
    ENABLE_EXTRA_PLATFORMS=NO
    ENABLE_EXTRA_PLATFORMS_IN_SDK=NO
fi

while [[ $# -gt 0 ]]
do
key="$1"

case $key in
    --backend)
        BACKEND=YES
        shift # past argument
        ;;
    --enable-bsd|--enable-extra-platforms)
        ENABLE_EXTRA_PLATFORMS=YES
        shift # past argument
        ;;
    --enable-extra-platforms-in-sdk)
        ENABLE_EXTRA_PLATFORMS_IN_SDK=YES
        shift # past argument
        ;;
    -r|--runtime)
        RID="$2"
        shift # past argument
        shift # past value
        ;;
    -f|--framework)
        FRAMEWORK="$2"
        shift # past argument
        shift # past value
        ;;
    --frontend)
        FRONTEND=YES
        shift # past argument
        ;;
    --packages)
        PACKAGES=YES
        shift # past argument
        ;;
    --installer)
        INSTALLER=YES
        shift # past argument
        ;;
    --lint)
        LINT=YES
        shift # past argument
        ;;
    --msbuild-arg)
        if [[ $# -lt 2 ]]; then
            echo "Missing value for --msbuild-arg" >&2
            exit 2
        fi
        MSBUILD_ARGS+=("$2")
        shift 2
        ;;
    --msbuild-arg=*)
        MSBUILD_ARGS+=("${key#*=}")
        shift
        ;;
    --all)
        BACKEND=YES
        FRONTEND=YES
        PACKAGES=YES
        LINT=YES
        shift # past argument
        ;;
    *)
        echo "Unknown option: $1" >&2
        exit 2
        ;;
esac
done

if [ "$ENABLE_EXTRA_PLATFORMS_IN_SDK" = "YES" ];
then
    EnableExtraPlatformsInSDK
fi

if [ "$BACKEND" = "YES" ];
then
    UpdateVersionNumber
    if [ "$ENABLE_EXTRA_PLATFORMS" = "YES" ];
    then
        EnableExtraPlatforms
    fi
    Build
    if [[ -z "$RID" || -z "$FRAMEWORK" ]];
    then
        PackageTests "net10.0" "linux-musl-x64"
        if [ "$ENABLE_EXTRA_PLATFORMS" = "YES" ];
        then
            PackageTests "net10.0" "freebsd-x64"
            PackageTests "net10.0" "linux-x86"
        fi
    else
        PackageTests "$FRAMEWORK" "$RID"
    fi
fi

if [[ "$LINT" = "YES" || "$FRONTEND" = "YES" ]];
then
    YarnInstall
fi

if [ "$LINT" = "YES" ];
then
    LintUI
fi

if [ "$FRONTEND" = "YES" ];
then
    RunWebpack
fi

if [ "$PACKAGES" = "YES" ];
then
    UpdateVersionNumber

    if [[ -z "$RID" || -z "$FRAMEWORK" ]];
    then
        Package "net10.0" "linux-musl-x64"
        if [ "$ENABLE_EXTRA_PLATFORMS" = "YES" ];
        then
            Package "net10.0" "freebsd-x64"
            Package "net10.0" "linux-x86"
        fi
    else
        Package "$FRAMEWORK" "$RID"
    fi
fi

if [ "$INSTALLER" = "YES" ];
then
    InstallInno
    BuildInstaller "net10.0" "win-x64"
    BuildInstaller "net10.0" "win-x86"
    RemoveInno
fi
