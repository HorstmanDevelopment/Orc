#!/usr/bin/env bash
# Runs the Orc CLI with info-level host logging suppressed so the TUI splash
# page is not polluted by "Application started" / DI startup messages.
#
# Usage:
#   ./run.sh                       # Debug build
#   ./run.sh -c Release            # Release build
#   ./run.sh -- --some-cli-arg     # forward args to the CLI

set -euo pipefail

configuration="Debug"
cli_args=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration)
            configuration="$2"
            shift 2
            ;;
        --)
            shift
            cli_args=("$@")
            break
            ;;
        *)
            cli_args+=("$1")
            shift
            ;;
    esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli_project="$script_dir/Orc.Cli/Orc.Cli.csproj"

# Configuration uses the ORC_ env prefix (see Orc.Cli/Program.cs).
# Double-underscore maps to the ':' config separator, so these override appsettings.json.
export ORC_Logging__LogLevel__Default="Warning"
export ORC_Logging__LogLevel__Microsoft="Warning"
export ORC_Logging__LogLevel__Microsoft__Hosting__Lifetime="Warning"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

exec dotnet run --project "$cli_project" --configuration "$configuration" --no-launch-profile -- "${cli_args[@]}"
