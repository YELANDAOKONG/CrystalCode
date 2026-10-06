#!/usr/bin/env sh
# Publish this checkout and install it by running scripts/install-local.sh.
# That script resolves the repository from its own path, so this wrapper
# executes the file directly.

set -eu

script_directory=$(CDPATH= cd -- "$(dirname "$0")" && pwd) || {
    printf 'Could not resolve the script directory.\n' >&2
    exit 1
}

exec "${script_directory}/scripts/install-local.sh" "$@"
