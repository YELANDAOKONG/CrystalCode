#!/usr/bin/env sh
# Publish this checkout as Release into build/ and install it under
# ~/.crystal/binaries/code/. The publish arguments match the release
# workflow. That workflow writes ./publish for the GitHub asset; this
# script writes the ignored build/ directory.

set -eu

install_directory="${HOME:?HOME must be set}/.crystal/binaries/code"
binary_name="CrystalCode"
# A self-contained single-file publish includes the runtime and is far
# larger than a framework-dependent apphost.
minimum_binary_bytes=10000000

profile_path=""
profile_changed=false
runtime_id=""
binary_bytes=""
previous_directory=""

fail() {
    printf 'Check failed: %s\n' "$1" >&2
    exit 1
}

step() {
    printf '\n[%s] %s\n' "$1" "$2"
}

detail() {
    printf '  %s\n' "$1"
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        fail "Required command not found: $1"
    fi
}

clear_directory() {
    target=$1
    label=$2

    if [ -L "$target" ]; then
        fail "Refusing to remove ${target} because it is a symbolic link."
    fi

    if [ -e "$target" ]; then
        detail "Removing previous ${label}."
        rm -rf -- "$target"
        return
    fi

    detail "No previous ${label}."
}

# Command substitution drops a trailing newline, so a non-empty result
# means the file does not end with one. Without this, the installer
# comment is appended onto the last profile line.
ensure_trailing_newline() {
    if [ -s "$1" ] && [ -n "$(tail -c 1 "$1")" ]; then
        printf '\n' >> "$1"
    fi
}

# The live directory is renamed aside before the staged directory takes
# its place. Both names are on one filesystem. Until the staged directory
# lands, a failure or interrupt moves the previous install back.
restore_install() {
    if [ -z "$previous_directory" ]; then
        return
    fi

    if [ ! -e "$install_directory" ] && [ -d "$previous_directory" ] && [ -d "$staging_directory" ]; then
        if mv -- "$previous_directory" "$install_directory"; then
            printf 'Restored the previous install at %s.\n' "$install_directory" >&2
        else
            printf 'Could not restore %s from %s.\n' "$install_directory" "$previous_directory" >&2
        fi
    fi
}

replace_install_directory() {
    previous_directory="${install_directory}.previous"

    case "$previous_directory" in
        */.crystal/binaries/code.previous) ;;
        *)
            fail "Refusing to replace unexpected backup directory: ${previous_directory}"
            ;;
    esac

    if [ -L "$previous_directory" ] || [ -L "$install_directory" ] || [ -L "$staging_directory" ]; then
        fail "Refusing to replace a symbolic link."
    fi

    if [ -e "$previous_directory" ] && [ ! -d "$previous_directory" ]; then
        fail "${previous_directory} exists and is not a directory."
    fi

    if [ -d "$previous_directory" ] && [ ! -e "$install_directory" ]; then
        detail "Restoring the install directory after an interrupted replace."
        if ! mv -- "$previous_directory" "$install_directory"; then
            fail "Could not restore ${install_directory} from ${previous_directory}."
        fi
    fi

    if [ -e "$previous_directory" ]; then
        clear_directory "$previous_directory" "install backup"
    fi

    trap restore_install HUP INT TERM

    if [ -d "$install_directory" ]; then
        detail "Moving the current install aside."
        if ! mv -- "$install_directory" "$previous_directory"; then
            trap - HUP INT TERM
            fail "Could not move ${install_directory} aside."
        fi
    elif [ -e "$install_directory" ]; then
        trap - HUP INT TERM
        fail "${install_directory} exists and is not a directory."
    fi

    detail "Moving the staged install into place."
    if ! mv -- "$staging_directory" "$install_directory"; then
        restore_install
        trap - HUP INT TERM
        if [ -e "$install_directory" ] && [ ! -e "$previous_directory" ]; then
            fail "Could not move ${staging_directory} to ${install_directory}. Restored the previous install."
        fi
        if [ -d "$previous_directory" ]; then
            fail "Could not move ${staging_directory} to ${install_directory}. The previous install is at ${previous_directory}."
        fi
        fail "Could not move ${staging_directory} to ${install_directory}."
    fi

    trap - HUP INT TERM

    if [ -e "$previous_directory" ]; then
        detail "Removing the previous install."
        if ! rm -rf -- "$previous_directory"; then
            detail "Could not remove ${previous_directory}. The new install is in place."
        fi
    fi
}

