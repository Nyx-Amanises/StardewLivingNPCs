#requires -Version 7.0
<#
.SYNOPSIS
Compare the old full metadata contract with scenario-selected contracts using built production DLLs.
.DESCRIPTION
Runs each version in its own PowerShell process. The current worker snapshots repository assets,
renders one shared fixture, and the baseline worker consumes those exact bytes. No game, provider,
configuration file, save, or network client is initialized. Character counts are .NET String.Length
(UTF-16 code units, including CR/LF), not tokens or model latency. No assemblies are built or deployed.
.EXAMPLE
./tools/measure_scene_action_contract.ps1 -BaselineRepositoryRoot C:/Temp/old-checkout -CurrentRepositoryRoot C:/Temp/new-checkout
#>
[CmdletBinding()]
param(
    [string] $BaselineRepositoryRoot = '',
    [string] $CurrentRepositoryRoot = '',
    [string] $AssetRepositoryRoot = '',
    [string] $BaselineAssemblyPath = '',
    [string] $CurrentAssemblyPath = '',
    [string] $OutputDirectory = '',
    [switch] $SmokeOnly,
    [Parameter(DontShow)] [ValidateSet('driver', 'current', 'baseline')] [string] $WorkerMode = 'driver'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
if ([string]::IsNullOrWhiteSpace($CurrentRepositoryRoot)) { $CurrentRepositoryRoot = Split-Path $PSScriptRoot -Parent }
if ([string]::IsNullOrWhiteSpace($BaselineRepositoryRoot)) { throw 'BaselineRepositoryRoot is required.' }
$CurrentRepositoryRoot = [IO.Path]::GetFullPath($CurrentRepositoryRoot)
$BaselineRepositoryRoot = [IO.Path]::GetFullPath($BaselineRepositoryRoot)
if ([string]::IsNullOrWhiteSpace($AssetRepositoryRoot)) { $AssetRepositoryRoot = $CurrentRepositoryRoot }
$AssetRepositoryRoot = [IO.Path]::GetFullPath($AssetRepositoryRoot)
if ([string]::IsNullOrWhiteSpace($BaselineAssemblyPath)) { $BaselineAssemblyPath = Join-Path $BaselineRepositoryRoot 'LivingNPCs/bin/Debug/net6.0/LivingNPCs.dll' }
if ([string]::IsNullOrWhiteSpace($CurrentAssemblyPath)) { $CurrentAssemblyPath = Join-Path $CurrentRepositoryRoot 'LivingNPCs/bin/Debug/net6.0/LivingNPCs.dll' }
$BaselineAssemblyPath = [IO.Path]::GetFullPath($BaselineAssemblyPath)
$CurrentAssemblyPath = [IO.Path]::GetFullPath($CurrentAssemblyPath)
foreach ($path in @($BaselineAssemblyPath, $CurrentAssemblyPath)) {
    if (-not [IO.File]::Exists($path)) { throw "Built production DLL missing: $path" }
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('LivingNPCs-scene-contract-measure-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$fixturePath = Join-Path $OutputDirectory 'fixture.json'

function Write-JsonFile([string] $Path, [object] $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 50), [Text.UTF8Encoding]::new($false))
}

function Get-Hash([string] $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function Invoke-IsolatedWorker([string] $Mode) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = Join-Path $PSHOME 'pwsh.exe'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($arg in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $PSCommandPath,
        '-WorkerMode', $Mode, '-BaselineRepositoryRoot', $BaselineRepositoryRoot,
        '-CurrentRepositoryRoot', $CurrentRepositoryRoot, '-AssetRepositoryRoot', $AssetRepositoryRoot,
        '-BaselineAssemblyPath', $BaselineAssemblyPath, '-CurrentAssemblyPath', $CurrentAssemblyPath,
        '-OutputDirectory', $OutputDirectory)) { $startInfo.ArgumentList.Add($arg) }
    if ($SmokeOnly) { $startInfo.ArgumentList.Add('-SmokeOnly') }
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "$Mode worker failed ($($process.ExitCode)):`n$errorOutput`n$output" }
        if (-not [string]::IsNullOrWhiteSpace($errorOutput)) { Write-Warning $errorOutput.Trim() }
    }
    finally { $process.Dispose() }
}

