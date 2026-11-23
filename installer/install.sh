#!/usr/bin/env bash
# Bootstrap installer for Linux x64. Verifies the signed installer manifest
# and checksum with the embedded public key, then downloads and runs the
# installer. The public key is public material: never put the private key here.
set -euo pipefail

BASE="${AMZ_INSTALLER_BASE:-https://github.com/Duster98/amz-cli/releases/download/installer-stable}"
TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/amz-cli-installer.XXXXXX")"
trap 'rm -rf "${TMP_DIR}"' EXIT

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
    echo "Automatic installation is supported only on Linux x86_64." >&2
    echo "Download the installer manually for your platform." >&2
    exit 1
fi

if ! command -v openssl > /dev/null 2>&1; then
    echo "openssl is required to verify the installer signature." >&2
    exit 1
fi

cat > "${TMP_DIR}/installer-pub.pem" <<'PEM'
-----BEGIN PUBLIC KEY-----
MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEApkzs+7aP+j8pjFfbH6vP
4O2BIf6rS3yYVk3JxKdAkpDItK5q4/pnWhqd6Mfnfr6XTpdKQ1mORKaIF1+dYIah
lEah2MO3JZMbUWaZmesVpQymkVmm1UmuoMaPne7KTo0OynI87B90rgEYbA7OHab2
2fP54FIv9lLgw+ImUIlRN5TMj1InyUE5TG1ibYOtJgeEncVGATqzSSqL5/jJC6HD
y16XhhDHzs2g/X+qBrFuPz9YmWxihUjHdJ8MTpLjIjzWGGwP+2PbJxp0kyOj/Bmh
CRRQM3V77mSWgl4Vmwdvodhb4BCB07TKLuSI0X6qFq+oTO5CbgssfeyFVaKsGNGV
jRU1CKu4/gwf/7AP1LltYlQVLMbDPKdTQUm2razkJVGpQt8eT5yOXncqeDrGwjLI
RyvFoT5NcG9Nzibsl+n4lH4EWHPzl7Hf4bPiKca/dOYmFpBSj5M+FVLfv62sE1Iu
4bEAbeS7AVHRlB2VDQD8bedCLc/nJ+QcNaJfw+0Fuqi7rT25p/VeDgKVmqhqXo40
yoMI52V+me5I3aKPS/gIrA2aDAnbYShj/BKRw0FVOM23p5qCQqkFwpIExkcS5gPT
xLL1A7Lnc2VhoOdtMpd/KIupuFIbFRH5yUhdrIM1MDNbaEaamcIdQm1xAAdexZfL
f6U/Sxu3tIfusO1kb75Jkw0CAwEAAQ==
-----END PUBLIC KEY-----
PEM

download() {
    local url="$1"
    local destination="$2"
    if command -v curl > /dev/null 2>&1; then
        curl -fsSL "${url}" -o "${destination}"
    elif command -v wget > /dev/null 2>&1; then
        wget -q "${url}" -O "${destination}"
    else
        echo "Serve curl oppure wget per scaricare l'installer." >&2
        exit 1
    fi
}

verify_signature() {
    local file="$1"
    local signature="$2"
    if ! openssl dgst -sha256 \
        -verify "${TMP_DIR}/installer-pub.pem" \
        -signature "${signature}" "${file}" > /dev/null 2>&1; then
        echo "Invalid signature for $(basename "${file}")." >&2
        exit 1
    fi
}

download "${BASE}/installer.json" "${TMP_DIR}/installer.json"
download "${BASE}/installer.json.sig" "${TMP_DIR}/installer.json.sig"
download "${BASE}/installer.sha256" "${TMP_DIR}/installer.sha256"
download "${BASE}/installer.sha256.sig" "${TMP_DIR}/installer.sha256.sig"

verify_signature "${TMP_DIR}/installer.json" "${TMP_DIR}/installer.json.sig"
verify_signature "${TMP_DIR}/installer.sha256" "${TMP_DIR}/installer.sha256.sig"

EXPECTED="$(awk '/  (linux-x64\/(amz-cli-installer|amazon-installer)|(amz-cli|amazon)-installer-linux-x64)$/ { print $1; exit }' \
    "${TMP_DIR}/installer.sha256")"
if [[ -z "${EXPECTED}" ]]; then
    echo "Linux installer hash was not found in the signed manifest." >&2
    exit 1
fi

DOWNLOAD="${TMP_DIR}/amz-cli-installer"
for ASSET in amz-cli-installer-linux-x64 amazon-installer-linux-x64; do
    if download "${BASE}/${ASSET}" "${DOWNLOAD}"; then
        break
    fi
done
if [[ ! -s "${DOWNLOAD}" ]]; then
    echo "Installer binary not found in the GitHub release." >&2
    exit 1
fi
ACTUAL="$(openssl dgst -sha256 "${DOWNLOAD}" | awk '{ print $NF }')"
if [[ "${ACTUAL}" != "${EXPECTED}" ]]; then
    echo "Invalid installer hash: expected ${EXPECTED}, got ${ACTUAL}." >&2
    exit 1
fi

chmod +x "${DOWNLOAD}"
"${DOWNLOAD}" "$@"
