#!/usr/bin/env sh

set -eu

repository="YELANDAOKONG/CrystalCode"
install_directory="${HOME:?HOME must be set}/.crystal/binaries/code"
binary_name="CrystalCode"

# Mineral palette from CrystalCode.Display Theme: grey42, lightsteelblue, indianred.
# A non-terminal, NO_COLOR, or TERM=dumb keeps the plain status lines.
styled=false
MUTED=
ACCENT=
FAIL=
NC=
cursor_hidden=false
download_pid=
card_kind=
docs_url="https://github.com/${repository}/blob/master/docs/user-guide.md"

if [ -t 1 ] && [ -t 2 ] && [ -z "${NO_COLOR:-}" ] && [ "${TERM:-}" != "dumb" ]; then
    styled=true
    MUTED=$(printf '\033[38;5;242m')
    NC=$(printf '\033[0m')
    if [ "${COLORTERM:-}" = "truecolor" ] || [ "${COLORTERM:-}" = "24bit" ]; then
        ACCENT=$(printf '\033[38;2;176;196;222m')
        FAIL=$(printf '\033[38;2;205;92;92m')
    else
        ACCENT=$(printf '\033[38;5;152m')
        FAIL=$(printf '\033[38;5;167m')
    fi
fi

fail() {
    restore_cursor
    if [ "$styled" = true ] && [ -t 2 ]; then
        printf '%s%s%s\n' "$FAIL" "$1" "$NC" >&2
    else
        printf '%s\n' "$1" >&2
    fi
    exit 1
}

# Dash has no RETURN trap. The EXIT cleanup also restores the cursor when a
# download is interrupted while it is hidden.
restore_cursor() {
    if [ "${cursor_hidden:-false}" = true ]; then
        printf '\033[?25h\n' >&2
        cursor_hidden=false
    fi
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        fail "Required command not found: $1"
    fi
}

# Command substitution drops a trailing newline, so a non-empty result
# means the file does not end with one. Without this, the installer
# comment is appended onto the last profile line.
ensure_trailing_newline() {
    if [ -s "$1" ] && [ -n "$(tail -c 1 "$1")" ]; then
        printf '\n' >> "$1"
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
        if [ "$styled" = true ]; then
            card_kind="direct"
            return
        fi

        printf 'Installed %s. Start Crystal Code with: %s\n' "$binary_name" "${install_directory}/${binary_name}"
        return
    fi

    path_export="export PATH=\"${install_directory}:\$PATH\""
    command_alias="alias crystal=${binary_name}"
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
        if [ "$styled" = true ]; then
            card_kind="ready"
            return
        fi

        printf 'Crystal Code is already configured in %s.\n' "$profile_path"
        printf 'Start Crystal Code with: crystal\n'
        return
    fi

    ensure_trailing_newline "$profile_path"
    if [ "$(uname -s)" = "Linux" ]; then
        printf '\n\n\n' >> "$profile_path"
    fi

    # Write a complete block when updating a partial or legacy configuration.
    # A later alias definition intentionally supersedes the legacy absolute-path alias.
    printf '%s\n' "$profile_comment" >> "$profile_path"
    printf '%s\n' "$path_export" >> "$profile_path"
    printf '%s\n' "$command_alias" >> "$profile_path"
    printf '\n\n' >> "$profile_path"

    if [ "$styled" = true ]; then
        card_kind="configured"
        return
    fi

    printf 'Configured Crystal Code in %s.\n' "$profile_path"
    printf 'Open a new terminal or run: . %s\n' "$profile_path"
    printf 'Start Crystal Code with: crystal\n'
}