if ($WorkerMode -eq 'driver') {
    Invoke-IsolatedWorker 'current'
    Invoke-IsolatedWorker 'baseline'
    $current = Get-Content -LiteralPath (Join-Path $OutputDirectory 'current.json') -Raw | ConvertFrom-Json -AsHashtable
    $baseline = Get-Content -LiteralPath (Join-Path $OutputDirectory 'baseline.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($current.FixtureSha256 -ne $baseline.FixtureSha256) { throw 'Versions did not use identical input bytes.' }
    $baselineRows = @{}
    foreach ($row in $baseline.Scenarios) { $baselineRows[$row.Key] = $row }
    $comparison = @($current.Scenarios | ForEach-Object {
        $before = $baselineRows[$_.Key]
        [pscustomobject][ordered]@{
            Locale = $_.Locale; Scenario = $_.Scenario; Plan = $_.PlanLabel; IsFallback = $_.Plan.IsFallback
            BaselineContractChars = $before.ContractCharacters
            CoreChars = $_.CoreContractCharacters; SceneChars = $_.SceneContractCharacters
            CurrentContractChars = $_.ContractCharacters
            ContractSavedChars = $before.ContractCharacters - $_.ContractCharacters
            ContractSavedPercent = [Math]::Round(100.0 * ($before.ContractCharacters - $_.ContractCharacters) / $before.ContractCharacters, 2)
            BaselineTotalChars = $before.TotalCharacters; CurrentTotalChars = $_.TotalCharacters
            TotalSavedChars = $before.TotalCharacters - $_.TotalCharacters
            TotalSavedPercent = [Math]::Round(100.0 * ($before.TotalCharacters - $_.TotalCharacters) / $before.TotalCharacters, 2)
            NonContractDelta = ($before.TotalCharacters - $before.ContractCharacters) - ($_.TotalCharacters - $_.ContractCharacters)
        }
    })
    $baselineContractChars = [int]$baseline.Scenarios[0].ContractCharacters
    foreach ($combination in $current.FamilyCombinations) {
        $combination['BaselineContractChars'] = $baselineContractChars
        $combination['SavedChars'] = $baselineContractChars - [int]$combination.CombinedChars
        $combination['SavedPercent'] = [Math]::Round(100.0 * $combination.SavedChars / $baselineContractChars, 2)
    }
    $comparison | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'comparison.csv') -NoTypeInformation -Encoding utf8
    $current.FamilyCombinations | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'family-combinations.csv') -NoTypeInformation -Encoding utf8
    Write-JsonFile (Join-Path $OutputDirectory 'comparison.json') ([ordered]@{
        Unit = 'UTF-16 code units (.NET String.Length), including Windows CR/LF; not tokens'
        ScriptSha256 = Get-Hash $PSCommandPath
        BaselineAssembly = $baseline.Assembly; CurrentAssembly = $current.Assembly
        FixtureSha256 = $current.FixtureSha256; AssetSources = $current.AssetSources
        Scope = $current.Scope; Comparisons = $comparison
        FamilyCombinations = $current.FamilyCombinations
    })
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# Scene action contract: offline character comparison')
    $lines.Add('')
    foreach ($note in $current.Scope) { $lines.Add('- ' + $note) }
    $lines.Add('- Unit: UTF-16 code units, including CR/LF. These are not model tokens or response-time measurements.')
    $lines.Add('- Total = actual AssembledPrompt.TotalCharacters; ResponseStart is reported separately in per-version JSON.')
    $lines.Add('- NonContractDelta compares non-contract prompt lengths; zero means those lengths match, not that their bytes or contents were compared.')
    $lines.Add('')
    $lines.Add('| Locale | Scene | Selected families | Old contract | Core + scene | Contract saved | Old total | New total | Total saved | Non-contract delta |')
    $lines.Add('|---|---|---|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($row in $comparison) {
        $lines.Add("| $($row.Locale) | $($row.Scenario) | $($row.Plan) | $($row.BaselineContractChars) | $($row.CoreChars) + $($row.SceneChars) | $($row.ContractSavedChars) ($($row.ContractSavedPercent)%) | $($row.BaselineTotalChars) | $($row.CurrentTotalChars) | $($row.TotalSavedChars) ($($row.TotalSavedPercent)%) | $($row.NonContractDelta) |")
    }
    $lines.Add('')
    $lines.Add('All 64 family combinations are in family-combinations.csv. Mask 63 is verified identical to the full fallback scene contract.')
    $lines.Add('')
    $lines.Add('Baseline SHA256: ' + $baseline.Assembly.Sha256)
    $lines.Add('Current SHA256: ' + $current.Assembly.Sha256)
    $lines.Add('Shared fixture SHA256: ' + $current.FixtureSha256)
    [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'report.md'), $lines, [Text.UTF8Encoding]::new($false))
    [pscustomobject]@{
        Status = $(if ($SmokeOnly) { 'Smoke passed' } else { 'Measured' })
        Scenarios = $comparison.Count; FamilyCombinations = $current.FamilyCombinations.Count
        BaselineSha256 = $baseline.Assembly.Sha256; CurrentSha256 = $current.Assembly.Sha256
        OutputDirectory = $OutputDirectory; Report = Join-Path $OutputDirectory 'report.md'
    } | ConvertTo-Json
    exit 0
}

