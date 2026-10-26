param([Parameter(Mandatory=$true)][string]$RuntimeRoot,[string]$BuildToolsRoot=(Join-Path (Split-Path $PSScriptRoot -Parent) 'GTFO-Mod-Build-and-Verification-Tools'))
$ErrorActionPreference='Stop'
$repo=$PSScriptRoot
$root=$repo
$microRoot=$repo
$bin=Join-Path $repo 'out/tests'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$refs=@(Get-ChildItem $RuntimeRoot -Filter '*.dll' | Where-Object { $_.Name -match '^(System\.|mscorlib\.|netstandard\.)' -and $_.Name -notmatch '\.Native\.' } | ForEach-Object FullName)
function RunSuite([string]$name,[string[]]$sources) {
    if(@($sources | Where-Object {[IO.Path]::GetFileName($_) -eq 'BotControlApi.cs'}).Count){$sources+=Join-Path $root 'Captain\src\BotControlCommands.cs'}
    $output=Join-Path $bin ($name+'.dll')
    & (Join-Path $BuildToolsRoot 'tools/Compile.ps1') -Sources $sources -References $refs -Output $output -Executable
    ('{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"'+(Split-Path $RuntimeRoot -Leaf)+'"}}}') | Set-Content (Join-Path $bin ($name+'.runtimeconfig.json')) -Encoding utf8
    & dotnet $output | Tee-Object -FilePath (Join-Path $bin ($name+'.log'))
    if($LASTEXITCODE -ne 0){throw ('Test failed: '+$name)}
}
RunSuite 'ControlTests' @((Join-Path $root 'Captain\src\BotControlApi.cs'),(Join-Path $root 'HeadCamera\src\ViewPolicy.cs'),(Join-Path $root 'HeadCamera\src\ToggleGesture.cs'),(Join-Path $microRoot 'MicroMaster\src\ControlSafety.cs'),(Join-Path $root 'IntegrationTests/Control/ControlTests.cs'))
RunSuite 'ConfigUpgradeTests' @((Join-Path $root 'HeadCamera/src/Plugin.cs'),(Join-Path $root 'HeadCamera/src/ViewPolicy.cs'),(Join-Path $root 'HeadCamera/src/CaptureAnalysis.cs'),(Join-Path $root 'HeadCamera/tests/PluginMocks.cs'),(Join-Path $root 'HeadCamera/tests/ConfigUpgradeTests.cs'))
RunSuite 'ControlFeedbackTests' @((Join-Path $root 'HeadCamera/src/ViewPolicy.cs'),(Join-Path $root 'HeadCamera/src/ControlFeedback.cs'),(Join-Path $root 'HeadCamera/tests/ControlFeedbackTests.cs'))
$runtimeSources=@('ControlRuntime.cs','ControlHooks.cs','ControlSafety.cs','ManualMotionPolicy.cs','NativeFlashlight.cs','NativeFlashlightCompatibility.cs','NativeLightSettings.cs','BotFlashlights.cs','NativeControlView.cs') | ForEach-Object {Join-Path $microRoot ('MicroMaster\src\'+$_)}
RunSuite 'RuntimeSafetyTests' @($runtimeSources+(Join-Path $root 'HeadCamera/src/NativeBotLamp.cs')+(Join-Path $root 'Captain\src\BotControlApi.cs')+(Join-Path $root 'MicroMaster/tests/RuntimeMocks.cs')+(Join-Path $root 'MicroMaster/tests/RuntimeSafetyTests.cs'))
RunSuite 'OwnedFogLifetimeTests' @((Join-Path $root 'HeadCamera/src/OwnedFogLifetime.cs'),(Join-Path $root 'HeadCamera/tests/OwnedFogLifetimeTests.cs'))
RunSuite 'CompatibleReadbackTests' @((Join-Path $root 'HeadCamera/src/CompatibleReadback.cs'),(Join-Path $root 'HeadCamera/tests/CompatibleReadbackTests.cs'))
RunSuite 'BotLightViewScopeTests' @((Join-Path $root 'HeadCamera/src/NativeBotLamp.cs'),(Join-Path $root 'HeadCamera/src/BotLightViewScope.cs'),(Join-Path $root 'HeadCamera/tests/NativeLightViewMocks.cs'),(Join-Path $root 'HeadCamera/tests/BotLightViewScopeTests.cs'))
RunSuite 'FirstPersonCaptureTests' @((Join-Path $root 'HeadCamera/src/FirstPersonCapture.cs'),(Join-Path $root 'HeadCamera/src/FirstPersonRenderScope.cs'),(Join-Path $root 'HeadCamera/src/NativeBotLamp.cs'),(Join-Path $root 'HeadCamera/tests/NativeLightViewMocks.cs'),(Join-Path $root 'HeadCamera/tests/FirstPersonCaptureTests.cs'))
RunSuite 'LightBufferBindingsTests' @((Join-Path $root 'HeadCamera/src/LightBufferBindings.cs'),(Join-Path $root 'HeadCamera/tests/LightBufferBindingsTests.cs'))
RunSuite 'FogCompatibilityTests' @((Join-Path $root 'HeadCamera/src/FogCompatibility.cs'),(Join-Path $root 'HeadCamera/tests/FogCompatibilityTests.cs'))
RunSuite 'ControlMenuOverlayTests' @((Join-Path $root 'HeadCamera/src/ControlEventOverlay.cs'),(Join-Path $root 'HeadCamera/src/ControlView.cs'),(Join-Path $root 'HeadCamera/src/ControlMenuOverlay.cs'),(Join-Path $root 'HeadCamera/tests/ControlMenuOverlayTests.cs'))
RunSuite 'MotionTests' @((Join-Path $microRoot 'MicroMaster\src\ManualMotionPolicy.cs'),(Join-Path $microRoot 'MicroMaster\src\NativeManualWalk.cs'),(Join-Path $root 'HeadCamera\src\ControlView.cs'),(Join-Path $root 'HeadCamera\src\ViewPolicy.cs'),(Join-Path $root 'IntegrationTests/CameraAndControl/MotionMocks.cs'),(Join-Path $root 'IntegrationTests/CameraAndControl/MotionTests.cs'))
RunSuite 'ServiceTests' @((Join-Path $root 'Captain\src\BotControlApi.cs'),(Join-Path $root 'Captain.Commands\src\BotControlService.cs'),(Join-Path $root 'Captain.Commands\src\ControlCommandPolicy.cs'),(Join-Path $root 'Captain.Commands\src\KillBorrowPolicy.cs'),(Join-Path $root 'Captain.Commands/tests/ServiceMocks.cs'),(Join-Path $root 'Captain.Commands/tests/ServiceTests.cs'))
RunSuite 'LeaseClockTests' @((Join-Path $root 'Captain.Commands/src/LeaseClockPolicy.cs'),(Join-Path $root 'Captain.Commands/src/BrokerHeartbeatPatch.cs'),(Join-Path $root 'Captain.Commands/tests/LeaseClockTests.cs'))
$commandSources=@('BotControlService.cs','ControlCommandAdapter.cs','ControlCommandPolicy.cs','ControlCombatContract.cs','KillBorrowPolicy.cs','ItemTaskPolicy.cs','OpenTaskPolicy.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
$commandSources+=@((Join-Path $root 'Captain\src\BotControlApi.cs'),(Join-Path $microRoot 'MicroMaster\src\ManualMotionPolicy.cs'),(Join-Path $root 'IntegrationTests/CommandsAndControl/CommandMocks.cs'),(Join-Path $root 'IntegrationTests/CommandsAndControl/CommandTests.cs'))
RunSuite 'CommandTests' $commandSources
$combatSources=@('SingleKillAdapter.cs','SingleKillPolicy.cs','KillContinuationPolicy.cs','KillBorrowPolicy.cs','FocusOrders.cs','FocusControlTask.cs','ControlCombatTask.cs','ControlCombatContract.cs','ControlCommandPolicy.cs','ItemTaskPolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
RunSuite 'NativeCombatTests' @($combatSources+(Join-Path $root 'Captain.Commands/tests/NativeCombatMocks.cs')+(Join-Path $root 'Captain.Commands/tests/NativeCombatTests.cs'))
$itemSources=@('ItemTaskPolicy.cs','ControlCommandPolicy.cs','ItemRegion.cs','ItemTaskSession.cs','StackCompatibility.cs','ItemArrivalPolicy.cs','ItemInteractionGeometry.cs','BotTaskBridge.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
$itemMocks=@('ItemTaskMocks.cs','ControlCommandExternalMocks.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\tests\'+$_)}
RunSuite 'ItemTaskTests' @($itemSources+$itemMocks+(Join-Path $root 'Captain.Commands\tests\ItemTaskTests.cs'))
RunSuite 'BorrowedItemTests' @($itemSources+$itemMocks+(Join-Path $root 'Captain.Commands/tests/BorrowedItemTests.cs'))
$openSources=@('OpenTaskAdapter.cs','ControlCommandPolicy.cs','OpenTaskPolicy.cs','OpenTargetLocks.cs','OpenTaskDiagnostics.cs','KillBorrowPolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
$openMocks=@('OpenAdapterMocks.cs','ControlCommandExternalMocks.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\tests\'+$_)}
RunSuite 'OpenAdapterTests' @($openSources+$openMocks+(Join-Path $root 'Captain.Commands\tests\OpenAdapterTests.cs'))
RunSuite 'BorrowedOpenTests' @($openSources+$openMocks+(Join-Path $root 'Captain.Commands/tests/BorrowedOpenTests.cs'))
RunSuite 'OpenTaskTests' @((Join-Path $root 'Captain.Commands\src\OpenTaskPolicy.cs'),(Join-Path $root 'Captain.Commands\tests\OpenTaskTests.cs'))
$menuSources=@('CaptainMenuInput.cs','CaptainMenuTree.cs','CaptainCommandCatalog.cs','OpenTaskPolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
RunSuite 'MenuInputTests' @($menuSources+(Join-Path $root 'Captain.Commands\tests\MenuInputTests.cs'))
RunSuite 'HeadAimTests' @((Join-Path $root 'HeadCamera/src/ControlPresentation.cs'),(Join-Path $root 'HeadCamera\src\HeadCameraApi.cs'),(Join-Path $root 'HeadCamera\src\HudViewIdentityApi.cs'),(Join-Path $root 'HeadCamera\src\HudCameraRegistry.cs'),(Join-Path $root 'HeadCamera\src\ControlView.cs'),(Join-Path $root 'HeadCamera\src\ViewPolicy.cs'),(Join-Path $root 'HeadCamera/tests/HeadAimMocks.cs'),(Join-Path $root 'HeadCamera/tests/HeadAimTests.cs'))
RunSuite 'CameraPolicyTests' @((Join-Path $root 'HeadCamera\src\ViewPolicy.cs'),(Join-Path $root 'HeadCamera\tests\PolicyTests.cs'))
RunSuite 'RuntimeViewTests' @((Join-Path $root 'HeadCamera/src/FirstPersonCapture.cs'),(Join-Path $root 'HeadCamera/src/CameraRuntime.cs'),(Join-Path $root 'HeadCamera/src/ViewPolicy.cs'),(Join-Path $root 'HeadCamera/src/ControlView.cs'),(Join-Path $root 'HeadCamera/src/ToggleGesture.cs'),(Join-Path $root 'HeadCamera/src/ControlFeedback.cs'),(Join-Path $root 'HeadCamera/tests/RuntimeViewMocks.cs'),(Join-Path $root 'HeadCamera/tests/RuntimeViewTests.cs'))
RunSuite 'CaptureAnalysisTests' @((Join-Path $root 'HeadCamera/src/CaptureAnalysis.cs'),(Join-Path $root 'HeadCamera/tests/CaptureAnalysisTests.cs'))
RunSuite 'CapturePresentationTests' @((Join-Path $root 'HeadCamera/src/CaptureAnalysis.cs'),(Join-Path $root 'HeadCamera/src/CapturePresentation.cs'),(Join-Path $root 'HeadCamera/tests/PresentationMocks.cs'),(Join-Path $root 'HeadCamera/tests/PresentationTests.cs'))
RunSuite 'KillBorrowTests' @((Join-Path $root 'Captain.Commands\src\KillBorrowPolicy.cs'),(Join-Path $root 'Captain.Commands\src\ItemTaskPolicy.cs'),(Join-Path $root 'Captain.Commands\tests\KillBorrowTests.cs'))
$guardSources=@('ToolSupplyPolicy.cs','ToolSupplyAdapter.cs','FocusOrders.cs','SharedMeleeBridge.cs','TaskBoard.cs','GuardPolicy.cs','GuardTargets.cs','GuardCombat.cs','MovementOrders.cs','PositionTaskPolicy.cs','StationPolicy.cs','KillBorrowPolicy.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
$guardSources+=@('GuardMovementMocks.cs','GuardMovementTests.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\tests\'+$_)}
$guardSources+=Join-Path $root 'Captain.Commands/tests/Fixtures/CaptainRegressionStubs.cs'
$guardSources+=Join-Path $root 'Captain\src\BotControlApi.cs'
RunSuite 'GuardMovementTests' $guardSources
$followSources=@($guardSources | Where-Object { [IO.Path]::GetFileName($_) -ne 'GuardMovementTests.cs' })
$followSources+=Join-Path $root 'Captain.Commands/tests/FollowControlTests.cs'
RunSuite 'FollowControlTests' $followSources
$stationSources=@($guardSources | Where-Object { [IO.Path]::GetFileName($_) -ne 'GuardMovementTests.cs' })
RunSuite 'StationHandoffTests' @($stationSources+(Join-Path $root 'Captain.Commands/tests/StationHandoffTests.cs'))
$continuationSources=@('KillContinuationPolicy.cs','KillCombatBridge.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
$continuationSources+=@('KillContinuationMocks.cs','KillContinuationTests.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\tests\'+$_)}
$continuationSources+=Join-Path $root 'Captain.Commands/tests/Fixtures/CombatPolicy.cs'
RunSuite 'KillContinuationTests' $continuationSources
RunSuite 'RescueTests' @((Join-Path $root 'Captain.Commands\src\RescuePolicy.cs'),(Join-Path $root 'Captain.Commands\tests\RescueTests.cs'))
$rescueSources=@('RescueOrders.cs','RescuePolicy.cs','TaskBoard.cs') | ForEach-Object {Join-Path $root ('Captain.Commands\src\'+$_)}
RunSuite 'RescueAdapterTests' @($rescueSources+(Join-Path $root 'Captain.Commands\tests\RescueAdapterMocks.cs')+(Join-Path $root 'Captain.Commands\tests\RescueAdapterTests.cs'))
$buildRecord=Join-Path $repo 'out/build-manifest.json'
if(Test-Path -LiteralPath $buildRecord){
    $built=Get-Content -LiteralPath $buildRecord -Raw | ConvertFrom-Json
    & (Join-Path $root 'IntegrationTests/Loader/Verify-Dependencies.ps1') -RepositoryRoot $repo -GameRoot $built.GameRoot |
        Tee-Object -FilePath (Join-Path $bin 'PluginDependencyTests.log')
}else{Write-Output 'SKIP: actual BepInEx dependency checks require Build.ps1 first; source/mock tests above are independent.'}
