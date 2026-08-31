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
    profile_comment="# Crystal Code CLI (Installer)"

    case "${SHELL:-sh}" in
        */zsh | zsh)
            profile_path="$HOME/.zshrc"
            ;;
        */bash | bash)
            profile_path="$HOME/.bashrc"
            ;;
    esac

    if [ -z "$profile_path" ]; then
        printf 'Installed %s. Start Crystal Code with: %s\n' "$binary_name" "${install_directory}/${binary_name}"
        return
    fi

    path_export="export PATH=\"${install_directory}:\$PATH\""
    command_alias="alias crystal=\"${install_directory}/${binary_name}\""
    comment_exists=false
    path_exists=false
    alias_exists=false

    if [ -f "$profile_path" ] && grep -F -x "$profile_comment" "$profile_path" >/dev/null 2>&1; then
        comment_exists=true
    fi

    if [ -f "$profile_path" ] && grep -F -x "$path_export" "$profile_path" >/dev/null 2>&1; then
        path_exists=true
    fi

    if [ -f "$profile_path" ] && grep -F -x "$command_alias" "$profile_path" >/dev/null 2>&1; then
        alias_exists=true
    fi

    if [ "$comment_exists" = true ] && [ "$path_exists" = true ] && [ "$alias_exists" = true ]; then
        printf 'Crystal Code is already configured in %s.\n' "$profile_path"
        printf 'Start Crystal Code with: crystal\n'
        return
    fi

    if [ "$(uname -s)" = "Linux" ]; then
        printf '\n\n\n' >> "$profile_path"
    fi

    if [ "$comment_exists" = false ]; then
        printf '%s\n' "$profile_comment" >> "$profile_path"
    fi

    if [ "$path_exists" = false ]; then
        printf '%s\n' "$path_export" >> "$profile_path"
    fi

    if [ "$alias_exists" = false ]; then
        printf '%s\n' "$command_alias" >> "$profile_path"
    fi

    printf '\n\n' >> "$profile_path"
    printf 'Configured Crystal Code in %s.\n' "$profile_path"
    printf 'Open a new terminal or run: . %s\n' "$profile_path"
    printf 'Start Crystal Code with: crystal\n'
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

printf 'Downloading %s...\n' "$archive_name"
if ! curl --fail --location --show-error --output "$archive_path" "$download_url"; then
    fail "Could not download ${archive_name} from the latest release."
fi

printf 'Extracting %s...\n' "$archive_name"
mkdir -p "$extraction_directory"
if ! unzip -q "$archive_path" -d "$extraction_directory"; then
    fail "Could not extract ${archive_name}."
fi

published_binary="$(find "$extraction_directory" -type f -name "$binary_name" -print | head -n 1)"
if [ -z "$published_binary" ]; then
    fail "The archive does not contain ${binary_name}."
fi

published_directory="$(dirname "$published_binary")"
printf 'Installing Crystal Code files...\n'
mkdir -p "$install_directory"
cp -R "$published_directory"/. "$install_directory"/
chmod 755 "${install_directory}/${binary_name}"

printf 'Installed %s to %s\n' "$archive_name" "$install_directory"
printf 'Configuring PATH...\n'
configure_path