# Only workers load a production version. Byte loading does not lock build outputs.
$assemblyPath = if ($WorkerMode -eq 'current') { $CurrentAssemblyPath } else { $BaselineAssemblyPath }
$versionRoot = if ($WorkerMode -eq 'current') { $CurrentRepositoryRoot } else { $BaselineRepositoryRoot }
$dependencyRoots = [string[]]@(
    [IO.Path]::GetDirectoryName($assemblyPath),
    (Join-Path $versionRoot 'LivingNPCs.Tests/bin/Debug/net6.0'),
    (Join-Path $versionRoot 'LivingNPCs.Tests/bin/Release/net6.0')
)
$resolverBlock = {
    param([object] $sender, [ResolveEventArgs] $request)
    $name = [Reflection.AssemblyName]::new($request.Name).Name
    foreach ($loaded in [AppDomain]::CurrentDomain.GetAssemblies()) {
        if ($loaded.GetName().Name -eq $name) { return $loaded }
    }
    foreach ($directory in $dependencyRoots) {
        $candidate = [IO.Path]::Combine($directory, $name + '.dll')
        if ([IO.File]::Exists($candidate)) { return [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($candidate)) }
    }
    return $null
}.GetNewClosure()
$resolver = [ResolveEventHandler]$resolverBlock
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assemblyPath))

