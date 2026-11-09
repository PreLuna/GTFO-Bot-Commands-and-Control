param([Parameter(Mandatory)][string]$GameRoot,[Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
Add-Type -LiteralPath (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$provider=@(Get-ChildItem -LiteralPath (Join-Path $GameRoot 'BepInEx/plugins') -Recurse -File -Filter 'Hikaria.BoosterTweaker.dll')
if(!$provider.Count){Write-Output 'SKIP: optional BoosterTweaker is not installed.';return}
if($provider.Count -ne 1){throw 'Multiple BoosterTweaker assemblies found.'}
$script:checks=0
function Check([bool]$value,[string]$message){if(!$value){throw $message};$script:checks++}
$mod=[Mono.Cecil.ModuleDefinition]::ReadModule($provider[0].FullName)
$interop=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'BepInEx/interop/Modules-ASM.dll'))
$output=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $RepositoryRoot 'out/package/BepInEx/plugins/PreLuna.Captain.Boosters/PreLuna.Captain.Boosters.dll'))
try{
 $manager=$mod.GetType('Hikaria.BoosterTweaker.Managers.BoosterImplantTemplateManager')
 $perfect=$mod.GetType('Hikaria.BoosterTweaker.Features.PerfectBooster')
 foreach($name in 'EnableCustomPerfectBooster','EnableCustomBooster','DisableBoosterConditions','DisableBoosterNegativeEffects'){
  $p=$manager.Properties|Where-Object Name -eq $name
  Check ($p.PropertyType.FullName -eq 'System.Boolean' -and $p.GetMethod.IsStatic -and $null -ne $p.SetMethod) ('Provider setting shape: '+$name)
 }
 $settings=$perfect.Properties|Where-Object Name -eq 'Settings'
 $type=$mod.GetTypes()|Where-Object FullName -eq $settings.PropertyType.FullName
 foreach($name in 'EnablePerfectBooster','DisableBoosterConsume','EnableBoosterFarmer'){
  $p=$type.Properties|Where-Object Name -eq $name
  Check ($p.PropertyType.FullName -eq 'System.Boolean' -and $null -ne $p.GetMethod -and $null -ne $p.SetMethod) ('Perfect setting shape: '+$name)
 }
 foreach($plan in @(@($manager,'LoadTemplateData'),@($perfect,'ApplyPerfectBoosterImplants'))){
  $method=$plan[0].Methods|Where-Object Name -eq $plan[1]
  Check ($method.IsStatic -and $method.Parameters.Count -eq 0 -and $method.ReturnType.FullName -eq 'System.Void') ('Provider callback shape: '+$plan[1])
 }
 $row=$manager.NestedTypes|Where-Object Name -eq 'BoosterImplantTemplate'
 foreach($name in 'BoosterImplantID','ImplantCategory','EffectGroups','ConditionGroups'){Check ($null -ne ($row.Properties|Where-Object Name -eq $name).GetMethod) ('Runtime template property: '+$name)}
 $native=$interop.GetType('BoosterImplants.BoosterImplantManager')
 $convert=$native.Methods|Where-Object Name -eq 'ActiveBoosterImplantToData'
 Check ($convert.IsStatic -and $convert.Parameters.Count -eq 1 -and $convert.Parameters[0].ParameterType.FullName -eq 'BoosterImplant' -and $convert.ReturnType.FullName -eq 'BoosterImplants.pBoosterImplantData') 'Native packet converter shape'
 $prepare=$interop.GetType('PersistentInventoryManager').Methods|Where-Object Name -eq 'PrepareBoosterImplantForInjection'
 Check ($prepare.Parameters.Count -eq 1 -and $prepare.Parameters[0].ParameterType.FullName -eq 'System.UInt32' -and $prepare.ReturnType.FullName -eq 'System.Void') 'Native prepare shape'
 $compat=$output.GetType('PreLuna.Captain.Boosters.BoosterConditionCompat')
 Check (@($compat.Methods|Where-Object Name -in 'ConditionPrefix','TemplatePostfix').Count -eq 0) 'Old ineffective condition hooks removed'
 Check (@($compat.Methods|Where-Object Name -eq 'PacketPrefix').Count -eq 0) 'Native value-type packet return is not detoured; bot catalog checks inventory before conversion'
 Check ($output.Assembly.Name.Version.ToString() -eq '0.5.3.0') 'Built booster version'
 Write-Output ('PASS: '+$script:checks+' installed provider/interop/built-output contract checks; no plugin or native methods executed.')
}finally{$mod.Dispose();$interop.Dispose();$output.Dispose()}
