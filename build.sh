#! /usr/bin/env bash
set -e

outputFolder='_output'
testPackageFolder='_tests'

#Artifact variables
artifactsFolder="_artifacts";

# Additional arguments passed through to the backend MSBuild invocation.
MSBUILD_ARGS=()

RunPython()
{
    local python_command
    for python_command in python3 python; do
        if command -v "$python_command" >/dev/null 2>&1 &&
            "$python_command" -c 'import sys; raise SystemExit(sys.version_info < (3, 9))' >/dev/null 2>&1; then
            "$python_command" "$@"
            return
        fi
    done

    echo "Python 3.9 or later is required to configure the .NET 10 platform runtime packs." >&2
    return 1
}

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

EnableExtraPlatformsInSDK()
{
    local dotnet_root
    local sdk_version
    local bundled_versions

    if ! command -v dotnet >/dev/null 2>&1; then
        echo "dotnet must be on PATH before enabling the extra runtime identifiers" >&2
        exit 1
    fi

    sdk_version="${DOTNETVERSION:-$(dotnet --version)}"
    dotnet_root="${DOTNET_ROOT:-}"
    if [ -z "$dotnet_root" ]; then
        local sdk_base_path
        sdk_base_path="$(dotnet --info | sed -n 's/^[[:space:]]*Base Path:[[:space:]]*//p' | head -1 | tr -d '\r')"
        if command -v cygpath >/dev/null 2>&1; then
            sdk_base_path="$(cygpath -u "$sdk_base_path")"
        fi
        if [[ "$sdk_base_path" != */sdk/* ]]; then
            echo "Could not determine the .NET SDK root from 'dotnet --info'" >&2
            exit 1
        fi
        dotnet_root="${sdk_base_path%%/sdk/*}"
    elif command -v cygpath >/dev/null 2>&1; then
        dotnet_root="$(cygpath -u "$dotnet_root")"
    fi
    bundled_versions="$dotnet_root/sdk/$sdk_version/Microsoft.NETCoreSdk.BundledVersions.props"

    if [ ! -f "$bundled_versions" ]; then
        echo "Could not find SDK bundled runtime metadata: $bundled_versions" >&2
        exit 1
    fi

    RunPython - "$bundled_versions" <<'PY'
from pathlib import Path
import re
import sys

path = Path(sys.argv[1])
content = path.read_text(encoding="utf-8-sig")
changed = False

def update_pack(match):
    global changed
    block = match.group(0)
    if 'TargetFramework="net10.0"' not in block:
        return block
    include = re.search(r'Include="([^"]+)"', block)
    if not include:
        return block
    package = include.group(1)
    attributes = []
    if package in ("Microsoft.NETCore.App", "Microsoft.AspNetCore.App"):
        attributes.append("RuntimePackRuntimeIdentifiers")
    if package == "Microsoft.NETCore.App":
        attributes.append("AppHostRuntimeIdentifiers")
    for attribute in attributes:
        attr = re.search(rf'{attribute}="([^"]*)"', block)
        if not attr:
            continue
        identifiers = attr.group(1).split(";")
        for rid in ("linux-x86", "freebsd-x64"):
            if rid not in identifiers:
                identifiers.append(rid)
                changed = True
        block = block[:attr.start(1)] + ";".join(identifiers) + block[attr.end(1):]
    return block

pattern = re.compile(r'<(?:KnownFrameworkReference|KnownAppHostPack)\s+Include="[^"]+"[\s\S]*?/>')
updated = pattern.sub(update_pack, content)
for framework in ("Microsoft.NETCore.App", "Microsoft.AspNetCore.App"):
    if not re.search(rf'<KnownFrameworkReference\s+Include="{re.escape(framework)}"[\s\S]*?TargetFramework="net10\.0"[\s\S]*?RuntimePackRuntimeIdentifiers="[^"]*(?:linux-x86|freebsd-x64)', updated):
        raise SystemExit(f"Could not enable .NET 10 runtime identifiers for {framework}")
if changed:
    path.write_text(updated, encoding="utf-8")
PY
}

EnableExtraPlatforms()
{
    local rid
    for rid in linux-x86 freebsd-x64; do
        if ! grep -q "<RuntimeIdentifiers>[^<]*$rid" src/Directory.Build.props; then
            sed -i'' -e "s#</RuntimeIdentifiers>#;$rid</RuntimeIdentifiers>#" src/Directory.Build.props
        fi
    done
}

EnableReleaseRuntimePlatforms()
{
    # The release workflow sets CUSTOM_RUNTIME_PACKS_DIR when it builds the
    # full standalone release matrix. Add the remaining official runtime IDs
    # to the shared projects so PublishAllRids creates each package input.
    # Keep the regular development build matrix unchanged.
    if [ -z "${CUSTOM_RUNTIME_PACKS_DIR:-}" ]; then
        return
    fi

    local rid
    for rid in linux-arm64 osx-x64 osx-arm64 win-x64 win-x86; do
        if ! grep -q "<RuntimeIdentifiers>[^<]*$rid" src/Directory.Build.props; then
            sed -i'' -e "s#</RuntimeIdentifiers>#;$rid</RuntimeIdentifiers>#" src/Directory.Build.props
        fi
    done
}

PrepareExtraRuntimePacks()
{
    local sourceFolder="${CUSTOM_RUNTIME_PACKS_DIR:-_temp/platform-runtime-packs}"
    local packageFolder="_temp/platform-runtime-nuget"
    local configFile="_temp/platform-runtime-nuget.config"
    local absolutePackageFolder
    local nugetPackageFolder
    local nugetConfigFile
    local packages=(
        "Microsoft.NETCore.App.Host.linux-x86.10.0.12.nupkg"
        "Microsoft.NETCore.App.Runtime.linux-x86.10.0.12.nupkg"
        "Microsoft.AspNetCore.App.Runtime.linux-x86.10.0.12.nupkg"
        "Microsoft.NETCore.App.Host.freebsd-x64.10.0.12.nupkg"
        "Microsoft.NETCore.App.Runtime.freebsd-x64.10.0.12.nupkg"
        "Microsoft.AspNetCore.App.Runtime.freebsd-x64.10.0.12.nupkg"
    )

    if command -v cygpath >/dev/null 2>&1; then
        sourceFolder="$(cygpath -u "$sourceFolder")"
    fi

    rm -rf "$packageFolder"
    mkdir -p "$packageFolder" "$(dirname "$configFile")"
    absolutePackageFolder="$(cd "$packageFolder" && pwd)"

    local package
    for package in "${packages[@]}"; do
        local sourcePackage
        sourcePackage="$(find "$sourceFolder" -type f -name "$package" -print -quit 2>/dev/null || true)"
        if [ -z "$sourcePackage" ]; then
            echo "Missing .NET 10 platform package $package in $sourceFolder." >&2
            echo "Build or download the pinned platform packs first and set CUSTOM_RUNTIME_PACKS_DIR." >&2
            exit 1
        fi
        cp "$sourcePackage" "$packageFolder/$package"
    done

    cp src/NuGet.config "$configFile"
    nugetPackageFolder="$absolutePackageFolder"
    nugetConfigFile="$configFile"
    if command -v cygpath >/dev/null 2>&1; then
        nugetPackageFolder="$(cygpath -m "$nugetPackageFolder")"
        nugetConfigFile="$(cygpath -m "$nugetConfigFile")"
    fi
    dotnet nuget add source "$nugetPackageFolder" --name bookshelf-platform-runtime-packs --configfile "$nugetConfigFile"
    RunPython - "$configFile" "${packages[@]}" <<'PY'
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
packages = sys.argv[2:]
tree = ET.parse(path)
root = tree.getroot()
mapping = root.find("packageSourceMapping")
if mapping is None:
    mapping = ET.SubElement(root, "packageSourceMapping")
source = ET.SubElement(mapping, "packageSource", {"key": "bookshelf-platform-runtime-packs"})
for package in packages:
    package_id = package.removesuffix(".10.0.12.nupkg")
    ET.SubElement(source, "package", {"pattern": package_id})
ET.indent(tree, space="  ")
tree.write(path, encoding="utf-8", xml_declaration=True)
PY

    PLATFORM_RUNTIME_NUGET_CONFIG="$(cd "$(dirname "$configFile")" && pwd)/$(basename "$configFile")"
    if command -v cygpath >/dev/null 2>&1; then
        PLATFORM_RUNTIME_NUGET_CONFIG="$(cygpath -m "$PLATFORM_RUNTIME_NUGET_CONFIG")"
    fi
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

    local msbuild_args=(-restore "$slnFile" -m:1 "-p:Configuration=Release" "-p:Platform=$platform")
    if [[ -n "$RID" && -n "$FRAMEWORK" ]];
    then
        msbuild_args+=("-p:RuntimeIdentifiers=$RID")
    fi
    if [ -n "$PLATFORM_RUNTIME_NUGET_CONFIG" ]; then
        msbuild_args+=("-p:RestoreConfigFile=$PLATFORM_RUNTIME_NUGET_CONFIG")
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

    local windowsFrameworkFolder="$outputFolder/$framework-windows/$runtime/publish"
    if [ -d "$windowsFrameworkFolder" ]; then
        cp -r "$windowsFrameworkFolder"/* "$folder"
    fi

    # Keep the app launcher name used by the standalone docs and installer.
    # Windows builds name the service-capable apphost Readarr.Console.exe.
    if [[ ! -f "$folder/Readarr.exe" && -f "$folder/Readarr.Console.exe" ]]; then
        cp "$folder/Readarr.Console.exe" "$folder/Readarr.exe"
    fi
    if [[ ! -f "$folder/Readarr.exe" ]]; then
        echo "Windows package is missing Readarr.exe and Readarr.Console.exe." >&2
        exit 1
    fi

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

BuildInstallerNet()
{
    local framework="$1"
    local runtime="$2"

    ProgressStart "Creating $runtime .NET installer"

    local installerProj="distribution/windows/installer-net"
    local payloadDir="$installerProj/Payload"
    local payloadZip="$payloadDir/Readarr.payload.zip"
    local buildFolder="$artifactsFolder/$runtime/$framework/Readarr"
    local publishDir="$installerProj/publish"
    local destDir="distribution/windows/setup/output"
    local version="${READARRVERSION:-dev}"
    local destExe="$destDir/BookshelfNG.Installer.$version.$runtime.exe"

    if [ ! -d "$buildFolder" ]; then
        echo "[ERROR] Build folder not found: $buildFolder" >&2
        echo "[ERROR] Build and package the win-x64 runtime first." >&2
        exit 1
    fi

    mkdir -p "$payloadDir"
    rm -f "$payloadZip"
    RunPython - "$buildFolder" "$payloadZip" <<'PAYLOAD_ZIP_PY'
from pathlib import Path
import sys
from zipfile import ZIP_DEFLATED, ZipFile

source = Path(sys.argv[1])
destination = Path(sys.argv[2])
with ZipFile(destination, "w", ZIP_DEFLATED, compresslevel=9) as archive:
    for item in source.rglob("*"):
        relative = item.relative_to(source)
        if relative.parts and relative.parts[0].casefold() == "readarr.update":
            continue
        if item.is_file() and not item.is_symlink():
            archive.write(item, relative.as_posix())
PAYLOAD_ZIP_PY

    rm -rf "$publishDir"
    dotnet publish "$installerProj/Readarr.Installer.csproj" -c Release -r "$runtime" --self-contained \
        -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$publishDir"

    mkdir -p "$destDir"
    mv "$publishDir/BookshelfNG.Installer.exe" "$destExe"
    rm -f "$payloadZip"
    rm -rf "$publishDir"

    ProgressEnd "Created .NET installer: $destExe"
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
        EnableReleaseRuntimePlatforms
        EnableExtraPlatformsInSDK
        PrepareExtraRuntimePacks
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
    BuildInstallerNet "net10.0" "win-x64"
fi
