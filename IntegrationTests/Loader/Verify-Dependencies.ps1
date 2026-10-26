param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [string]$RepositoryRoot=(Join-Path $PSScriptRoot '../..')
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($RepositoryRoot)
$core=Join-Path $GameRoot 'BepInEx/core'
foreach($name in @('Mono.Cecil.dll','SemanticVersioning.dll','BepInEx.Core.dll')){Add-Type -LiteralPath (Join-Path $core $name)}
$record=Get-Content -LiteralPath (Join-Path $repo 'out/build-manifest.json') -Raw | ConvertFrom-Json
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($core)
$resolver.AddSearchDirectory((Join-Path $GameRoot 'BepInEx/interop'))
$reader=[Mono.Cecil.ReaderParameters]::new(); $reader.AssemblyResolver=$resolver
$versions=@{}; $dependencies=@(); $checks=0; $brokerVerified=$false
foreach($artifact in $record.Artifacts){
 $path=Join-Path $repo $artifact.Path
 if((Get-FileHash -LiteralPath $path).Hash -cne $artifact.SHA256){throw ('Artifact changed: '+$artifact.Id)}
 $module=[Mono.Cecil.ModuleDefinition]::ReadModule($path,$reader)
 try {
  if($artifact.Id -eq 'CoopBotsCustom'){
   $broker=$module.GetTypes() | Where-Object {$_.FullName -eq 'PreLuna.CoopBotsCustom.Commands.BotCommandBroker'}
   $owned=$broker.Methods | Where-Object {$_.Name -eq 'IsOwned'}
   $matches=@($owned.Body.Instructions | Where-Object {$_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldc_R4 -and $_.Operand -is [single] -and $_.Operand -eq 15 -and $_.Previous.Operand -is [Mono.Cecil.FieldReference] -and $_.Previous.Operand.Name -eq 'ExternalUntil' -and $_.Next.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Sub})
   if($matches.Count -ne 1){throw 'Frozen broker lease clock IL does not match the scoped compatibility patch.'}
   $brokerVerified=$true
  }
  foreach($type in $module.GetTypes()){
   $plugin=$type.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'}
   if(!$plugin){continue}
   $guid=[string]$plugin.ConstructorArguments[0].Value
   $versions[$guid]=[SemanticVersioning.Version]::Parse([string]$plugin.ConstructorArguments[2].Value)
   foreach($attribute in $type.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInDependency'}){
    $target=[string]$attribute.ConstructorArguments[0].Value
    $argument=$attribute.ConstructorArguments[1].Value
    $dependency=if($argument -is [string]){[BepInEx.BepInDependency]::new($target,[string]$argument)}else{[BepInEx.BepInDependency]::new($target,[BepInEx.BepInDependency+DependencyFlags][int]$argument)}
    $dependencies+=@{Plugin=$guid;Dependency=$dependency}
   }
  }
 }finally{$module.Dispose()}
}
foreach($entry in $dependencies){
 $dependency=$entry.Dependency
 if(([int]$dependency.Flags -band 1) -eq 0){continue}
 $checks++
 if(!$versions.ContainsKey($dependency.DependencyGUID)){throw ('Missing hard dependency for '+$entry.Plugin+': '+$dependency.DependencyGUID)}
 if($dependency.VersionRange -and !$dependency.VersionRange.IsSatisfied($versions[$dependency.DependencyGUID])){throw ('BepInEx rejects '+$entry.Plugin+' -> '+$dependency.DependencyGUID+' '+$dependency.VersionRange)}
}
$range=($dependencies | Where-Object {$_.Plugin -eq 'PreLuna.MicroMaster' -and $_.Dependency.DependencyGUID -eq 'PreLuna.HeadCamera'}).Dependency.VersionRange
if(!$range){throw 'Missing MicroMaster camera range'}
foreach($version in @('0.3.14','0.3.15','0.3.99')){if(!$range.IsSatisfied([SemanticVersioning.Version]::Parse($version))){throw ('Compatible patch rejected: '+$version)};$checks++}
foreach($version in @('0.3.0','0.3.1','0.3.2','0.3.3','0.3.4','0.3.5','0.3.6','0.3.7','0.3.8','0.3.9','0.3.10','0.3.11','0.3.12','0.3.13','0.4.0','1.0.0')){if($range.IsSatisfied([SemanticVersioning.Version]::Parse($version))){throw ('Unsupported camera accepted: '+$version)};$checks++}
$commands=($dependencies | Where-Object {$_.Plugin -eq 'PreLuna.MicroMaster' -and $_.Dependency.DependencyGUID -eq 'PreLuna.Captain.Commands'}).Dependency.VersionRange
if(!$commands){throw 'Missing MicroMaster command range'}
foreach($version in @('0.5.42','0.5.43','0.5.99')){if(!$commands.IsSatisfied([SemanticVersioning.Version]::Parse($version))){throw ('Compatible commands rejected: '+$version)};$checks++}
foreach($version in @('0.5.40','0.5.41','0.6.0','1.0.0')){if($commands.IsSatisfied([SemanticVersioning.Version]::Parse($version))){throw ('Unsupported commands accepted: '+$version)};$checks++}
$old=[BepInEx.BepInDependency]::new('PreLuna.HeadCamera','0.3.0')
if($old.VersionRange.IsSatisfied([SemanticVersioning.Version]::Parse('0.3.1'))){throw 'Old exact-version regression was not reproduced'};$checks++
Write-Output ('PASS: '+$checks+' real BepInEx dependency range checks; '+$versions.Count+' built plugins; no plugin Load or Unity game executed.')
if(!$brokerVerified){throw 'Missing frozen broker IL compatibility check.'}
Write-Output 'PASS: frozen broker lease clock IL matches the scoped Harmony patch; frozen input and built DLL are unchanged by this check.'
$lightPaths=@(
 (Join-Path (Split-Path $repo -Parent) 'GTFO-Weapon-Attachments-Flashlights-and-Stats/FlashlightCustom/baseline/WangLocal.FlashlightCustom.dll'),
 (Join-Path $GameRoot 'BepInEx/plugins/本地定制/PreLuna.FlashlightCustom/PreLuna.FlashlightCustom.dll')
)
$lightChecked=0
foreach($lightPath in $lightPaths){
 if(!(Test-Path -LiteralPath $lightPath)){continue}
 $module=[Mono.Cecil.ModuleDefinition]::ReadModule($lightPath,$reader)
 try {
  $fog=$module.GetTypes() | Where-Object {$_.Name -eq 'LocalVisualFog'}
  $local=$fog.Methods | Where-Object {$_.Name -eq 'IsLocal' -and $_.IsStatic -and $_.Parameters.Count -eq 1}
  $after=$fog.Methods | Where-Object {$_.Name -eq 'AfterCamera' -and $_.IsStatic -and $_.Parameters.Count -eq 1}
  $ready=$fog.Fields | Where-Object {$_.Name -eq 'ready' -and $_.IsStatic -and $_.FieldType.FullName -eq 'System.Boolean'}
  $master=$fog.Fields | Where-Object {$_.Name -eq 'masterEnabled' -and $_.IsStatic -and $_.FieldType.FullName -eq 'BepInEx.Configuration.ConfigEntry`1<System.Boolean>'}
  if(!$local -or !$after -or !$ready -or !$master -or $local.ReturnType.FullName -ne 'System.Boolean' -or $local.Parameters[0].ParameterType.FullName -ne 'PreLitVolume' -or $after.Parameters[0].ParameterType.FullName -ne 'ClusteredRendering'){throw 'Optional local visual fog bridge signature mismatch.'}
  $nativeScope=@($local.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.Name})
  if('TryGetLocalPlayerAgent' -notin $nativeScope -or 'get_FPSCamera' -notin $nativeScope -or 'get_m_camera' -notin $nativeScope){throw 'Original local-camera fog guard no longer matches the scoped extension.'}
  $lightChecked++
 }finally{$module.Dispose()}
}
if($lightChecked){Write-Output ('PASS: '+$lightChecked+' frozen/installed LocalVisualFog signatures and original FPS-only guard IL checks; read-only, no plugin Load.')}else{Write-Output 'SKIP: optional LocalVisualFog bridge shape checks; no local input DLL available.'}

