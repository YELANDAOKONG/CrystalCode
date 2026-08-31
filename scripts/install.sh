#!/usr/bin/env sh

set -eu

repository="YELANDAOKONG/CrystalCode"
install_directory="${HOME:?HOME must be set}/.crystal/binaries/code"
binary_name="CrystalCode"

fail() {
    printf '%s\n' "$1" >&2
    exit 1
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        fail "Required command not found: $1"
    fi
}

configure_path() {
    profile_path=""

    case "${SHELL:-sh}" in
        */zsh | zsh)
            profile_path="$HOME/.zshrc"
            ;;
        */bash | bash)
            profile_path="$HOME/.bashrc"
            ;;
    esac

    if [ -z "$profile_path" ]; then
        printf 'Installed %s. Add %s to your shell PATH to run it from any directory.\n' "$binary_name" "$install_directory"
        return
    fi

    path_export="export PATH=\"${install_directory}:\$PATH\""
    if [ -f "$profile_path" ] && grep -F -x "$path_export" "$profile_path" >/dev/null 2>&1; then
        printf '%s is already configured in %s.\n' "$install_directory" "$profile_path"
        return
    fi

    if [ "$(uname -s)" = "Linux" ]; then
        printf '\n\n\n' >> "$profile_path"
    fi

    printf '%s\n\n' "$path_export" >> "$profile_path"
    printf 'Added %s to %s.\n' "$install_directory" "$profile_path"
    printf 'Open a new terminal or run: . %s\n' "$profile_path"
}

detect_asset() {
    operating_system="$(uname -s)"
    architecture="$(uname -m)"

    case "$operating_system" in
        Linux)
            case "$architecture" in
                x86_64 | amd64)
                    printf '%s\n' "linux-x64"
                    ;;
                aarch64 | arm64)
                    printf '%s\n' "linux-arm64"
                    ;;
                *)
                    fail "Unsupported Linux architecture: $architecture"
                    ;;
            esac
            ;;
        Darwin)
            case "$architecture" in
                arm64)
                    printf '%s\n' "macos-arm64"
                    ;;
                x86_64)
                    if [ "$(sysctl -in sysctl.proc_translated 2>/dev/null || true)" = "1" ]; then
                        printf '%s\n' "macos-arm64"
                    else
                        fail "macOS x64 is not supported."
                    fi
                    ;;
                *)
                    fail "Unsupported macOS architecture: $architecture"
                    ;;
            esac
            ;;
        *)
            fail "Unsupported operating system: $operating_system"
            ;;
    esac
}

require_command curl
require_command unzip
require_command mktemp
require_command grep

asset="$(detect_asset)"
archive_name="CrystalCode-${asset}.zip"
download_url="https://github.com/${repository}/releases/latest/download/${archive_name}"
working_directory="$(mktemp -d "${TMPDIR:-/tmp}/crystalcode.XXXXXX")"
archive_path="${working_directory}/${archive_name}"
extraction_directory="${working_directory}/extracted"

cleanup() {
    rm -rf -- "$working_directory"
}

trap cleanup EXIT HUP INT TERM

if ! curl --fail --location --show-error --output "$archive_path" "$download_url"; then
    fail "Could not download ${archive_name} from the latest release."
fi

mkdir -p "$extraction_directory"
if ! unzip -q "$archive_path" -d "$extraction_directory"; then
    fail "Could not extract ${archive_name}."
fi

published_binary="$(find "$extraction_directory" -type f -name "$binary_name" -print | head -n 1)"
if [ -z "$published_binary" ]; then
    fail "The archive does not contain ${binary_name}."
fi

mkdir -p "$install_directory"
staged_binary="$(mktemp "${install_directory}/.${binary_name}.XXXXXX")"
cp "$published_binary" "$staged_binary"
chmod 755 "$staged_binary"
mv -f "$staged_binary" "${install_directory}/${binary_name}"

printf 'Installed %s to %s\n' "$archive_name" "${install_directory}/${binary_name}"
configure_path