check_not_running() {
    step "check" "Crystal Code process"

    if ! command -v pgrep >/dev/null 2>&1; then
        detail "pgrep is not available. Skipping the running-process check."
        return
    fi

    if pgrep -x "$binary_name" >/dev/null 2>&1; then
        fail "Crystal Code is running. Exit it, then run this script again."
    fi

    detail "Check passed: ${binary_name} is not running."
}

file_bytes() {
    wc -c < "$1" | tr -d '[:space:]'
}

configure_path() {
    step "check" "Shell profile"
    profile_path=""
    profile_changed=false
    profile_comment="# Crystal Code CLI (Installer)"

    detail "Shell: ${SHELL:-sh}"

    case "${SHELL:-sh}" in
        */zsh | zsh)
            profile_path="$HOME/.zshrc"
            ;;
        */bash | bash)
            profile_path="$HOME/.bashrc"
            ;;
    esac

    if [ -z "$profile_path" ]; then
        detail "No zsh or bash profile selected."
        detail "Check passed: PATH was left unchanged."
        return
    fi

    detail "Profile: ${profile_path}"

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

    if [ "$comment_exists" = true ]; then
        detail "Installer comment: present"
    else
        detail "Installer comment: missing"
    fi

    if [ "$path_exists" = true ]; then
        detail "PATH entry: present"
    else
        detail "PATH entry: missing"
    fi

    if [ "$alias_exists" = true ]; then
        detail "Alias: present"
    else
        detail "Alias: missing"
    fi

    if [ "$comment_exists" = true ] && [ "$path_exists" = true ] && [ "$alias_exists" = true ]; then
        detail "Check passed: profile already contains the installer block."
        return
    fi

    # Write a complete block when updating a partial or legacy configuration.
    # A later alias definition intentionally supersedes the legacy absolute-path alias.
    # The block matches scripts/install.sh.
    ensure_trailing_newline "$profile_path"
    if [ "$(uname -s)" = "Linux" ]; then
        printf '\n\n\n' >> "$profile_path"
    fi

    printf '%s\n' "$profile_comment" >> "$profile_path"
    printf '%s\n' "$path_export" >> "$profile_path"
    printf '%s\n' "$command_alias" >> "$profile_path"
    printf '\n\n' >> "$profile_path"
    profile_changed=true
    detail "Check passed: wrote PATH and alias to ${profile_path}."
}

script_directory=$(CDPATH= cd -- "$(dirname "$0")" && pwd) || fail "Could not resolve the script directory."
repository_root=$(CDPATH= cd -- "${script_directory}/.." && pwd) || fail "Could not resolve the repository root."
publish_directory="${repository_root}/build"
crystal_root="${repository_root}/../Crystal"
staging_directory="${install_directory}.new"

printf 'Crystal Code local install\n'
printf 'Repository: %s\n' "$repository_root"

step "check" "Repository"
if ! CDPATH= cd -- "$repository_root"; then
    fail "Could not enter the repository root: ${repository_root}"
fi
detail "Working directory: ${repository_root}"

if [ ! -f "CrystalCode.sln" ]; then
    fail "Missing solution: ${repository_root}/CrystalCode.sln"
fi
detail "Solution: ${repository_root}/CrystalCode.sln"

if [ ! -f "CrystalCode/CrystalCode.csproj" ]; then
    fail "Missing project: ${repository_root}/CrystalCode/CrystalCode.csproj"