$headPath=Join-Path $repo ($record.Artifacts | Where-Object {$_.Id -eq 'HeadCamera'}).Path
$module=[Mono.Cecil.ModuleDefinition]::ReadModule($headPath,$reader)
try {
 if($module.GetTypes() | Where-Object {$_.Name -eq 'ObservationLight'}){throw 'Independent observation lamp must be removed.'}
 $capture=($module.GetTypes() | Where-Object {$_.Name -eq 'NativeCapture'}).Methods | Where-Object {$_.Name -eq 'Capture'}
 if($capture.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'ObservationLight'}){throw 'Capture still invokes independent light.'}
 foreach($ins in $capture.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'set_UpdateShadows'}){
  if($ins.Previous.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Ldc_I4_0 -and !($ins.Previous.Operand -is [Mono.Cecil.FieldReference] -and $ins.Previous.Operand.Name -eq 'UpdateSceneShadows')){throw 'Feed shadow updates must inherit the native main renderer setting.'}
 }
 if(!($capture.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'UpdateSceneShadows'})){throw 'Native scene shadow refresh is still hard-disabled.'}
 $fogState=$module.GetTypes() | Where-Object {$_.Name -eq 'FogShaderState'}
 $stateCtor=$fogState.Methods | Where-Object {$_.Name -eq '.ctor'}
 $stateRestore=$fogState.Methods | Where-Object {$_.Name -eq 'Restore'}
 if(!($stateCtor.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetGlobalTexture'}) -or !($stateRestore.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetGlobalTexture'})){throw 'Fog shader scope must save and restore actual global textures.'}
 $stateStrings=@($fogState.Methods | Where-Object {$_.HasBody} | ForEach-Object {$_.Body.Instructions} | Where-Object {$_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr} | ForEach-Object Operand)
 foreach($name in @('_LitVolume','_FogVolume','_DensityNoise','_LitBufferCopy')){if($name -notin $stateStrings){throw ('Missing native volume texture restoration: '+$name)}}
 $diagnosticRead=($module.GetTypes() | Where-Object {$_.Name -eq 'CaptureDiagnostics'}).Methods | Where-Object {$_.Name -eq 'Read'}
 if(!($diagnosticRead.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'CompatibleReadback'})){throw 'Diagnostic reads must use compatible staging.'}
 Write-Output 'PASS: actual volume texture restore, inherited native shadow setting and compatible diagnostic readback are wired into the built camera.'
 $scope=$false
 foreach($handler in @($capture.Body.ExceptionHandlers | Where-Object {$_.HandlerType -eq [Mono.Cecil.Cil.ExceptionHandlerType]::Finally})){
  $calls=@();for($ins=$handler.HandlerStart;$ins -ne $handler.HandlerEnd;$ins=$ins.Next){if($ins.Operand -is [Mono.Cecil.MethodReference]){$calls+=$ins.Operand.DeclaringType.Name+'::'+$ins.Operand.Name}}
  if('FogCompatibility::Restore' -in $calls -and 'RenderGlobals::Restore' -in $calls){$scope=$true}
 }
 if(!$scope){throw 'Capture must retain optional fog scope and main globals restoration in finally.'}
 Write-Output 'PASS: independent lamp/control shadow pass removed; fog/globals finally restoration retained.'
}finally{$module.Dispose()}
$microPath=Join-Path $repo ($record.Artifacts | Where-Object {$_.Id -eq 'MicroMaster'}).Path
$module=[Mono.Cecil.ModuleDefinition]::ReadModule($microPath,$reader)
try {
 $native=$module.GetTypes() | Where-Object {$_.Name -eq 'NativeFlashlight'}
 $tick=$native.Methods | Where-Object {$_.Name -eq 'Tick'}
 $sync=@($tick.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'PlayerSync' -and $_.Operand.Name -eq 'WantsToSetFlashlightEnabled'})
 if($sync.Count -ne 1 -or $sync[0].Previous.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Ldc_I4_0){throw 'Native Sync call must use broadcastOnly=false.'}
 foreach($method in $native.Methods | Where-Object {$_.HasBody}) {
  if($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and ($_.Operand.Name -eq 'set_enabled' -or $_.Operand.DeclaringType.Name -eq 'C_LightDynamic' -and $_.Operand.Name -eq '.ctor')}){throw 'Native control must not enable Unity lighting or allocate another native lamp.'}
  if($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and ($_.Operand.Name -in @('UpdateData','UpdateTransform','UpdateTransformData') -or $_.Operand.DeclaringType.Name -eq 'Light' -and $_.Operand.Name.StartsWith('set_'))}){throw 'Manual GPU refresh and direct light parameter restoration must remain removed.'}
 }
 if(!$tick.Body.ExceptionHandlers.Count){throw 'Native light errors must be contained without ending control.'}
 Write-Output 'PASS: native Sync flashlight request broadcastOnly=false, existing inventory lamp only, no Unity enabled writes; error containment present.'
}finally{$module.Dispose()}
foreach($lightPath in $lightPaths){
 if(!(Test-Path -LiteralPath $lightPath)){continue}
 $module=[Mono.Cecil.ModuleDefinition]::ReadModule($lightPath,$reader)
 try {
  foreach($name in @('Patch_LightIntensity','Patch_LightRange','Patch_LightAngle','Patch_LightInnerAngle','Patch_LightColor','Patch_LightType','Patch_LightCullingMask')){
   $type=$module.GetTypes() | Where-Object {$_.Name -eq $name}
   $target=@($type.Methods | Where-Object {$_.IsStatic -and $_.Name -in @('Prefix','Postfix') -and $_.Parameters.Count -gt 0 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Light'})
   if($target.Count -ne 1 -or $target[0].ReturnType.FullName -notin @('System.Void','System.Boolean')){throw ('Optional flashlight compatibility handler mismatch: '+$name)}
  }
  $gearPrefix=($module.GetTypes() | Where-Object {$_.Name -eq 'Patch_GearManager'}).Methods | Where-Object {$_.Name -eq 'Prefix' -and $_.IsStatic -and $_.Parameters.Count -eq 0}
  if(!$gearPrefix){throw 'Pre-zero settings snapshot target missing.'}
  $sandwich=$module.GetTypes() | Where-Object {$_.Name -eq 'LightSandwich'}
  $writers=@($sandwich.Methods | Where-Object {$_.IsStatic -and $_.Name -in @('ApplySandwich','ApplySandwichInternal','ApplyHelmetInPlace','SuppressRangedSiblings','EnsureRangedSettings') -and $_.ReturnType.FullName -eq 'System.Void' -and $_.Parameters.Count -gt 0 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Light'})
  if($writers.Count -ne 7){throw 'Direct RF writer guard signatures changed.'}
  if(!($sandwich.Methods | Where-Object {$_.Name -eq 'SyncEnabled' -and $_.Parameters.Count -eq 2 -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Transform'})){throw 'RF enable guard signature changed.'}
  $type=$module.GetTypes() | Where-Object {$_.Name -eq 'LightUpdater'}
  $target=@($type.Methods | Where-Object {$_.Name -eq 'IsLocalItem' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'ItemEquippable'})
  if($target.Count -ne 1){throw 'Optional flashlight ownership guard signature mismatch.'}
  $type=$module.GetTypes() | Where-Object {$_.Name -eq 'Patch_BehaviourEnabled'}
  if(!($type.Methods | Where-Object {$_.Name -eq 'Postfix' -and $_.Parameters[0].ParameterType.FullName -eq 'UnityEngine.Behaviour'})){throw 'Optional Behaviour handler mismatch.'}
 }finally{$module.Dispose()}
}
Write-Output 'PASS: frozen/installed flashlight property and ownership handler signatures checked read-only; native Unity/Harmony execution still requires game testing.'

$module=[Mono.Cecil.ModuleDefinition]::ReadModule($headPath,$reader)
try {
 $plugin=$module.GetTypes() | Where-Object {$_.FullName -eq 'PreLuna.HeadCamera.Plugin'}
 $guard=$plugin.Methods | Where-Object {$_.Name -eq 'FogCleanup'}
 if(!$guard -or !($guard.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'OwnsFog'})){throw 'Missing owned PreLitVolume cleanup guard.'}
 $lifetime=$module.GetTypes() | Where-Object {$_.Name -eq 'OwnedFogLifetime'}
 if(!$lifetime){throw 'Missing delayed fog ownership lifetime.'}
 $capture=$module.GetTypes() | Where-Object {$_.Name -eq 'NativeCapture'}
 $owns=$capture.Methods | Where-Object {$_.Name -eq 'OwnsFog'}
 if(!($owns.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'OwnedFogLifetime' -and $_.Operand.Name -eq 'Contains'})){throw 'Native fog guard must use lifetime identity.'}
 Write-Output 'PASS: owned PreLitVolume cleanup guard and identity lifetime are wired into the built camera.'
 $bindings=$module.GetTypes() | Where-Object {$_.Name -eq 'LightBufferBindings'}
 $bind=$bindings.Methods | Where-Object {$_.Name -eq 'Restore'}
 $names=@($bind.Body.Instructions | Where-Object {$_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr} | ForEach-Object Operand)
 foreach($name in @('_CL_PLL','_CL_ELL','_CL_SLL','_CL_FSL','_CB_LightClusters','_LightCluster','_FogCluster')){if($name -notin $names){throw ('Missing global buffer binding: '+$name)}}
 $globals=$module.GetTypes() | Where-Object {$_.Name -eq 'RenderGlobals'}
 $restore=$globals.Methods | Where-Object {$_.Name -eq 'Restore'}
 if(!($restore.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'LightBufferBindings' -and $_.Operand.Name -eq 'Restore'})){throw 'Main render globals do not restore buffer bindings.'}
 $initialize=$capture.Methods | Where-Object {$_.Name -eq 'Initialize'}
 if($initialize.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'AddComponent' -and $_.Operand.GenericArguments.FullName -contains 'ClusteredLightCollector'}){throw 'Unused pre-Setup collector must remain removed.'}
 $dispose=$capture.Methods | Where-Object {$_.Name -eq 'Dispose'}
 $ops=@($dispose.Body.Instructions)
 $bindIndex=-1;$freeIndex=-1
 for($i=0;$i -lt $ops.Count;$i++){if($ops[$i].Operand -is [Mono.Cecil.MethodReference]){if($ops[$i].Operand.DeclaringType.Name -eq 'RenderGlobals' -and $ops[$i].Operand.Name -eq 'Restore' -and $bindIndex -lt 0){$bindIndex=$i};if($ops[$i].Operand.Name -eq 'ReleaseGpu' -and $freeIndex -lt 0){$freeIndex=$i}}}
 if($bindIndex -lt 0 -or $freeIndex -lt 0 -or $bindIndex -ge $freeIndex){throw 'Main buffers must be rebound before GPU resource release.'}
 Write-Output 'PASS: seven per-view buffer bindings, real native collector ownership and rebind-before-release wiring verified.'
}finally{$module.Dispose()}
$module=[Mono.Cecil.ModuleDefinition]::ReadModule($microPath,$reader)
try {
 $hooks=$module.GetTypes() | Where-Object {$_.Name -eq 'ControlHooks'}
 $install=$hooks.Methods | Where-Object {$_.Name -eq 'Install'}
 $hookNames=@($install.Body.Instructions | Where-Object {$_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr} | ForEach-Object Operand)
 foreach($name in @('WantsToSetFlashlightEnabled','ReceiveSetFlashlightStatus','SetFlashlightEnabled','UnWieldFlashlight','FlashlightRequest','FlashlightReceive','FlashlightSet','BeforeLightHandoff','FinishLightHandoff')){if($name -notin $hookNames){throw ('Missing installed switch boundary: '+$name)}}
 $interop=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'BepInEx/interop/Modules-ASM.dll'),$reader)
 try {
  foreach($entry in @(@('Player.PlayerSync','WantsToSetFlashlightEnabled',2),@('PlayerInventoryBase','ReceiveSetFlashlightStatus',2),@('PlayerInventoryBase','SetFlashlightEnabled',3),@('PlayerInventoryBase','UnWieldFlashlight',0))){
   $method=@($interop.GetType($entry[0]).Methods | Where-Object {$_.Name -eq $entry[1] -and $_.Parameters.Count -eq $entry[2] -and $_.ReturnType.FullName -eq 'System.Void' -and !($_.Parameters | Where-Object {$_.ParameterType.FullName -ne 'System.Boolean'})})
   if($method.Count -ne 1){throw ('Native switch signature changed: '+$entry[0]+'::'+$entry[1])}
  }
  if(!($interop.GetType('Player.PlayerSync').Methods | Where-Object {$_.Name -eq 'get_m_agent' -and $_.ReturnType.FullName -eq 'Player.PlayerAgent'})){throw 'Missing real sync owner accessor.'}
  if(@($interop.GetType('Globals.Global').Methods | Where-Object {$_.Name -eq 'OnApplicationQuit' -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Void'}).Count -ne 1){throw 'Native application quit signature changed.'}
 } finally {$interop.Dispose()}
 $finish=$hooks.Methods | Where-Object {$_.Name -eq 'FinishLightHandoff'}
 if(!($finish.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Dispose'})){throw 'Missing exception-safe equipment handoff release.'}
 Write-Output 'PASS: three installed native switch guards, real interop signatures and equipment finalizer verified.'
 foreach($name in @('ApplicationQuitting','OnApplicationQuit')){if($name -notin $hookNames){throw ('Missing early quit hook: '+$name)}}
 foreach($name in @('BeforeHelmetLight','BeforeWieldLight','FlashlightRequest','FlashlightReceive','FlashlightSet')){
  $method=$hooks.Methods | Where-Object Name -eq $name
  if($method.ReturnType.FullName -ne 'System.Boolean' -or !($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'NativeLightLifetime' -and $_.Operand.Name -eq 'Allows'})){throw ('Missing teardown identity guard: '+$name)}
 }
 $lifetime=$module.GetTypes() | Where-Object Name -eq 'NativeLightLifetime'
 if(!$lifetime -or !($lifetime.Fields | Where-Object {$_.IsStatic -and $_.Name -eq 'retiring'})){throw 'Teardown guard must survive runner destruction.'}
 $nativeEnd=($module.GetTypes() | Where-Object Name -eq 'NativeFlashlight').Methods | Where-Object Name -eq 'End'
 if($nativeEnd.Parameters.Count -ne 1 -or $nativeEnd.Parameters[0].ParameterType.FullName -ne 'System.Boolean'){throw 'Missing discard-without-switch-restore path.'}
 Write-Output 'PASS: early native quit hook, teardown switch/setup guards, runner-independent identity lifetime and discard path are wired into the built plugin.'
 foreach($name in @('BeforeWieldLight','BeforeHelmetLight','AfterLightSetup','LightData','LightOn','LightRange','LightAngle','LightIntensity')){if(!($hooks.Methods | Where-Object {$_.Name -eq $name})){throw ('Missing native lamp boundary hook: '+$name)}}
 $settings=$module.GetTypes() | Where-Object {$_.Name -eq 'NativeLightSettings'}
 if(!$settings){throw 'Missing pre-RF settings snapshots.'}
 Write-Output 'PASS: built native lamp settings snapshot and SetOn/UpdateData/equipment boundary guards are present.'
 $runtime=$module.GetTypes() | Where-Object {$_.Name -eq 'ControlRuntime'}
 $update=$runtime.Methods | Where-Object {$_.Name -eq 'Update'}
 $calls=@($update.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.DeclaringType.Name+'::'+$_.Operand.Name})
 $lampTick=[Array]::IndexOf($calls,'BotFlashlights::Tick');$busy=[Array]::IndexOf($calls,'ControlRuntime::get_Busy')
 if($lampTick -lt 0 -or $busy -lt 0 -or $lampTick -ge $busy){throw 'Global lamps must tick before the input-session early return.'}
 $exit=$runtime.Methods | Where-Object {$_.Name -eq 'Exit'}
 if(!($exit.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'BotFlashlights' -and $_.Operand.Name -eq 'Release'})){throw 'Control exit must release only session-scoped lamps.'}
 $service=$module.GetTypes() | Where-Object {$_.Name -eq 'BotFlashlights'}
 if(!$service){throw 'Missing independent bot light service.'}
 foreach($method in $service.Methods | Where-Object {$_.HasBody}){
  if($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'ControlRuntime' -and $_.Operand.Name -eq 'get_BlocksBody'}){throw 'Global light ownership must not depend on body input isolation.'}
 }
 $native=$module.GetTypes() | Where-Object {$_.Name -eq 'NativeFlashlight'}
 $refresh=$native.Methods | Where-Object {$_.Name -eq 'RefreshRenderState'}
 if(!$refresh -or !($refresh.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'NativeBotLamp' -and $_.Operand.Name -eq 'Inspect'})){throw 'RF layer suppression must depend on inspected native render state.'}
 if(!($hooks.Methods | Where-Object {$_.Name -eq 'AfterLightDisabled'})){throw 'Missing observation of native temporary light disabling.'}
 Write-Output 'PASS: built global lamp maintenance runs without a control session; T/Q release uses policy-specific ownership.'
}finally{$module.Dispose()}
$module=[Mono.Cecil.ModuleDefinition]::ReadModule($headPath,$reader)
try {
 $scope=$module.GetTypes() | Where-Object {$_.Name -eq 'BotLightViewScope'}
 if(!$scope){throw 'Missing native bot FPS light render scope.'}
 $restore=$scope.Methods | Where-Object {$_.Name -eq 'Dispose'}
 $calls=@($restore.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.Name})
 foreach($name in @('set_IsFPSPlayerLight','set_IsForced','set_IsFPSFlashlight','set_ForcedShadowRender','SetGlobalInt')){if($name -notin $calls){throw ('Missing per-view lamp restoration: '+$name)}}
 if(@($calls | Where-Object {$_ -eq 'set_IsFPSFlashlight'}).Count -ne 2){throw 'Both body and observed FPS flags must be restored.'}
 $capture=($module.GetTypes() | Where-Object {$_.Name -eq 'NativeCapture'}).Methods | Where-Object {$_.Name -eq 'Capture'}
 $restoredBeforeRecull=$false
 foreach($handler in $capture.Body.ExceptionHandlers | Where-Object {$_.HandlerType -eq [Mono.Cecil.Cil.ExceptionHandlerType]::Finally}){
  $ops=@();for($ins=$handler.HandlerStart;$ins -ne $handler.HandlerEnd;$ins=$ins.Next){if($ins.Operand -is [Mono.Cecil.MethodReference]){$ops+=$ins.Operand.DeclaringType.Name+'::'+$ins.Operand.Name}}
  $end=[Array]::IndexOf($ops,'BotLightViewScope::Dispose');$recull=[Array]::IndexOf($ops,'C_Camera::RunVisibility')
  if($end -ge 0 -and $recull -gt $end){$restoredBeforeRecull=$true}
 }
 if(!$restoredBeforeRecull){throw 'FPS light fields and shader state must restore in finally before main-camera reculling.'}
 if($scope.Methods | Where-Object {$_.HasBody} | ForEach-Object {$_.Body.Instructions} | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'set_m_isFPSPlayerLight'}){throw 'Raw FPS flag writes bypass the native shadow-generator role.'}
 $inspect=($module.GetTypes() | Where-Object {$_.Name -eq 'NativeBotLamp'}).Methods | Where-Object {$_.Name -eq 'Inspect'}
 if(!$inspect -or !($inspect.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_Data'})){throw 'Native lamp readiness must inspect actual clustered data.'}
 Write-Output 'PASS: native lamp FPS fields, forced shadow source and shader index restore before the main camera reculls.'
}finally{$module.Dispose()}
