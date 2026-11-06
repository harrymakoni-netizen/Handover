#!/usr/bin/env bash
# Builds Handover.Core as netstandard2.1 / C# 9 (Unity's API level) and runs the
# deterministic NUnit suite shared with Unity's EditMode tests.
# Usage: tools/run-core-tests.sh [extra dotnet test args]
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet build "$here/CoreTests/Core/Handover.Core.csproj" -c Release --nologo -v quiet
dotnet test "$here/CoreTests/Tests/Handover.Core.Tests.csproj" -c Release --nologo "$@"
