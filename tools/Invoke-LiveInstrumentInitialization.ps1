param(
    [string]$RuntimeRoot = "E:\FST\TestDLL\ManualCanDebug\ManualCanDebug\bin\Release",
    [string]$ReportPath = "E:\FST\TestDLL\artifacts\live-instrument-initialization.json"
)

$ErrorActionPreference = "Stop"
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $RuntimeRoot "ManualCanDebug.exe"))
$runtimeType = $assembly.GetType("ManualCanDebug.LegacySequenceRuntime", $true)
$constructor = $runtimeType.GetConstructors([Reflection.BindingFlags]"Instance,Public,NonPublic") | Select-Object -First 1
$initialize = $runtimeType.GetMethods() | Where-Object { $_.Name -eq "InitializeInstrumentsAsync" -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
$shutdown = $runtimeType.GetMethods() | Where-Object { $_.Name -eq "SafeShutdownAsync" -and $_.GetParameters().Count -eq 0 } | Select-Object -First 1
$runSingle = $runtimeType.GetMethods() | Where-Object { $_.Name -eq "RunSingleStepAsync" -and $_.GetParameters().Count -eq 2 } | Select-Object -First 1
$lastExecution = $runtimeType.GetProperty("LastStepExecution")
$configPath = Join-Path $RuntimeRoot "Config\InstrumentConfig.json"
$config = [System.IO.File]::ReadAllText($configPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$sequence = Join-Path $RuntimeRoot "Sequence\Instrument_Acceptance_All_Safe.json"
$sequenceDocument = [System.IO.File]::ReadAllText($sequence, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$names = @("DUTCAN", "AUXCAN", "RESOLVERCAN", "DMM_HV", "DMM_LV", "RES_1", "RES_2", "RES_3", "RELAY_FCT", "RELAY_HVMUX", "DCDC_LOAD", "HVDC", "LVDC", "LVDC_KL15")
$results = @()
$constructorArguments = [object[]]@([string]$RuntimeRoot, [string]$sequence)
$runtime = $constructor.Invoke($constructorArguments)

foreach ($name in $names) {
    $item = $config | Where-Object Name -eq $name | Select-Object -First 1
    if ($null -eq $item) { $results += [pscustomobject]@{ Name=$name; Success=$false; Message="Missing configuration" }; continue }
    try {
        $payload = ConvertTo-Json -InputObject @($item) -Depth 8 -Compress
        $initializeArguments = [object[]]@([string]$payload)
        $task = $initialize.Invoke($runtime, $initializeArguments); $task.GetAwaiter().GetResult()
        $stepDevice = if ($name -eq "DUTCAN") { "PRODUCTCAN" } elseif ($name -eq "RESOLVERCAN") { "RESOLVER" } else { $name }
        $indexes = @(); for ($index = 0; $index -lt $sequenceDocument.StepList.Count; $index++) { $step = $sequenceDocument.StepList[$index]; if ($step.RunMode -eq "Normal" -and $step.Device -eq $stepDevice) { $indexes += $index } }
        $actionResults = @(); foreach ($index in $indexes) { $arguments = [object[]]@([string]$sequence, [int]$index); $stepTask = $runSingle.Invoke($runtime, $arguments); $actionResult = [string]$stepTask.GetAwaiter().GetResult(); $actionResults += $actionResult; if ($actionResult -match "^(Error|Failed|Fail)$") { $detail = $lastExecution.GetValue($runtime, $null); throw "Safe action failed at STEP $($index + 1): $($sequenceDocument.StepList[$index].StepName). $detail" } }
        $results += [pscustomobject]@{ Name=$name; Success=$true; Resource=$item.Resource; ActionCount=$indexes.Count; ActionResults=($actionResults -join " | "); Message="MainTest initialization and safe actions passed" }
    }
    catch {
        $message = $_.Exception.ToString()
        $results += [pscustomobject]@{ Name=$name; Success=$false; Resource=$item.Resource; Message=$message }
    }
    finally {
        try { $task = $shutdown.Invoke($runtime, @()); $task.GetAwaiter().GetResult() | Out-Null } catch {}
    }
}
try { $runtime.Dispose() } catch {}

$directory = Split-Path -Parent $ReportPath
if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$results | Select-Object Name,Success,Resource,@{N="Message";E={ if ($_.Success) { $_.Message } else { ($_.Message -split "`r?`n")[0] } }} | Format-Table -AutoSize
