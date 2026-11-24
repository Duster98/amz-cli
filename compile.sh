#!/usr/bin/env bash
# Build amz-cli from source into self-contained executables.
#
#   ./compile.sh                 Linux and Windows, with ZIP archives
#   ./compile.sh --rid linux-x64 only one target
#   ./compile.sh --test          run the .NET test suite first
#   ./compile.sh --no-web        reuse the existing WebAssets/web.zip
#   ./compile.sh --no-zip        leave the dist/<rid> folders only
#
# Output: dist/linux-x64/amz-cli, dist/win-x64/amz-cli.exe and, by default,
# amz-cli-<version>-<rid>.zip in dist/.

set -euo pipefail
cd "$(dirname "$0")"

ROOT="$(pwd)"
DIST="${ROOT}/dist"
APP="amz-cli"
CLI_PROJ="src/AmazonScraper.Cli/AmazonScraper.Cli.csproj"
NOTIFY_PROJ="src/AmazonScraper.Notifications.Windows/AmazonScraper.Notifications.Windows.csproj"
RIDS=()
RUN_TESTS=0
NO_WEB=0
NO_ZIP=0

usage() {
    cat <<'EOF'
Build amz-cli from source.

Usage:
  ./compile.sh [options]

Options:
  --rid <linux-x64|win-x64>  Build only one target (repeatable).
  --test                     Run the .NET test suite before publishing.
  --no-web                   Do not rebuild the Web UI; use the current
                             src/AmazonScraper.Cli/WebAssets/web.zip.
  --no-zip                   Do not create ZIP archives.
  -h, --help                 Show this help.

Requirements:
  .NET SDK 10, Node.js 20.19+, npm and Python 3.
  On Linux, the generated executable also needs curl-impersonate in PATH at
  runtime. On Windows, the dependencies are included in the output folder.

Output:
  dist/linux-x64/amz-cli
  dist/win-x64/amz-cli.exe
  dist/amz-cli-<version>-linux-x64.zip
  dist/amz-cli-<version>-win-x64.zip
EOF
}

die() {
    printf 'Error: %s\n' "$*" >&2
    exit 1
}

need() {
    command -v "$1" > /dev/null 2>&1 \
        || die "'$1' is required but was not found in PATH."
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --rid)
            [[ $# -ge 2 ]] || die "--rid requires linux-x64 or win-x64."
            case "$2" in
                linux-x64|win-x64) ;;
                *) die "Unsupported RID '$2'. Use linux-x64 or win-x64." ;;
            esac
            RIDS+=("$2")
            shift
            ;;
        --test) RUN_TESTS=1 ;;
        --no-web) NO_WEB=1 ;;
        --no-zip) NO_ZIP=1 ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            usage >&2
            die "Unknown option: $1"
            ;;
    esac
    shift
done

