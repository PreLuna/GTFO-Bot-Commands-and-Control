param([Parameter(Mandatory=$true)][string]$RuntimeRoot,[string]$BuildToolsRoot=(Join-Path (Split-Path $PSScriptRoot -Parent) 'GTFO-Mod-Build-and-Verification-Tools'))
$ErrorActionPreference='Stop'
$repo=$PSScriptRoot
$root=$repo
$microRoot=$repo
$tests=Join-Path $repo 'tests/MicroMaster'
$bin=Join-Path $repo 'out/tests'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$refs=@(Get-ChildItem $RuntimeRoot -Filter '*.dll' | Where-Object { $_.Name -match '^(System\.|mscorlib\.|netstandard\.)' -and $_.Name -notmatch '\.Native\.' } | ForEach-Object FullName)
function RunSuite([string]$name,[string[]]$sources) {
    if(@($sources | Where-Object {[IO.Path]::GetFileName($_) -eq 'BotControlApi.cs'}).Count){$sources+=Join-Path $root 'src\Captain\BotControlCommands.cs'}
    $output=Join-Path $bin ($name+'.dll')
    & (Join-Path $BuildToolsRoot 'tools/Compile.ps1') -Sources $sources -References $refs -Output $output -Executable
    ('{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"'+(Split-Path $RuntimeRoot -Leaf)+'"}}}') | Set-Content (Join-Path $bin ($name+'.runtimeconfig.json')) -Encoding utf8
    & dotnet $output | Tee-Object -FilePath (Join-Path $bin ($name+'.log'))
    if($LASTEXITCODE -ne 0){throw ('Test failed: '+$name)}
}
RunSuite 'ControlTests' @((Join-Path $root 'src\Captain\BotControlApi.cs'),(Join-Path $root 'src\HeadCamera\ViewPolicy.cs'),(Join-Path $root 'src\HeadCamera\ToggleGesture.cs'),(Join-Path $microRoot 'src\MicroMaster\ControlSafety.cs'),(Join-Path $tests 'ControlTests.cs'))
$runtimeSources=@('ControlRuntime.cs','ControlHooks.cs','ControlSafety.cs','BodyThreatWatch.cs','ManualMotionPolicy.cs') | ForEach-Object {Join-Path $microRoot ('src\MicroMaster\'+$_)}
RunSuite 'RuntimeSafetyTests' @($runtimeSources+(Join-Path $root 'src\Captain\BotControlApi.cs')+(Join-Path $tests 'RuntimeMocks.cs')+(Join-Path $tests 'RuntimeSafetyTests.cs'))
RunSuite 'MotionTests' @((Join-Path $microRoot 'src\MicroMaster\ManualMotionPolicy.cs'),(Join-Path $microRoot 'src\MicroMaster\NativeManualWalk.cs'),(Join-Path $root 'src\HeadCamera\ControlView.cs'),(Join-Path $root 'src\HeadCamera\ViewPolicy.cs'),(Join-Path $tests 'MotionMocks.cs'),(Join-Path $tests 'MotionTests.cs'))
RunSuite 'ServiceTests' @((Join-Path $root 'src\Captain\BotControlApi.cs'),(Join-Path $root 'src\Captain.Commands\BotControlService.cs'),(Join-Path $root 'src\Captain.Commands\ControlCommandPolicy.cs'),(Join-Path $root 'src\Captain.Commands\KillBorrowPolicy.cs'),(Join-Path $tests 'ServiceMocks.cs'),(Join-Path $tests 'ServiceTests.cs'))
$commandSources=@('BotControlService.cs','ControlCommandAdapter.cs','ControlCommandPolicy.cs','ControlCombatContract.cs','KillBorrowPolicy.cs','ItemTaskPolicy.cs','OpenTaskPolicy.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
$commandSources+=@((Join-Path $root 'src\Captain\BotControlApi.cs'),(Join-Path $microRoot 'src\MicroMaster\ManualMotionPolicy.cs'),(Join-Path $tests 'CommandMocks.cs'),(Join-Path $tests 'CommandTests.cs'))
RunSuite 'CommandTests' $commandSources
$combatSources=@('SingleKillAdapter.cs','SingleKillPolicy.cs','KillContinuationPolicy.cs','KillBorrowPolicy.cs','FocusOrders.cs','FocusControlTask.cs','ControlCombatTask.cs','ControlCombatContract.cs','ControlCommandPolicy.cs','ItemTaskPolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
RunSuite 'NativeCombatTests' @($combatSources+(Join-Path $tests 'NativeCombatMocks.cs')+(Join-Path $tests 'NativeCombatTests.cs'))
$itemSources=@('ItemTaskPolicy.cs','ControlCommandPolicy.cs','ItemRegion.cs','ItemTaskSession.cs','StackCompatibility.cs','ItemArrivalPolicy.cs','ItemInteractionGeometry.cs','BotTaskBridge.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
$itemMocks=@('ItemTaskMocks.cs','ControlCommandExternalMocks.cs') | ForEach-Object {Join-Path $root ('tests\Captain.Commands\'+$_)}
RunSuite 'ItemTaskTests' @($itemSources+$itemMocks+(Join-Path $root 'tests\Captain.Commands\ItemTaskTests.cs'))
RunSuite 'BorrowedItemTests' @($itemSources+$itemMocks+(Join-Path $tests 'BorrowedItemTests.cs'))
$openSources=@('OpenTaskAdapter.cs','ControlCommandPolicy.cs','OpenTaskPolicy.cs','OpenTargetLocks.cs','OpenTaskDiagnostics.cs','KillBorrowPolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
$openMocks=@('OpenAdapterMocks.cs','ControlCommandExternalMocks.cs') | ForEach-Object {Join-Path $root ('tests\Captain.Commands\'+$_)}
RunSuite 'OpenAdapterTests' @($openSources+$openMocks+(Join-Path $root 'tests\Captain.Commands\OpenAdapterTests.cs'))
RunSuite 'BorrowedOpenTests' @($openSources+$openMocks+(Join-Path $tests 'BorrowedOpenTests.cs'))
RunSuite 'OpenTaskTests' @((Join-Path $root 'src\Captain.Commands\OpenTaskPolicy.cs'),(Join-Path $root 'tests\Captain.Commands\OpenTaskTests.cs'))
$menuSources=@('CaptainMenuInput.cs','CaptainMenuTree.cs','CaptainCommandCatalog.cs','OpenTaskPolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
RunSuite 'MenuInputTests' @($menuSources+(Join-Path $root 'tests\Captain.Commands\MenuInputTests.cs'))
RunSuite 'HeadAimTests' @((Join-Path $root 'src\HeadCamera\HeadCameraApi.cs'),(Join-Path $root 'src\HeadCamera\HudViewIdentityApi.cs'),(Join-Path $root 'src\HeadCamera\HudCameraRegistry.cs'),(Join-Path $root 'src\HeadCamera\ControlView.cs'),(Join-Path $root 'src\HeadCamera\ViewPolicy.cs'),(Join-Path $tests 'HeadAimMocks.cs'),(Join-Path $tests 'HeadAimTests.cs'))
RunSuite 'CameraPolicyTests' @((Join-Path $root 'src\HeadCamera\ViewPolicy.cs'),(Join-Path $root 'tests\HeadCamera\PolicyTests.cs'))
RunSuite 'KillBorrowTests' @((Join-Path $root 'src\Captain.Commands\KillBorrowPolicy.cs'),(Join-Path $root 'src\Captain.Commands\ItemTaskPolicy.cs'),(Join-Path $root 'tests\Captain.Commands\KillBorrowTests.cs'))
$guardSources=@('ToolSupplyPolicy.cs','ToolSupplyAdapter.cs','FocusOrders.cs','SharedMeleeBridge.cs','TaskBoard.cs','GuardPolicy.cs','GuardTargets.cs','GuardCombat.cs','MovementOrders.cs','PositionTaskPolicy.cs','StationPolicy.cs','KillBorrowPolicy.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
$guardSources+=@('GuardMovementMocks.cs','GuardMovementTests.cs') | ForEach-Object {Join-Path $root ('tests\Captain.Commands\'+$_)}
$guardSources+=Join-Path $tests 'CaptainRegressionStubs.cs'
$guardSources+=Join-Path $root 'src\Captain\BotControlApi.cs'
RunSuite 'GuardMovementTests' $guardSources
$followSources=@($guardSources | Where-Object { [IO.Path]::GetFileName($_) -ne 'GuardMovementTests.cs' })
$followSources+=Join-Path $tests 'FollowControlTests.cs'
RunSuite 'FollowControlTests' $followSources
$stationSources=@($guardSources | Where-Object { [IO.Path]::GetFileName($_) -ne 'GuardMovementTests.cs' })
RunSuite 'StationHandoffTests' @($stationSources+(Join-Path $tests 'StationHandoffTests.cs'))
$continuationSources=@('KillContinuationPolicy.cs','KillCombatBridge.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
$continuationSources+=@('KillContinuationMocks.cs','KillContinuationTests.cs') | ForEach-Object {Join-Path $root ('tests\Captain.Commands\'+$_)}
$continuationSources+=Join-Path $BuildToolsRoot 'tests/Compatibility/CombatPolicy.cs'
RunSuite 'KillContinuationTests' $continuationSources
RunSuite 'RescueTests' @((Join-Path $root 'src\Captain.Commands\RescuePolicy.cs'),(Join-Path $root 'tests\Captain.Commands\RescueTests.cs'))
$rescueSources=@('RescueOrders.cs','RescuePolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('src\Captain.Commands\'+$_)}
RunSuite 'RescueAdapterTests' @($rescueSources+(Join-Path $root 'tests\Captain.Commands\RescueAdapterMocks.cs')+(Join-Path $root 'tests\Captain.Commands\RescueAdapterTests.cs'))