fi
detail "Project: ${repository_root}/CrystalCode/CrystalCode.csproj"
detail "Check passed: repository files exist."

step "check" "Commands"
for command_name in dotnet grep cp mv find chmod mkdir rm touch uname tail
do
    require_command "$command_name"
    detail "Found ${command_name}: $(command -v "$command_name")"
done
detail "Check passed: required commands are present."

step "check" ".NET SDK"
detail "dotnet: $(command -v dotnet)"

if ! sdk_version=$(dotnet --version); then
    fail "Could not read the .NET SDK version."
fi
sdk_version=$(printf '%s' "$sdk_version" | tr -d '[:space:]')
detail "Version: ${sdk_version}"

case "$sdk_version" in
    10.*)
        detail "Check passed: SDK major version is 10."
        ;;
    *)
        fail "The release workflow uses the .NET 10 SDK. This machine selected ${sdk_version}."
        ;;
esac

step "check" "Crystal sibling"
if [ ! -d "$crystal_root" ]; then
    fail "Missing sibling Crystal checkout: ${crystal_root}"
fi
crystal_root=$(CDPATH= cd -- "$crystal_root" && pwd) || fail "Could not resolve the Crystal checkout."
detail "Root: ${crystal_root}"

for relative_path in \
    Crystal/Crystal.csproj \
    Crystal.Tools/Crystal.Tools.csproj \
    Crystal.Agents/Crystal.Agents.csproj \
    Crystal.Harness/Crystal.Harness.csproj
do
    project_path="${crystal_root}/${relative_path}"
    if [ ! -f "$project_path" ]; then
        fail "Missing Crystal project: ${project_path}"
    fi
    detail "Found ${project_path}"
done
detail "Check passed: sibling Crystal projects exist."

step "check" "Runtime"
operating_system=$(uname -s)
architecture=$(uname -m)
detail "Operating system: ${operating_system}"
detail "Architecture: ${architecture}"

case "$operating_system" in
    Linux)
        case "$architecture" in
            x86_64 | amd64)
                runtime_id="linux-x64"
                ;;
            aarch64 | arm64)
                runtime_id="linux-arm64"
                ;;
            *)
                fail "Unsupported Linux architecture: ${architecture}"
                ;;
        esac
        ;;
    Darwin)
        case "$architecture" in
            arm64)
                runtime_id="osx-arm64"
                ;;
            x86_64)
                if [ "$(sysctl -in sysctl.proc_translated 2>/dev/null || true)" = "1" ]; then
                    detail "Rosetta translation: yes"
                    runtime_id="osx-arm64"
                else
                    fail "macOS x64 is not supported."
                fi
                ;;
            *)
                fail "Unsupported macOS architecture: ${architecture}"
                ;;
        esac
        ;;
    *)
        fail "Unsupported operating system: ${operating_system}"
        ;;
esac

detail "Runtime identifier: ${runtime_id}"
detail "Check passed: platform matches a release workflow runtime."

step "check" "Install destination"
detail "Directory: ${install_directory}"

case "$install_directory" in
    */.crystal/binaries/code) ;;
    *)
        fail "Refusing to replace unexpected install directory: ${install_directory}"
        ;;
esac

if [ -L "$install_directory" ]; then
    fail "Refusing to replace ${install_directory} because it is a symbolic link."
fi

if [ -e "$install_directory" ] && [ ! -d "$install_directory" ]; then
    fail "${install_directory} exists and is not a directory."
fi

install_parent=$(dirname "$install_directory")
if ! mkdir -p "$install_parent"; then
    fail "Could not create ${install_parent}."
fi

probe="${install_parent}/.install-local-probe"
if ! touch "$probe"; then
    fail "Cannot write to ${install_parent}."
fi
rm -f -- "$probe"
detail "Check passed: ${install_parent} is writable."

if [ -d "$install_directory" ]; then
    detail "Check passed: existing install directory can be replaced."
else
    detail "Check passed: no previous install directory."
fi

check_not_running