# If no --rid was supplied, keep the default pair. The default array is
# replaced above as soon as the first --rid arrives.
if [[ ${#RIDS[@]} -eq 0 ]]; then
    RIDS=("linux-x64" "win-x64")
fi

need dotnet
need node
need npm
need python3

DOTNET_VERSION="$(dotnet --version 2>/dev/null || true)"
[[ "${DOTNET_VERSION}" == 10.* ]] \
    || die "The .NET 10 SDK is required (found '${DOTNET_VERSION:-none}')."

node -e '
const [major, minor] = process.versions.node.split(".").map(Number);
if (major < 20 || (major === 20 && minor < 19)) process.exit(1);
' || die "Node.js 20.19+ is required (found $(node --version))."

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "${CLI_PROJ}" | head -1)"
[[ -n "${VERSION}" ]] || die "Unable to read the project version from ${CLI_PROJ}."

echo "AMZ CLI ${VERSION}"
echo "Targets: ${RIDS[*]}"
echo

if [[ "${NO_WEB}" == "1" ]]; then
    if [[ ! -f "src/AmazonScraper.Cli/WebAssets/web.zip" ]]; then
        echo "Warning: --no-web was used but WebAssets/web.zip does not exist."
        echo "         The executable will not include the Web UI."
    else
        echo "Web UI: using existing src/AmazonScraper.Cli/WebAssets/web.zip"
    fi
else
    echo "Building Web UI..."
    bash ./web/build.sh
fi

if [[ "${RUN_TESTS}" == "1" ]]; then
    echo
    echo "Running tests..."
    dotnet test AmazonScraper.slnx -c Release --nologo
fi

copy_windows_native_libraries() {
    local out="$1"
    local package_version nuget_root native_dir

    package_version="$(
        sed -n 's/.*CurlImpersonate\.Http.*Version="\([^"]*\)".*/\1/p' \
            Directory.Packages.props | head -1
    )"
    [[ -n "${package_version}" ]] || {
        echo "Warning: CurlImpersonate.Http version not found; native libraries were not copied."
        return 0
    }

    nuget_root="$(
        dotnet nuget locals global-packages --list \
            | tr -d '\r' \
            | sed -n 's/^global-packages: //p' \
            | head -1
    )"
    if command -v cygpath > /dev/null 2>&1; then
        nuget_root="$(cygpath -u "${nuget_root}")"
    fi
    native_dir="${nuget_root}/curlimpersonate/${package_version}/runtimes/win-x64/native"

    if [[ -d "${native_dir}" ]]; then
        cp -f "${native_dir}"/* "${out}/"
        echo "Windows native libraries copied from NuGet."
    else
        echo "Warning: Windows native libraries were not found in the NuGet cache."
        echo "         The executable may rely on its extracted native assets."
    fi
}

cleanup_single_file_artifacts() {
    local out="$1"
    rm -f \
        "${out}/${APP}.dll" \
        "${out}/${APP}.pdb" \
        "${out}/${APP}.deps.json" \
        "${out}/${APP}.runtimeconfig.json" \
        "${out}/${APP}.exe.deps.json" \
        "${out}/${APP}.exe.pdb" \
        "${out}/${APP}.exe.runtimeconfig.json"
}

publish_cli() {
    local rid="$1"
    local out="$2"

    dotnet publish "${CLI_PROJ}" \
        -c Release \
        -r "${rid}" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -p:IncludeSourceRevisionInInformationalVersion=false \
        -o "${out}"
}

publish_windows_notifier() {
    local out="$1"

    [[ -f "${NOTIFY_PROJ}" ]] || {
        echo "Warning: Windows notifier project not found; desktop notifications"
        echo "         will not be available in this package."
        return 0
    }

    dotnet publish "${NOTIFY_PROJ}" \
        -c Release \
        -r win-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=None \
        -p:DebugSymbols=false \
        -o "${out}"

    rm -f "${out}/amz-notify.pdb" \
        "${out}/amz-notify.deps.json" \
        "${out}/amz-notify.runtimeconfig.json"
}

make_zip() {
    local rid="$1"
    local out="$2"
    local archive="${DIST}/amz-cli-${VERSION}-${rid}.zip"

    python3 - "${out}" "${archive}" <<'PY'
import pathlib
import sys
import zipfile

source = pathlib.Path(sys.argv[1])
destination = pathlib.Path(sys.argv[2])
with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(source.rglob("*")):
        if path.is_file():
            archive.write(path, path.relative_to(source).as_posix())
print(f"ZIP: {destination} ({destination.stat().st_size / 1024 / 1024:.1f} MB)")
PY
}

mkdir -p "${DIST}"

for rid in "${RIDS[@]}"; do
    out="${DIST}/${rid}"
    echo
    echo "Publishing ${rid}..."
    rm -rf "${out}"
    mkdir -p "${out}"

    publish_cli "${rid}" "${out}"

    if [[ "${rid}" == "win-x64" ]]; then
        publish_windows_notifier "${out}"
        copy_windows_native_libraries "${out}"
    fi

    cleanup_single_file_artifacts "${out}"
    cp -f LICENSE NOTICE THIRD_PARTY_NOTICES.md README.md "${out}/"

    if [[ "${rid}" == "linux-x64" ]]; then
        chmod +x "${out}/${APP}"
    fi

    if [[ "${NO_ZIP}" != "1" ]]; then
        make_zip "${rid}" "${out}"
    fi
done

echo
echo "Build complete: ${DIST}"
for rid in "${RIDS[@]}"; do
    if [[ "${rid}" == "win-x64" ]]; then
        executable="${DIST}/${rid}/${APP}.exe"
    else
        executable="${DIST}/${rid}/${APP}"
    fi
    echo "  ${rid}: ${executable}"
done

echo
for rid in "${RIDS[@]}"; do
    if [[ "${rid}" == "win-x64" ]]; then
        echo "Run Windows: ${DIST}\\win-x64\\${APP}.exe --version"
    else
        echo "Run Linux:   ${DIST}/linux-x64/${APP} --version"
    fi
done
echo
for rid in "${RIDS[@]}"; do
    if [[ "${rid}" == "linux-x64" ]]; then
        echo "Linux requires curl-impersonate in PATH."
    else
        echo "Windows includes its runtime dependencies in the output folder."
    fi
done