print_card() {
    printf '\n'
    printf '%s┌─────────┐%s\n' "$MUTED" "$NC"
    printf '%s│ %scrystal%s │%s\n' "$MUTED" "$ACCENT" "$MUTED" "$NC"
    printf '%s└─────────┘%s\n' "$MUTED" "$NC"
    printf '\n'
    printf 'Installed %s to %s%s%s\n' "$archive_name" "$ACCENT" "$install_directory" "$NC"

    case "$card_kind" in
        ready)
            printf 'Crystal Code is already configured in %s%s%s.\n' "$ACCENT" "$profile_path" "$NC"
            printf 'Start Crystal Code with: %scrystal%s\n' "$ACCENT" "$NC"
            start_command="crystal"
            ;;
        configured)
            printf 'Configured Crystal Code in %s%s%s.\n' "$ACCENT" "$profile_path" "$NC"
            printf 'Open a new terminal or run: %s. %s%s\n' "$ACCENT" "$profile_path" "$NC"
            printf 'Start Crystal Code with: %scrystal%s\n' "$ACCENT" "$NC"
            start_command="crystal"
            ;;
        *)
            printf 'Installed %s. Start Crystal Code with: %s%s%s\n' \
                "$binary_name" "$ACCENT" "${install_directory}/${binary_name}" "$NC"
            start_command="${install_directory}/${binary_name}"
            ;;
    esac

    printf '\n'
    printf '%scd <project>%s  %s# Open a repository%s\n' "$ACCENT" "$NC" "$MUTED" "$NC"
    if [ "$start_command" = "crystal" ]; then
        # Pad to the same column as "cd <project>".
        printf '%scrystal%s       %s# Start Crystal Code%s\n' "$ACCENT" "$NC" "$MUTED" "$NC"
    else
        printf '%s%s%s  %s# Start Crystal Code%s\n' "$ACCENT" "$start_command" "$NC" "$MUTED" "$NC"
    fi
    printf '\n'
    printf '%sFor more information visit %s%s\n' "$MUTED" "$NC" "$docs_url"
    printf '\n'
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

# GNU sed flushes with -u. BSD sed flushes with -l. The padded fallback
# forces a flush where neither flag exists.
unbuffered_sed() {
    if echo | sed -u -e '' >/dev/null 2>&1; then
        sed -nu "$@"
    elif echo | sed -l -e '' >/dev/null 2>&1; then
        sed -nl "$@"
    else
        pad=$(printf '\n%512s' '')
        sed -ne "s/\$/${pad}/" "$@"
    fi
}

is_uint() {
    case "$1" in
        '' | *[!0-9]*)
            return 1
            ;;
    esac
    return 0
}

render_bar() {
    received=$1
    total=$2
    if ! is_uint "$received" || ! is_uint "$total" || [ "$total" -le 0 ]; then
        return 0
    fi

    width=50
    percent=$((received * 100 / total))
    if [ "$percent" -gt 100 ]; then
        percent=100
    fi
    if [ "$percent" -eq "${last_percent:--1}" ]; then
        return 0
    fi
    last_percent=$percent

    on=$((percent * width / 100))
    off=$((width - on))
    filled=
    empty=
    i=0
    while [ "$i" -lt "$on" ]; do
        filled="${filled}━"
        i=$((i + 1))
    done
    i=0
    while [ "$i" -lt "$off" ]; do
        empty="${empty}─"
        i=$((i + 1))
    done

    # stderr is unbuffered, so a carriage return redraws the same line.
    printf '\r%s%s%s %3d%%%s' "$ACCENT" "$filled" "$empty" "$percent" "$NC" >&2
}

