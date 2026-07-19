param(
    [string]$KspRoot = $env:KSP_ROOT,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($KspRoot)) {
    throw "Set KSP_ROOT or pass -KspRoot."
}

$required = @(
    "$KspRoot\KSP_x64_Data\Managed\Assembly-CSharp.dll",
    "$KspRoot\KSP_x64_Data\Managed\UnityEngine.dll",
    "$KspRoot\KSP_x64_Data\Managed\UnityEngine.CoreModule.dll",
    "$KspRoot\KSP_x64_Data\Managed\UnityEngine.UI.dll",
    "$KspRoot\GameData\ContractConfigurator\ContractConfigurator.dll",
    "$KspRoot\GameData\000_Harmony\0Harmony.dll"
)

foreach ($path in $required) {
    if (-not (Test-Path $path)) {
        throw "Required assembly not found: $path"
    }
}

dotnet build "$PSScriptRoot\src\KerbalismContractScienceBridge.csproj" `
    -c $Configuration `
    -p:KspRoot="$KspRoot" `
    -p:HarmonyAssembly="$KspRoot\GameData\000_Harmony\0Harmony.dll"
