param(
 [Parameter(Mandatory)][string]$GameRoot,
 [Parameter(Mandatory)][string]$RuntimeRoot,
 [string]$BuildToolsRoot=(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) '../GTFO-Mod-Build-and-Verification-Tools')
)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $repo 'out/tests'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$refs=@(Get-ChildItem -LiteralPath $RuntimeRoot -Filter '*.dll' | Where-Object {$_.Name -match '^(System\.|mscorlib\.|netstandard\.)' -and $_.Name -notmatch '\.Native\.'} | ForEach-Object FullName)
$refs+=Join-Path $GameRoot 'BepInEx/core/0Harmony.dll'
$sources=@('Captain.Boosters/src/BoosterConditionCompat.cs','Captain.Boosters/src/PerfectBoosterPolicy.cs','Captain.Boosters/src/BoosterSelection.cs','Captain.Boosters/tests/OrdinaryPerfectFixture.cs','Captain.Boosters/tests/OrdinaryPerfectHarmonyTests.cs')|ForEach-Object {Join-Path $repo $_}
$target=Join-Path $bin 'OrdinaryPerfectHarmonyTests.dll'
& (Join-Path $BuildToolsRoot 'tools/Compile.ps1') -Sources $sources -References $refs -Output $target -Executable
@{runtimeOptions=@{tfm='net6.0';framework=@{name='Microsoft.NETCore.App';version=(Split-Path $RuntimeRoot -Leaf)}}}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $bin 'OrdinaryPerfectHarmonyTests.runtimeconfig.json') -Encoding utf8
foreach($mode in 'early','late','off','absent'){
 & dotnet $target (Join-Path $GameRoot 'BepInEx/core') $mode | Tee-Object -FilePath (Join-Path $bin ('OrdinaryPerfectHarmony-'+$mode+'.log'))
 if($LASTEXITCODE -ne 0){throw ('Ordinary perfect Harmony failed: '+$mode)}
}