# curl's ascii trace reports Content-Length and each received block.
# sed keeps only those lines, and the shell redraws one bar from them.
download_with_progress() {
    if [ "$styled" != true ] || ! command -v mkfifo >/dev/null 2>&1 || ! command -v sed >/dev/null 2>&1; then
        return 1
    fi

    trace_path="${working_directory}/download.trace"
    rm -f "$trace_path"
    if ! mkfifo "$trace_path"; then
        return 1
    fi

    printf '\033[?25l' >&2
    cursor_hidden=true

    # stderr stays quiet here. A failed attempt falls back to curl, and an
    # interrupt must not print a write error after the temp directory is gone.
    curl --fail --location --silent --trace-ascii "$trace_path" --output "$archive_path" "$download_url" 2>/dev/null &
    download_pid=$!

    length=0
    bytes=0
    last_percent=-1
    unbuffered_sed \
        -e 'y/ACDEGHLNORTV/acdeghlnortv/' \
        -e '/^0000: content-length:/p' \
        -e '/^<= recv data/p' \
        "$trace_path" | while IFS= read -r line; do
        case "$line" in
            "0000: content-length:"*)
                length=${line#0000: content-length:}
                length=${length# }
                length=${length%%[!0-9]*}
                bytes=0
                ;;
            "<= recv data,"*)
                size=${line#<= recv data,}
                size=${size# }
                size=${size%%[!0-9]*}
                if is_uint "$size" && is_uint "$length" && [ "$length" -gt 0 ]; then
                    bytes=$((bytes + size))
                    render_bar "$bytes" "$length"
                fi
                ;;
        esac
    done || true

    download_status=0
    if [ -n "${download_pid:-}" ]; then
        wait "$download_pid" || download_status=$?
        download_pid=
    else
        # The interrupt trap already reaped the downloader.
        download_status=130
    fi

    if [ "$download_status" -ne 0 ]; then
        restore_cursor
        # 128+signal means the download was interrupted. Do not start another curl.
        if [ "$download_status" -gt 128 ]; then
            exit "$download_status"
        fi
        return 1
    fi

    final_size=$(wc -c < "$archive_path" | tr -d '[:space:]') || final_size=0
    if is_uint "$final_size" && [ "$final_size" -gt 0 ]; then
        last_percent=-1
        render_bar "$final_size" "$final_size"
    fi
    printf '\n' >&2
    printf '\033[?25h' >&2
    cursor_hidden=false
    return 0
}

require_command curl
require_command unzip
require_command mktemp
require_command grep
require_command tail

asset="$(detect_asset)"
archive_name="CrystalCode-${asset}.zip"
download_url="https://github.com/${repository}/releases/latest/download/${archive_name}"
working_directory="$(mktemp -d "${TMPDIR:-/tmp}/crystalcode.XXXXXX")"
archive_path="${working_directory}/${archive_name}"
extraction_directory="${working_directory}/extracted"

cleanup() {
    restore_cursor
    if [ -n "${download_pid:-}" ]; then
        kill "$download_pid" 2>/dev/null || true
        wait "$download_pid" 2>/dev/null || true
        download_pid=
    fi
    if [ -n "${working_directory:-}" ]; then
        rm -rf -- "$working_directory"
        working_directory=
    fi
}

# A signal trap that returns lets the script continue into wait and a second
# curl against the directory cleanup just removed.
trap cleanup EXIT
trap 'cleanup; exit 130' INT
trap 'cleanup; exit 143' TERM
trap 'cleanup; exit 129' HUP

if [ "$styled" = true ]; then
    printf '%sInstalling Crystal Code...%s\n\n' "$MUTED" "$NC"
    if ! download_with_progress; then
        if ! curl -# --fail --location --output "$archive_path" "$download_url"; then
            fail "Could not download ${archive_name} from the latest release."
        fi
        printf '\n'
    fi
else
    printf 'Downloading %s...\n' "$archive_name"
    if ! curl --fail --location --show-error --output "$archive_path" "$download_url"; then
        fail "Could not download ${archive_name} from the latest release."
    fi
fi

if [ "$styled" != true ]; then
    printf 'Extracting %s...\n' "$archive_name"
fi
mkdir -p "$extraction_directory"
if ! unzip -q "$archive_path" -d "$extraction_directory"; then
    fail "Could not extract ${archive_name}."
fi

published_binary="$(find "$extraction_directory" -type f -name "$binary_name" -print | head -n 1)"
if [ -z "$published_binary" ]; then
    fail "The archive does not contain ${binary_name}."
fi

published_directory="$(dirname "$published_binary")"
if [ "$styled" != true ]; then
    printf 'Installing Crystal Code files...\n'
fi
mkdir -p "$install_directory"
cp -R "$published_directory"/. "$install_directory"/
chmod 755 "${install_directory}/${binary_name}"

if [ "$styled" = true ]; then
    configure_path
    print_card
else
    printf 'Installed %s to %s\n' "$archive_name" "$install_directory"
    printf 'Configuring PATH...\n'
    configure_path
fi