function Get-ProductionType([string] $Name) { return $assembly.GetType($Name, $true) }
function New-ProductionObject([string] $Name) { return [Activator]::CreateInstance((Get-ProductionType $Name), $true) }
function Set-Value([object] $Object, [string] $Name, [object] $Value, [switch] $Optional) {
    $property = $Object.GetType().GetProperty($Name, $flags)
    if ($null -eq $property) {
        if ($Optional) { return }
        throw "Missing property $($Object.GetType().FullName).$Name"
    }
    # Pass the converted value directly: returning a singleton array through an if/pipeline
    # would unwrap it into one ConversationTurn instead of preserving IReadOnlyList<T>.
    if ($null -eq $Value) { $property.SetValue($Object, $null) }
    else { $property.SetValue($Object, [Management.Automation.LanguagePrimitives]::ConvertTo($Value, $property.PropertyType)) }
}
function Invoke-Fragment([string] $NestedType, [string] $Method, [object[]] $Arguments) {
    $type = Get-ProductionType ('LivingNPCs.Behavior.PromptFragments+' + $NestedType)
    return $type.GetMethod($Method, $flags).Invoke($null, $Arguments)
}
function Get-FragmentField([string] $NestedType, [string] $Field) {
    return (Get-ProductionType ('LivingNPCs.Behavior.PromptFragments+' + $NestedType)).GetField($Field, $flags).GetValue($null)
}
function Get-PromptMap([System.Collections.IDictionary] $Assets, [string] $Locale) {
    $map = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($pair in $Assets.DefaultPrompts.GetEnumerator()) { $map[$pair.Key] = [string]$pair.Value }
    if ($Locale -eq 'zh') {
        foreach ($pair in $Assets.ZhPrompts.GetEnumerator()) {
            if (-not ([string]$pair.Value).StartsWith('(no translation', [StringComparison]::Ordinal)) { $map[$pair.Key] = [string]$pair.Value }
        }
    }
    return ,$map
}
function New-PromptLookup([Collections.Generic.Dictionary[string,string]] $Map) {
    $replaceTokens = (Get-ProductionType 'LivingNPCs.Dialogue.Content.PromptTable').GetMethod('ReplaceTokens', $flags)
    $lookupBlock = {
        param([string] $key, [object] $tokens, [bool] $optimized)
        $keys = if ($optimized) { @(($key + '.Optimized.FemaleNpc'), ($key + '.Optimized'), ($key + '.FemaleNpc'), $key) } else { @(($key + '.FemaleNpc'), $key) }
        foreach ($candidateKey in $keys) {
            if ($Map.ContainsKey($candidateKey)) {
                return [string]$replaceTokens.Invoke($null, [object[]]@($Map[$candidateKey], $tokens))
            }
        }
        return $null
    }.GetNewClosure()
    return [Management.Automation.LanguagePrimitives]::ConvertTo($lookupBlock, (Get-ProductionType 'LivingNPCs.Dialogue.Engine.PromptTextLookup'))
}
function New-BehaviorContext([string] $Kind, [string] $NpcDisplayName) {
    if ($Kind -eq 'unknown') { return 'External behavior context has no recognized capability fields.' }
    $allowsHelp = $Kind -in @('help', 'multiple')
    $parts = [Collections.Generic.List[string]]::new()
    $parts.Add((Invoke-Fragment 'Context' 'Header' @($NpcDisplayName)))
    $parts.Add((Get-FragmentField 'Context' 'Purpose'))
    $parts.Add((Get-FragmentField 'Context' 'CurrentStateHeading'))
    # These fixed scene/state values are synthetic captured data, not a live game snapshot.
    $parts.Add('- Scene: quiet afternoon; location: Town; spring 15; time: 1330.')
    $parts.Add('- Mood: calm; attention to farmer: 60/100; openness: 55/100; response inclination: chat.')
    $readiness = if ($allowsHelp) {
        Invoke-Fragment 'Context' 'HelpRequestReadinessAllowed' @('a modest favor fits today')
    } else {
        Invoke-Fragment 'Context' 'HelpRequestReadinessBlocked' @('no new favor is appropriate now')
    }
    $parts.Add((Invoke-Fragment 'Context' 'HelpRequestReadinessLine' @($readiness)))
    if ($allowsHelp -or $Kind -eq 'active-help') {
        $parts.Add((Get-FragmentField 'Context' 'HelpRequestLifecycleLine'))
        $parts.Add((Invoke-Fragment 'Context' 'HelpRequestFitLine' @('theme practical; currently reasonable item requests: Wood (O)388; Milk (O)184; item_request only.')))
    }
    if ($Kind -eq 'active-help') {
        $parts.Add((Invoke-Fragment 'Context' 'HelpRequestsLine' @('item_request, due tomorrow, status Pending, step 1/2; current step: Wood (O)388; summary: first Wood, then Milk')))
    } else {
        $parts.Add((Invoke-Fragment 'Context' 'EmptyStoresLine' @(,[string[]]@('help requests'))))
    }
    if ($Kind -in @('gift', 'multiple')) {
        $parts.Add((Invoke-Fragment 'GiftOpportunity' 'Section' @($NpcDisplayName,
            (Get-FragmentField 'GiftOpportunity' 'DefaultDailyCue'), '(O)20 Leek; (O)18 Daffodil', '(O)591 Tulip')))
    } else {
        $parts.Add((Invoke-Fragment 'GiftOpportunity' 'NoOpportunitySection' @()))
    }
    if ($allowsHelp) { $parts.Add((Invoke-Fragment 'HelpRequestOpportunity' 'Section' @($NpcDisplayName))) }
    return [string]::Join("`n", $parts)
}