step "publish" "Release"
detail "Output: build (${publish_directory})"
detail "Configuration: Release"
detail "Runtime: ${runtime_id}"
detail "Self-contained: true"
detail "PublishSingleFile: true"

if [ -L "$publish_directory" ]; then
    fail "Refusing to remove ${publish_directory} because it is a symbolic link."
fi

clear_directory "$publish_directory" "build output"

printf '  dotnet publish CrystalCode/CrystalCode.csproj --configuration Release --runtime %s --self-contained true -p:PublishSingleFile=true --output build\n' "$runtime_id"
if ! dotnet publish CrystalCode/CrystalCode.csproj \
    --configuration Release \
    --runtime "$runtime_id" \
    --self-contained true \
    -p:PublishSingleFile=true \
    --output build
then
    fail "dotnet publish exited with an error."
fi
detail "Check passed: publish finished."

step "check" "Publish output"
published_binary="${publish_directory}/${binary_name}"
detail "Binary: ${published_binary}"

if [ ! -f "$published_binary" ]; then
    fail "Publish output is missing ${binary_name}."
fi

if [ ! -x "$published_binary" ]; then
    fail "Published binary is not executable: ${published_binary}"
fi
detail "Check passed: binary exists and is executable."

if [ -e "${publish_directory}/CrystalCode.dll" ]; then
    fail "Publish output contains CrystalCode.dll. The release workflow publishes a single file."
fi
detail "Check passed: CrystalCode.dll is absent."

binary_bytes=$(file_bytes "$published_binary")
detail "Size: ${binary_bytes} bytes"

if [ "$binary_bytes" -lt "$minimum_binary_bytes" ]; then
    fail "Published binary is ${binary_bytes} bytes. A self-contained single-file build is larger than ${minimum_binary_bytes} bytes."
fi
detail "Check passed: size is large enough for a self-contained single-file build."

check_not_running

step "install" "Replace ${install_directory}"
clear_directory "$staging_directory" "staging directory"
if ! mkdir -p "$staging_directory"; then
    fail "Could not create ${staging_directory}."
fi

detail "Copying build output."
if ! cp -R "${publish_directory}/." "$staging_directory/"; then
    fail "Could not copy the build output."
fi

chmod 755 "${staging_directory}/${binary_name}"

staged_binary="${staging_directory}/${binary_name}"
if [ ! -x "$staged_binary" ]; then
    fail "Staged binary is not executable: ${staged_binary}"
fi

staged_bytes=$(file_bytes "$staged_binary")
if [ "$staged_bytes" != "$binary_bytes" ]; then
    fail "Staged binary is ${staged_bytes} bytes. Build binary is ${binary_bytes} bytes."
fi
detail "Check passed: staged binary matches the build (${staged_bytes} bytes)."

replace_install_directory

installed_binary="${install_directory}/${binary_name}"
if [ ! -x "$installed_binary" ]; then
    fail "Installed binary is not executable: ${installed_binary}"
fi

installed_bytes=$(file_bytes "$installed_binary")
if [ "$installed_bytes" != "$binary_bytes" ]; then
    fail "Installed binary is ${installed_bytes} bytes. Build binary is ${binary_bytes} bytes."
fi

file_count=$(find "$install_directory" -type f | wc -l | tr -d '[:space:]')
detail "Copied ${file_count} files."
detail "Check passed: installed binary matches the build (${installed_bytes} bytes)."

configure_path

step "done" "Local install complete"
detail "Build: ${published_binary}"
detail "Installed: ${installed_binary}"
detail "Runtime: ${runtime_id}"
detail "Configuration: Release"
detail "Self-contained: true"
detail "Single file: true"
detail "Configuration, credentials, and prompts were left in place."

if [ "$profile_changed" = true ]; then
    detail "Open a new terminal or run: . ${profile_path}"
fi

if [ -n "$profile_path" ]; then
    detail "Start Crystal Code with: crystal"
else
    detail "Start Crystal Code with: ${installed_binary}"
fi
