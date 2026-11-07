#!/usr/bin/env bash
# Compile-checks the Unity-side assemblies (Handover.Runtime, Handover.Platform.Meta,
# Handover.Editor, Handover.Editor.Meta) WITHOUT running Unity, by building them with the
# .NET SDK against a Unity 6000.3 editor's managed assemblies:
#   - UnityEngine/UnityEditor module DLLs from the editor install,
#   - UnityEngine.UI built from the editor's bundled com.unity.ugui source,
#   - signature stubs (stubs/*.cs) for the Input System and the Meta SDK members we call,
#     copied from com.unity.inputsystem 1.20.1 and Meta XR SDK 207.0.1 declarations.
# It proves names and signatures resolve; it does not replace opening the project in Unity
# (serialization, asmdef resolution, Play mode) or a device test.
#
# Usage: tools/unity-compile-check/run.sh /path/to/Unity-6000.3.28f1/Editor/Data
set -euo pipefail

DATA="${1:?usage: run.sh path/to/Unity/Editor/Data}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
work="${HANDOVER_CHECK_DIR:-$(mktemp -d)}"
ugui="$DATA/Resources/PackageManager/BuiltInPackages/com.unity.ugui"
defines="UNITY_2021_3_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_2023_2_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_6000_1_OR_NEWER;UNITY_6000_2_OR_NEWER;UNITY_6000_3_OR_NEWER;UNITY_6000_3;UNITY_6000;NET_STANDARD_2_1;NET_STANDARD"
mkdir -p "$work"

{
  echo '<Project>'
  echo '  <PropertyGroup>'
  echo '    <TargetFramework>netstandard2.1</TargetFramework>'
  echo '    <LangVersion>9.0</LangVersion>'
  echo '    <Nullable>disable</Nullable>'
  echo '    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
  echo '    <NoWarn>CS0067;CS0414;CS0649;CS0169;CS0618</NoWarn>'
  echo '  </PropertyGroup>'
  echo '  <ItemGroup>'
  echo "    <Reference Include=\"$DATA/Managed/UnityEngine/UnityEngine.*Module.dll\" Private=\"false\" />"
  echo '  </ItemGroup>'
  echo "  <ItemGroup Condition=\"'\$(NeedsUnityEditor)' == 'true'\">"
  echo "    <Reference Include=\"$DATA/Managed/UnityEngine/UnityEditor.*Module.dll\" Private=\"false\" />"
  echo '  </ItemGroup>'
  echo '</Project>'
} > "$work/Directory.Build.props"

# project <dir> <assembly> <defines> <needsEditor> <warningsAsErrors> <generateAssemblyInfo> <items...>
project() {
  local dir="$1" assembly="$2" extra="$3" editor="$4" strict="$5" info="$6"
  shift 6
  mkdir -p "$work/$dir"
  {
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <PropertyGroup>'
    echo "    <AssemblyName>$assembly</AssemblyName>"
    echo "    <DefineConstants>$defines;$extra</DefineConstants>"
    echo "    <NeedsUnityEditor>$editor</NeedsUnityEditor>"
    echo "    <TreatWarningsAsErrors>$strict</TreatWarningsAsErrors>"
    echo "    <GenerateAssemblyInfo>$info</GenerateAssemblyInfo>"
    echo '    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>'
    echo '  </PropertyGroup>'
    echo '  <ItemGroup>'
    for item in "$@"; do
      echo "    $item"
    done
    echo '  </ItemGroup>'
    echo '</Project>'
  } > "$work/$dir/$dir.csproj"
}

compile() { echo "<Compile Include=\"$1\" />"; }
ref() { echo "<ProjectReference Include=\"$1\" />"; }

core="$(ref "$repo/tools/CoreTests/Core/Handover.Core.csproj")"
uguiRef="$(ref ../ugui/ugui.csproj)"
inputRef="$(ref ../input/input.csproj)"
metaRef="$(ref ../meta/meta.csproj)"
runtimeSources="$(compile "$repo/Assets/Handover/Runtime/Unity/**/*.cs")"
metaSources="$(compile "$repo/Assets/Handover/Runtime/Meta/**/*.cs")"

project ugui UnityEngine.UI "PACKAGE_PHYSICS;PACKAGE_ANIMATION;PACKAGE_UITOOLKIT" false false false "$(compile "$ugui/Runtime/UGUI/**/*.cs")"
project input Unity.InputSystem "" false false true "$(compile "$here/stubs/InputSystemStub.cs")"
project meta MetaStubs "" false false true "$(compile "$here/stubs/MetaStubs.cs")"
project metaeditor MetaEditorStubs "" false false true "$(compile "$here/stubs/MetaEditorStubs.cs")"

project rt_new Handover.Runtime "UNITY_ANDROID;ENABLE_INPUT_SYSTEM" false true true "$runtimeSources" "$core" "$uguiRef" "$inputRef"
project rt_legacy Handover.Runtime "UNITY_ANDROID;ENABLE_LEGACY_INPUT_MANAGER" false true true "$runtimeSources" "$core" "$uguiRef" "$inputRef"
project rt_editor Handover.Runtime "UNITY_EDITOR;ENABLE_INPUT_SYSTEM;ENABLE_LEGACY_INPUT_MANAGER;DEVELOPMENT_BUILD" true true true "$runtimeSources" "$core" "$uguiRef" "$inputRef"
project meta_rt Handover.Platform.Meta "UNITY_ANDROID;ENABLE_INPUT_SYSTEM;HANDOVER_META_SDK" false true true "$metaSources" "$core" "$uguiRef" "$inputRef" "$metaRef" "$(ref ../rt_new/rt_new.csproj)"
project meta_ed Handover.Platform.Meta "UNITY_EDITOR;ENABLE_INPUT_SYSTEM;HANDOVER_META_SDK" true true true "$metaSources" "$core" "$uguiRef" "$inputRef" "$metaRef" "$(ref ../rt_editor/rt_editor.csproj)"
project editor Handover.Editor "UNITY_EDITOR" true true true "$(compile "$repo/Assets/Handover/Editor/*.cs")" "$core" "$uguiRef" "$inputRef" "$(ref ../rt_editor/rt_editor.csproj)"
project editor_meta Handover.Editor.Meta "UNITY_EDITOR;HANDOVER_META_SDK" true true true "$(compile "$repo/Assets/Handover/Editor/Meta/**/*.cs")" "$core" "$uguiRef" "$inputRef" "$metaRef" \
  "$(ref ../metaeditor/metaeditor.csproj)" "$(ref ../rt_editor/rt_editor.csproj)" "$(ref ../meta_ed/meta_ed.csproj)" "$(ref ../editor/editor.csproj)"

status=0
for p in rt_new rt_legacy rt_editor meta_rt meta_ed editor editor_meta; do
  if dotnet build "$work/$p/$p.csproj" -c Release -v quiet --nologo > "$work/$p.log" 2>&1; then
    echo "PASS $p"
  else
    echo "FAIL $p (log: $work/$p.log)"
    grep -E " error " "$work/$p.log" | sort -u | head -20 || true
    status=1
  fi
done
exit $status