$familyNames = @('IncludeTravel', 'IncludeGifts', 'IncludeNewHelp', 'IncludeHelpUpdates', 'IncludeMoney', 'IncludeFestival')
function Get-PlanValues([object] $Plan) {
    $values = [ordered]@{}
    foreach ($name in $familyNames + @('IsFallback', 'Reason')) { $values[$name] = $Plan.GetType().GetProperty($name).GetValue($Plan) }
    return $values
}
function Get-PlanLabel([System.Collections.IDictionary] $Plan) {
    $selected = @($familyNames | Where-Object { $Plan[$_] } | ForEach-Object { $_.Substring(7) })
    if ($selected.Count -eq 0) { return 'core-only' }
    return ($selected -join '+') + $(if ($Plan.IsFallback) { ' (fallback)' } else { '' })
}

if ($WorkerMode -eq 'current') {
    $relativeAssets = [ordered]@{
        DefaultPrompts = 'prompts/default.json'; ZhPrompts = 'prompts/zh.json'
        EnglishBio = 'bios/Penny.json'; ChineseBio = 'bios-zh/Penny.json'; World = 'world/GameSummary.json'
    }
    $assets = [ordered]@{}
    $sources = [Collections.Generic.List[object]]::new()
    foreach ($pair in $relativeAssets.GetEnumerator()) {
        $path = Join-Path (Join-Path $AssetRepositoryRoot 'LivingNPCs/assets/dialogue') $pair.Value
        $raw = [IO.File]::ReadAllText($path)
        $assets[$pair.Key] = if ($pair.Key -in @('DefaultPrompts', 'ZhPrompts')) { ConvertFrom-Json -InputObject $raw -AsHashtable } else { $raw }
        $sources.Add([ordered]@{ Path = $path; Sha256 = Get-Hash $path; Characters = $raw.Length })
    }
    $definitions = @(
        @{ Id = 'greeting'; Kind = 'restricted'; En = 'Good morning, Penny.'; Zh = '早上好，潘妮。' },
        @{ Id = 'preference'; Kind = 'restricted'; En = 'I love poppies and want to read more books.'; Zh = '我喜欢虞美人，也想多读一点书。' },
        @{ Id = 'current-gift'; Kind = 'gift'; En = 'It is a lovely afternoon.'; Zh = '今天天气真好。' },
        @{ Id = 'new-help'; Kind = 'help'; En = 'Do you need anything today?'; Zh = '今天有什么需要我帮忙的吗？' },
        @{ Id = 'existing-help-reminder'; Kind = 'active-help'; En = 'What were the items you asked me for?'; Zh = '上次你要的东西是什么？' },
        @{ Id = 'invitation'; Kind = 'restricted'; En = "Let's go to the Beach together now."; Zh = '现在一起去海边吧。' },
        @{ Id = 'short-continuation'; Kind = 'restricted'; En = 'Sounds good.'; Zh = '好呀。' },
        @{ Id = 'multiple-families'; Kind = 'multiple'; En = "Let's go to the Beach. Could you give me a flower and lend me 50 gold?"; Zh = '一起去海边吧。能送我一朵花，再借我50金币吗？' },
        @{ Id = 'unknown-context-fallback'; Kind = 'unknown'; En = 'Hi, Penny.'; Zh = '你好，潘妮。' }
    )
    if ($SmokeOnly) { $definitions = @($definitions | Where-Object { $_.Id -in @('greeting', 'short-continuation', 'unknown-context-fallback') }) }
    $worlds = [ordered]@{}
    $scenarios = [Collections.Generic.List[object]]::new()
    foreach ($locale in @('en', 'zh')) {
        $map = Get-PromptMap $assets $locale
        $worldLookupBlock = { param([string] $key); if ($map.ContainsKey($key)) { return $map[$key] }; return '' }.GetNewClosure()
        $rendererType = Get-ProductionType 'LivingNPCs.Dialogue.Content.WorldSummaryRenderer'
        $renderer = $rendererType.GetConstructors()[0].Invoke([object[]]@([Func[string,string]]$worldLookupBlock, $null))
        $world = [Newtonsoft.Json.JsonConvert]::DeserializeObject([string]$assets.World, (Get-ProductionType 'LivingNPCs.Dialogue.Content.WorldSummary'))
        $worlds[$locale] = [string]$rendererType.GetMethod('Render').Invoke($renderer, [object[]]@($world))
        foreach ($definition in $definitions) {
            $player = if ($locale -eq 'zh') { $definition.Zh } else { $definition.En }
            $displayName = if ($locale -eq 'zh') { '潘妮' } else { 'Penny' }
            $conversation = [Collections.Generic.List[object]]::new()
            if ($definition.Id -eq 'short-continuation') {
                $conversation.Add(@{ Text = $(if ($locale -eq 'zh') { '我们一起去海边吧。' } else { "Let's go to the Beach together." }); IsPlayer = $true; Id = 'prior-player' })
                $conversation.Add(@{ Text = $(if ($locale -eq 'zh') { '让我先把书收好，再拿件外套。' } else { 'Let me put my books away and get my coat.' }); IsPlayer = $false; Id = 'prior-npc' })
            }
            $conversation.Add(@{ Text = $player; IsPlayer = $true; Id = 'current-player' })
            $scenarios.Add([ordered]@{
                Key = $locale + '/' + $definition.Id; Locale = $locale; Id = $definition.Id
                PlayerText = $player; NpcDisplayName = $displayName; Conversation = $conversation
                BehaviorContext = New-BehaviorContext $definition.Kind $displayName
                IsEventActive = $definition.Id -eq 'multiple-families'
            })
        }
    }
    Write-JsonFile $fixturePath ([ordered]@{
        Assets = $assets; AssetSources = $sources; RenderedWorld = $worlds; Scenarios = $scenarios
        Scope = @(
            'Both production versions consume exactly the same fixture.json bytes; each runs in an isolated PowerShell process.'
            'Real assets: repository default/zh prompt dictionaries, English/Chinese Penny bios, and the vanilla GameSummary rendered once by the current production WorldSummaryRenderer.'
            'Actual production PromptAssembler runs with Full context routing in both versions. World retrieval and semantic routing are not run or compared here.'
            'Synthetic inputs: year 2, spring 15, 13:30, Town, 4 hearts, farmer Xiao Gang; controlled current dialogue/history and runtime PromptFragments capability sections.'
            'Help item-fit and pending-request values are synthetic, with Wood then Milk; they are not an actual captured save state.'
            'Repository prompt lookup uses FemaleNpc variants and production PromptTable.ReplaceTokens; game string preprocessing, content-pack patches, runtime portrait matching and saved histories are not invoked.'
            'Penny biography and world content are full; sample dialogue and saved event history are empty. This fixture is a controlled comparison, not a prediction of every live request size.'
            'No model calls, network, game initialization, user configuration, API keys or save files are used. No time-to-first-token or model latency is measured.'
        )
    })
}

$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json -AsHashtable
$oldInlineMethod = (Get-ProductionType 'LivingNPCs.Dialogue.Engine.LivingNpcMetadataExtractionPass').GetMethod('BuildInlineInstructions', $flags, $null, [Type[]]@(), $null)
$fullInline = [string]$oldInlineMethod.Invoke($null, @())
$contractType = if ($WorkerMode -eq 'current') { Get-ProductionType 'LivingNPCs.Dialogue.Engine.LivingNpcMetadataContract' } else { $null }
$coreMethod = if ($null -ne $contractType) { $contractType.GetMethod('BuildInlineCoreInstructions', $flags) } else { $null }
$sceneMethod = if ($null -ne $contractType) { $contractType.GetMethod('BuildSceneInstructions', $flags) } else { $null }
$core = if ($null -ne $coreMethod) { [string]$coreMethod.Invoke($null, @()) } else { '' }
$assemblerType = Get-ProductionType 'LivingNPCs.Dialogue.Engine.PromptAssembler'
$turnType = Get-ProductionType 'LivingNPCs.Dialogue.ConversationTurn'
$turnConstructor = $turnType.GetConstructor([Type[]]@([string], [bool], [string]))
$rows = [Collections.Generic.List[object]]::new()
foreach ($scenario in $fixture.Scenarios) {
    $snapshot = New-ProductionObject 'LivingNPCs.Dialogue.Engine.GameStateSnapshot'
    $snapshotValues = [ordered]@{
        Year = 2; SeasonIndex = 0; SeasonName = 'spring'; DayOfMonth = 15; TimeOfDay = 1330
        FarmerName = $(if ($scenario.Locale -eq 'zh') { '小刚' } else { 'Xiao Gang' })
        FarmerMoney = 6500; FarmerIsMale = $true; FriendshipPoints = 1000
        LocationName = 'Town'; LocationDisplayName = $(if ($scenario.Locale -eq 'zh') { '鹈鹕镇' } else { 'Pelican Town' })
        NpcIsDatable = $true; NpcIsAdult = $true; HomeLocationName = 'Trailer'
        ScheduleAvailability = 'Available'; CurrentActivity = 'reading a book'; NextScheduleLocation = 'Museum'
        MinutesUntilNextSchedule = 60; KentAbsent = $false
    }
    foreach ($pair in $snapshotValues.GetEnumerator()) { Set-Value $snapshot $pair.Key $pair.Value }
    Set-Value $snapshot 'IsEventActive' $scenario.IsEventActive -Optional
    $turns = [Array]::CreateInstance($turnType, $scenario.Conversation.Count)
    for ($index = 0; $index -lt $scenario.Conversation.Count; $index++) {
        $turn = $scenario.Conversation[$index]
        $turns.SetValue($turnConstructor.Invoke([object[]]@([string]$turn.Text, [bool]$turn.IsPlayer, [string]$turn.Id)), $index)
    }
    $request = New-ProductionObject 'LivingNPCs.Dialogue.GenerationRequest'
    foreach ($pair in ([ordered]@{
        NpcName = 'Penny'; NpcDisplayName = $scenario.NpcDisplayName; Trigger = 'Conversation'
        CurrentPlayerText = $scenario.PlayerText; BehaviorContext = $scenario.BehaviorContext; Snapshot = $snapshot; Conversation = $turns
    }).GetEnumerator()) { Set-Value $request $pair.Key $pair.Value }
    $bioJson = if ($scenario.Locale -eq 'zh') { $fixture.Assets.ChineseBio } else { $fixture.Assets.EnglishBio }
    $bio = [Newtonsoft.Json.JsonConvert]::DeserializeObject([string]$bioJson, (Get-ProductionType 'LivingNPCs.Dialogue.Content.NpcBio'))
    $bio.GetType().GetMethod('NormalizeNullFields').Invoke($bio, @()) | Out-Null
    $routing = (Get-ProductionType 'LivingNPCs.Dialogue.Engine.ContextRoutingPlan').GetMethod('Full', $flags).Invoke($null, @())
    $inputObject = New-ProductionObject 'LivingNPCs.Dialogue.Engine.PromptAssemblyInput'
    foreach ($pair in ([ordered]@{
        Request = $request; Bio = $bio; Plan = $routing; NpcName = 'Penny'; NpcDisplayName = $scenario.NpcDisplayName
        NpcGender = 'Female'; Conversation = $turns; Locale = $scenario.Locale; UseOptimizedPrompts = $false
        WorldSummaryFull = $fixture.RenderedWorld[$scenario.Locale]; WorldSummaryBrief = $fixture.RenderedWorld[$scenario.Locale]
        Lookup = New-PromptLookup (Get-PromptMap $fixture.Assets $scenario.Locale)
    }).GetEnumerator()) { Set-Value $inputObject $pair.Key $pair.Value }
    $assembler = $assemblerType.GetConstructors()[0].Invoke([object[]]@($inputObject))
    $prompt = $assemblerType.GetMethod('Assemble').Invoke($assembler, @())
    $segments = [ordered]@{}
    $segmentLengths = [ordered]@{}
    $sum = 0
    foreach ($name in @('System', 'GameConstantContext', 'NpcConstantContext', 'CorePrompt', 'Instructions', 'Command', 'ResponseStart')) {
        $text = [string]$prompt.GetType().GetProperty($name).GetValue($prompt)
        $segments[$name] = $text
        $segmentLengths[$name] = $text.Length
        if ($name -ne 'ResponseStart') { $sum += $text.Length }
    }
    $total = [int]$prompt.GetType().GetProperty('TotalCharacters').GetValue($prompt)
    if ($sum -ne $total) { throw "TotalCharacters mismatch for $($scenario.Key): $sum vs $total" }
    if ($segmentLengths.System -eq 0 -or $segmentLengths.Command -eq 0) {
        throw "Repository prompt lookup returned an empty required section for $($scenario.Key)."
    }
    $scene = ''
    if ($WorkerMode -eq 'current') {
        $planObject = $prompt.GetType().GetProperty('ActionContract').GetValue($prompt)
        $plan = Get-PlanValues $planObject
        $scene = [string]$sceneMethod.Invoke($null, [object[]]@($planObject))
        $contractCharacters = $core.Length + $scene.Length
    } else {
        $plan = [ordered]@{ IncludeTravel = $true; IncludeGifts = $true; IncludeNewHelp = $true; IncludeHelpUpdates = $true; IncludeMoney = $true; IncludeFestival = $true; IsFallback = $false; Reason = 'baseline-unconditional-full' }
        $contractCharacters = $fullInline.Length
    }
    $sectionLengths = [ordered]@{}
    foreach ($pair in $prompt.GetType().GetProperty('SectionLengths').GetValue($prompt).GetEnumerator()) { $sectionLengths[$pair.Key] = $pair.Value }
    $rows.Add([ordered]@{
        Key = $scenario.Key; Locale = $scenario.Locale; Scenario = $scenario.Id; Plan = $plan; PlanLabel = Get-PlanLabel $plan
        ContractCharacters = $contractCharacters; FullCompatibilityContractCharacters = $fullInline.Length
        CoreContractCharacters = $core.Length; SceneContractCharacters = $scene.Length
        TotalCharacters = $total; ResponseStartCharacters = $segmentLengths.ResponseStart
        SegmentLengths = $segmentLengths; SectionLengths = $sectionLengths; Segments = $segments
    })
}
$combinations = [Collections.Generic.List[object]]::new()
if ($WorkerMode -eq 'current') {
    $planType = Get-ProductionType 'LivingNPCs.Dialogue.Engine.SceneActionContractPlan'
    $fullPlan = $planType.GetMethod('Full', $flags).Invoke($null, [object[]]@('measurement-full-fallback'))
    $fallbackScene = [string]$sceneMethod.Invoke($null, [object[]]@($fullPlan))
    for ($mask = 0; $mask -lt 64; $mask++) {
        $planObject = [Activator]::CreateInstance($planType, $true)
        for ($bit = 0; $bit -lt $familyNames.Count; $bit++) { Set-Value $planObject $familyNames[$bit] ([bool]($mask -band (1 -shl $bit))) }
        $scene = [string]$sceneMethod.Invoke($null, [object[]]@($planObject))
        if ($mask -eq 63 -and $scene -cne $fallbackScene) { throw 'All-family combination differs from full fallback.' }
        $combinations.Add([ordered]@{
            Mask = $mask; Plan = Get-PlanLabel (Get-PlanValues $planObject)
            CoreChars = $core.Length; SceneChars = $scene.Length; CombinedChars = $core.Length + $scene.Length
            CurrentCompatibilityFullChars = $fullInline.Length; SameAsFullFallback = $mask -eq 63
        })
    }
}
Write-JsonFile (Join-Path $OutputDirectory ($WorkerMode + '.json')) ([ordered]@{
    Assembly = [ordered]@{ Path = $assemblyPath; Sha256 = Get-Hash $assemblyPath; Mvid = $assembly.ManifestModule.ModuleVersionId; Runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription }
    FixtureSha256 = Get-Hash $fixturePath; AssetSources = $fixture.AssetSources; Scope = $fixture.Scope
    Scenarios = $rows; FamilyCombinations = $combinations
})
[AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
