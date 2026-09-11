#requires -Version 7.2
<#
.SYNOPSIS
Compare real memory selection and assembled dialogue prompts from two built production DLLs.
.DESCRIPTION
Runs each version in a separate PowerShell process with identical synthetic states and assets.
The current worker uses production MergeLocalizedPrompts and WorldEntryIndex once, then both
workers consume the same captured Chinese prompts, biography and retrieved world entries.
No game is started, no builds or deployments are performed, and no provider, configuration,
credentials, saves or player logs are read. The script does not make network requests.
Raw synthetic fixtures and prompts stay in OutputDirectory (a new Temp directory by default).
comparison.json contains only fixture IDs, counts, hashes, checks and character measurements.
live-fixtures.json is optional input for benchmark_dialogue_latency.py, with 6 or 8 requests;
running that separate tool requires explicit live-call authorization and --timeout 85.
.EXAMPLE
./tools/measure_memory_context.ps1 -BaselineRepositoryRoot C:/Temp/baseline -CurrentRepositoryRoot C:/Temp/current
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaselineRepositoryRoot,
    [string] $CurrentRepositoryRoot = '',
    [string] $AssetRepositoryRoot = '',
    [string] $BaselineAssemblyPath = '',
    [string] $CurrentAssemblyPath = '',
    [string] $OutputDirectory = '',
    [ValidateSet(6, 8)] [int] $LiveRequestLimit = 8,
    [Parameter(DontShow)] [ValidateSet('driver', 'current', 'baseline')] [string] $WorkerMode = 'driver'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
if ([string]::IsNullOrWhiteSpace($CurrentRepositoryRoot)) { $CurrentRepositoryRoot = Split-Path $PSScriptRoot -Parent }
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
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('LivingNPCs-memory-context-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$fixturePath = Join-Path $OutputDirectory 'fixture.json'
$storeNames = @('LongTermMemories', 'PlayerPreferenceMemories', 'CommunityImpressions', 'SharedExperiences', 'DialogueBehaviorInfluences', 'HelpRequests', 'Conflicts')

function Write-JsonFile([string] $Path, [object] $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 60), [Text.UTF8Encoding]::new($false))
}
function Get-FileSha([string] $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Get-TextSha([string] $Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text)))
}
function Invoke-IsolatedWorker([string] $Mode) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = Join-Path $PSHOME 'pwsh.exe'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $PSCommandPath,
        '-WorkerMode', $Mode, '-BaselineRepositoryRoot', $BaselineRepositoryRoot,
        '-CurrentRepositoryRoot', $CurrentRepositoryRoot, '-AssetRepositoryRoot', $AssetRepositoryRoot,
        '-BaselineAssemblyPath', $BaselineAssemblyPath, '-CurrentAssemblyPath', $CurrentAssemblyPath,
        '-OutputDirectory', $OutputDirectory, '-LiveRequestLimit', [string]$LiveRequestLimit)) {
        $startInfo.ArgumentList.Add($argument)
    }
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
    if ($current.FixtureSha256 -cne $baseline.FixtureSha256) { throw 'Versions did not consume identical fixture bytes.' }
    $beforeById = @{}
    $afterById = @{}
    foreach ($row in $baseline.Scenarios) { $beforeById[$row.Id] = $row }
    foreach ($row in $current.Scenarios) { $afterById[$row.Id] = $row }
    $comparisons = @($current.Scenarios | ForEach-Object {
        $before = $beforeById[$_.Id]
        [ordered]@{
            Scenario = $_.Id; InputCounts = $_.InputCounts
            BaselineBehaviorChars = $before.BehaviorCharacters; CurrentBehaviorChars = $_.BehaviorCharacters
            BehaviorSavedChars = $before.BehaviorCharacters - $_.BehaviorCharacters
            BaselineTotalChars = $before.TotalCharacters; CurrentTotalChars = $_.TotalCharacters
            TotalSavedChars = $before.TotalCharacters - $_.TotalCharacters
            TotalSavedPercent = [Math]::Round(100.0 * ($before.TotalCharacters - $_.TotalCharacters) / $before.TotalCharacters, 2)
            OutsideBehaviorLengthDelta = ($before.TotalCharacters - $before.BehaviorCharacters) - ($_.TotalCharacters - $_.BehaviorCharacters)
            SystemEqual = $before.Segments.System -ceq $_.Segments.System
            GameContextEqual = $before.Segments.GameConstantContext -ceq $_.Segments.GameConstantContext
            NpcAndInstructionsEqual = $before.CacheableNpcContext -ceq $_.CacheableNpcContext
            BaselineSegmentLengths = $before.SegmentLengths; CurrentSegmentLengths = $_.SegmentLengths
            BaselineSectionLengths = $before.SectionLengths; CurrentSectionLengths = $_.SectionLengths
            BaselineSelection = $before.Selection; CurrentSelection = $_.Selection
            BaselineChecks = $before.Checks; CurrentChecks = $_.Checks
            SharedWorld = $_.WorldMeasurement
            BaselinePayloadSha256 = $before.PayloadSha256; CurrentPayloadSha256 = $_.PayloadSha256
        }
    })
    $liveIds = @('saturated-greeting', 'older-experience', 'cross-language-preference', 'active-constraints') | Select-Object -First ($LiveRequestLimit / 2)
    $live = [Collections.Generic.List[object]]::new()
    for ($pair = 0; $pair -lt $liveIds.Count; $pair++) {
        $id = $liveIds[$pair]
        $order = if ($pair % 2 -eq 0) { @('baseline', 'current') } else { @('current', 'baseline') }
        foreach ($version in $order) {
            $row = if ($version -eq 'baseline') { $beforeById[$id] } else { $afterById[$id] }
            $live.Add([ordered]@{
                name = "$version-$id"; effort = 'low'; system = $row.Segments.System
                user = $row.ProviderUser; stream = $true; max_tokens = 16000
            })
        }
    }
    Write-JsonFile (Join-Path $OutputDirectory 'live-fixtures.json') $live
    $historyBefore = @{}
    foreach ($row in $baseline.HistorySampling) { $historyBefore[$row.Id] = $row }
    $historyComparisons = @($current.HistorySampling | ForEach-Object {
        [ordered]@{ Scenario = $_.Id; Baseline = $historyBefore[$_.Id]; Current = $_ }
    })
    $sameAssembly = $baseline.Assembly.Sha256 -ceq $current.Assembly.Sha256
    Write-JsonFile (Join-Path $OutputDirectory 'comparison.json') ([ordered]@{
        Unit = 'UTF-16 code units (.NET String.Length), including CR/LF; not tokens or latency'
        ScriptSha256 = Get-FileSha $PSCommandPath
        BaselineAssembly = $baseline.Assembly; CurrentAssembly = $current.Assembly
        IdenticalAssemblies = $sameAssembly; FixtureSha256 = $current.FixtureSha256
        AssetSources = $current.AssetSources; Scope = $current.Scope
        RecallBudgets = @{ LongTerm = 3; Preferences = 4; Community = 2 }
        BaselineCapabilities = $baseline.Capabilities; CurrentCapabilities = $current.Capabilities
        Comparisons = $comparisons; HistorySampling = $historyComparisons
        LiveRun = @{
            RequestCount = $live.Count; TimeoutSeconds = 85; Effort = 'low'; Stream = $true; MaxTokens = 16000
            ThinkingType = 'omitted; caller may add the explicitly authorized provider setting'
            PairOrderAlternates = $true; ResponseStartIncluded = $false
            FixtureSha256 = Get-FileSha (Join-Path $OutputDirectory 'live-fixtures.json')
            NetworkCallsMadeByThisTool = 0
        }
    })
    [pscustomobject]@{
        Status = $(if ($sameAssembly) { 'Self-comparison smoke completed; not before/after evidence' } else { 'Offline comparison completed' })
        Scenarios = $comparisons.Count; LiveFixtures = $live.Count
        OutputDirectory = $OutputDirectory
        BaselineSha256 = $baseline.Assembly.Sha256; CurrentSha256 = $current.Assembly.Sha256
    } | ConvertTo-Json
    exit 0
}

# Byte-load one production version per process; do not lock build outputs or load test code.
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
$assemblyBytes = [IO.File]::ReadAllBytes($assemblyPath)
$loadedAssemblySha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($assemblyBytes))
$assembly = [Reflection.Assembly]::Load($assemblyBytes)

function Get-ProductionType([string] $Name) { return $assembly.GetType($Name, $true) }
function New-ProductionObject([string] $Name) { return [Activator]::CreateInstance((Get-ProductionType $Name), $true) }
function New-Model([string] $Name, [object] $Data) {
    $json = ConvertTo-Json -InputObject $Data -Depth 60 -Compress
    return [Newtonsoft.Json.JsonConvert]::DeserializeObject($json, (Get-ProductionType $Name))
}
function Set-Value([object] $Object, [string] $Name, [object] $Value) {
    # NPC exposes both speed and Speed; avoid PowerShell's case-insensitive member adapter.
    $objectType = [object].GetMethod('GetType').Invoke($Object, @())
    $property = $objectType.GetProperty($Name, $flags)
    if ($null -ne $property) {
        if ($null -eq $Value) { $property.SetValue($Object, $null) }
        else { $property.SetValue($Object, [Management.Automation.LanguagePrimitives]::ConvertTo($Value, $property.PropertyType)) }
        return
    }
    $field = $objectType.GetField($Name, $flags)
    if ($null -eq $field) { throw "Missing member $($objectType.FullName).$Name" }
    $field.SetValue($Object, [Management.Automation.LanguagePrimitives]::ConvertTo($Value, $field.FieldType))
}
function Invoke-Named([Reflection.MethodInfo] $Method, [Collections.IDictionary] $Values, [object] $Target = $null) {
    $parameters = $Method.GetParameters()
    $arguments = [object[]]::new($parameters.Length)
    for ($index = 0; $index -lt $parameters.Length; $index++) {
        $parameter = $parameters[$index]
        if ($Values.Contains($parameter.Name)) {
            $value = $Values[$parameter.Name]
            if ($null -eq $value) { $arguments[$index] = $null }
            else { $arguments.SetValue([Management.Automation.LanguagePrimitives]::ConvertTo($value, $parameter.ParameterType), $index) }
        }
        elseif ($parameter.HasDefaultValue) { $arguments[$index] = $parameter.DefaultValue }
        else { throw "No fixture argument for $($Method.DeclaringType.Name).$($Method.Name): $($parameter.Name)" }
    }
    # Preserve even singleton IReadOnlyList<T> results for later reflection calls.
    return ,$Method.Invoke($Target, $arguments)
}
function ConvertTo-PromptMap([Collections.IDictionary] $Data) {
    $map = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($pair in $Data.GetEnumerator()) { $map[$pair.Key] = [string]$pair.Value }
    return ,$map
}
function New-PromptLookup([Collections.Generic.Dictionary[string,string]] $Map, [object] $Bio) {
    $replaceTokens = (Get-ProductionType 'LivingNPCs.Dialogue.Content.PromptTable').GetMethod('ReplaceTokens', $flags)
    $lookupBlock = {
        param([string] $key, [object] $tokens, [bool] $optimized)
        # Production uses <key>Optimized, then the ordinary key. This fixture selects ordinary prompts.
        $baseKeys = if ($optimized) { @(($key + 'Optimized'), $key) } else { @($key) }
        foreach ($baseKey in $baseKeys) {
            if ($Bio.PromptOverrides.ContainsKey($baseKey) -and -not [string]::IsNullOrWhiteSpace($Bio.PromptOverrides[$baseKey])) {
                return [string]$replaceTokens.Invoke($null, [object[]]@($Bio.PromptOverrides[$baseKey], $tokens))
            }
            foreach ($candidate in @(($baseKey + '.FemaleNpc'), $baseKey)) {
                if ($Map.ContainsKey($candidate)) { return [string]$replaceTokens.Invoke($null, [object[]]@($Map[$candidate], $tokens)) }
            }
        }
        return $null
    }.GetNewClosure()
    return [Management.Automation.LanguagePrimitives]::ConvertTo($lookupBlock, (Get-ProductionType 'LivingNPCs.Dialogue.Engine.PromptTextLookup'))
}

function New-SyntheticState([string] $Kind) {
    $state = [ordered]@{
        NpcName = 'Penny'; RelationshipTrust = 80; Familiarity = 45; InteractionComfortTier = 'Trusted'
        CurrentEmotion = 'Calm'; LastFriendshipHearts = 6; InteractionRhythm = 'DailyRoutine'
        ConversationsToday = 1; ConsecutiveConversationDays = 3; LastInteraction = 'a brief greeting'
    }
    foreach ($name in $storeNames) { $state[$name] = [Collections.Generic.List[object]]::new() }
    if ($Kind -eq 'sparse') { return $state }
    $objects = @('striped scarf', 'red boots', 'linen shirt', 'copper buckle', 'woolen mittens', 'felt hat', 'leather belt', 'cotton socks')
    $people = @('Haley', 'Leah', 'Elliott', 'Emily', 'Pam', 'Alex', 'Robin', 'Clint')
    for ($index = 0; $index -lt 8; $index++) {
        $item = $objects[$index]
        $updated = 98 - $index
        $state.LongTermMemories.Add(@{
            AuditId = "personal-$index"; Kind = 'fact'; Subject = $item
            Summary = "The farmer keeps a $item in a separate wardrobe drawer."
            Importance = 95 - $index; CreatedTotalDays = 80; LastUpdatedTotalDays = $updated
            CreatedTimeOfDay = 900; LastUpdatedTimeOfDay = 900; TimesReinforced = 1; Tags = @('clothing')
        })
        $state.PlayerPreferenceMemories.Add(@{
            AuditId = "preference-$index"; PreferenceKind = 'liked_item'; Subject = $item
            Summary = "The farmer prefers the plain $item over the patterned version."
            Importance = 95 - $index; CreatedTotalDays = 80; LastUpdatedTotalDays = $updated
            CreatedTimeOfDay = 900; LastUpdatedTimeOfDay = 900; TimesReinforced = 1; Tags = @('clothing')
        })
        $state.CommunityImpressions.Add(@{
            AuditId = "community-$index"; SubjectNpcName = $people[$index]; SubjectDisplayName = $people[$index]
            Summary = "$($people[$index]) saw the farmer mend a $item."
            Source = 'CloseCircle'; Visibility = 'Personal'; Confidence = 75; Importance = 95 - $index
            TransmissionDepth = 1; HeardFromNpcName = 'Pam'; CircleKey = 'family'
            CreatedTotalDays = 98; LastUpdatedTotalDays = 99; ExpiresTotalDays = 105; TimesReinforced = 1
        })
        $state.SharedExperiences.Add(@{
            AuditId = "experience-$index"; Key = "wardrobe-$index"; Type = 'conversation'
            Summary = "Penny and the farmer compared the stitching on a $item."
            LocationName = 'Trailer'; LocationLabel = 'Trailer'; Importance = 90 - $index
            CreatedTotalDays = 80; LastUpdatedTotalDays = $updated; TimesReinforced = 1
            FollowUpEligibleTotalDays = -1; FollowUpShownTotalDays = 99
        })
        $state.DialogueBehaviorInfluences.Add(@{
            AuditId = "past-influence-$index"; Type = 'visit_location'; Summary = "Finished checking a $item at home."
            TargetLocation = 'Trailer'; TargetLocationLabel = 'Trailer'; Status = 'Expired'; Intensity = 55
            CreatedTotalDays = 80; LastUpdatedTotalDays = $updated; ExpiresTotalDays = 99; TimesReinforced = 1
        })
        $state.HelpRequests.Add(@{
            AuditId = "past-help-$index"; Type = 'item_request'; Summary = "Delivered wood to repair the drawer for a $item."
            RequestedItemId = '(O)388'; RequestedItemLabel = 'Wood'; Status = 'Fulfilled'; Resolution = 'delivered'
            CreatedTotalDays = 70; LastUpdatedTotalDays = $updated; DueTotalDays = 80 + $index
            FulfilledTotalDays = 80 + $index; LastMentionedTotalDays = 99; FollowUpPotential = 'none'; TimesReinforced = 1
        })
        $state.Conflicts.Add(@{
            AuditId = "past-conflict-$index"; CauseKind = 'dialogue'; Summary = "Settled a misunderstanding about the misplaced $item."
            Status = 'Resolved'; Severity = 0; PeakSeverity = 20; RepairStage = 'Resolved'
            CreatedTotalDays = 70; LastUpdatedTotalDays = $updated; ResolvedTotalDays = 80 + $index
            RecoveryMentionedTotalDays = 99; TimesReinforced = 1
        })
    }
    $state.LongTermMemories.Add(@{
        AuditId = 'personal-old-atlas'; Kind = 'fact'; Subject = 'library atlas'
        Summary = 'The farmer and Penny once catalogued a borrowed atlas at the library.'
        Importance = 25; CreatedTotalDays = 30; LastUpdatedTotalDays = 30; TimesReinforced = 1; Tags = @('reading')
    })
    $state.PlayerPreferenceMemories.Add(@{
        AuditId = 'preference-quiet'; PreferenceKind = 'value'; Subject = 'quiet settings'
        Summary = 'The farmer prefers quiet evenings and avoids crowds.'
        Importance = 25; CreatedTotalDays = 30; LastUpdatedTotalDays = 30; TimesReinforced = 1; Tags = @()
    })
    $state.SharedExperiences.Add(@{
        AuditId = 'experience-old-atlas'; Key = 'old-library-atlas'; Type = 'conversation'
        Summary = '潘妮和玩家曾在图书馆一起整理地图集，随后把借来的航海图归还给馆员。'
        LocationName = 'Library'; LocationLabel = 'Library'; Importance = 20
        CreatedTotalDays = 30; LastUpdatedTotalDays = 30; FollowUpEligibleTotalDays = -1; TimesReinforced = 1
    })
    $state.HelpRequests.Add(@{
        AuditId = 'help-old-atlas'; Type = 'item_request'; Summary = '为图书馆找回并归还了地图集缺失的页面。'
        RequestedItemId = '(O)102'; RequestedItemLabel = 'Lost Book'; Status = 'Fulfilled'; Resolution = 'returned'
        CreatedTotalDays = 20; LastUpdatedTotalDays = 30; DueTotalDays = 90; FulfilledTotalDays = 30
        LastMentionedTotalDays = 31; FollowUpPotential = 'none'; TimesReinforced = 1
    })
    $state.Conflicts.Add(@{
        AuditId = 'conflict-old-atlas'; CauseKind = 'dialogue'; Summary = '图书馆整理地图集时曾有分歧，互相解释后已经解决。'
        Status = 'Resolved'; Severity = 0; PeakSeverity = 10; RepairStage = 'Resolved'
        CreatedTotalDays = 20; LastUpdatedTotalDays = 30; ResolvedTotalDays = 30; RecoveryMentionedTotalDays = 31; TimesReinforced = 1
    })
    if ($Kind -eq 'active') {
        for ($index = 0; $index -lt 6; $index++) {
            $state.DialogueBehaviorInfluences.Add(@{
                AuditId = "active-influence-$index"; Required = $true; Type = 'visit_location'
                Summary = "待办约束$($index + 1)：稍后检查教室第$($index + 1)张书桌的情况。"
                TargetLocation = 'Library'; TargetLocationLabel = 'Library'; Status = 'Active'; Intensity = 65 - $index
                CreatedTotalDays = 99; LastUpdatedTotalDays = 100; ExpiresTotalDays = 103; MaxTriggers = 1; TimesReinforced = 1
            })
            $state.Conflicts.Add(@{
                AuditId = "active-conflict-$index"; Required = $true; CauseKind = 'dialogue'
                Summary = "尚未解决的边界$($index + 1)：关于教室第$($index + 1)条约定的误会仍需要明确解释。"
                Status = $(if ($index % 2 -eq 0) { 'Active' } else { 'Recovering' }); Severity = 80 - $index
                PeakSeverity = 80; RepairStage = 'NeedsApology'; RequiresComplexRepair = $true
                CreatedTotalDays = 99; LastUpdatedTotalDays = 100; MinimumRepairTotalDays = 103; TimesReinforced = 1
            })
        }
        $state.HelpRequests.Add(@{
            AuditId = 'unclaimed-reward'; Required = $true; Type = 'item_request'
            Summary = '修好教室书柜的委托已经完成，约定的120金币谢礼尚未领取。'
            RequestedItemId = '(O)388'; RequestedItemLabel = 'Wood'; Status = 'Fulfilled'; Resolution = 'delivered'
            CreatedTotalDays = 80; LastUpdatedTotalDays = 94; DueTotalDays = 95; FulfilledTotalDays = 94
            LastMentionedTotalDays = 95; RewardMoney = 120; RewardMoneyClaimQueued = $true; RewardMoneyGranted = $false
            FollowUpPotential = 'none'; TimesReinforced = 1
        })
    }
    return $state
}

function New-HistoryFixtures {
    $records = [Collections.Generic.List[object]]::new()
    for ($age = 1; $age -le 25; $age++) {
        $records.Add(@{
            Item1 = @{
                year = 2; season = $(if ($age -lt 15) { 'Summer' } else { 'Spring' })
                dayOfMonth = $(if ($age -lt 15) { 15 - $age } else { 43 - $age }); timeOfDay = 1200
            }
            Item2 = @{ ConversationElements = @(
                @{ Id = "short-$age-player"; Text = "这是第$age 次归档对话，蓝色围巾已经归还。"; IsPlayerLine = $true },
                @{ Id = "short-$age-npc"; Text = '我已经把围巾放回衣柜了。'; IsPlayerLine = $false }
            ) }
        })
    }
    $target = 'We drank coffee together while examining that quartz crystal.'
    $records.Add(@{
        Item1 = @{ year = 2; season = 'Spring'; dayOfMonth = 3; timeOfDay = 1200 }
        Item2 = @{ ConversationElements = @(
            @{ Id = 'old-topic-player'; Text = $target; IsPlayerLine = $true },
            @{ Id = 'old-topic-npc'; Text = 'You kept the quartz on the desk and asked for coffee without sugar.'; IsPlayerLine = $false }
        ) }
    })
    $ordinary = @{ schemaVersion = 1; npcName = 'Penny'; ConversationHistory = $records.ToArray() }
    $oversized = @{ schemaVersion = 1; npcName = 'Penny'; ConversationHistory = @($records.ToArray()) + @(@{
        Item1 = @{ year = 2; season = 'Summer'; dayOfMonth = 15; timeOfDay = 1100 }
        Item2 = @{ ConversationElements = @(@{
            Id = 'oversized-newest'; Text = ('This is a long synthetic transcript. ' * 150); IsPlayerLine = $false
        }) }
    }) }
    return @{
        Now = @{ year = 2; season = 'Summer'; dayOfMonth = 15; timeOfDay = 1200 }
        Query = '还记得我们喝咖啡、看石英那次吗？'; TargetText = $target; TargetAgeDays = 40
        Cases = @(@{ Id = 'old-topic'; History = $ordinary }, @{ Id = 'oversized-newest'; History = $oversized })
    }
}

if ($WorkerMode -eq 'current') {
    $relativeAssets = [ordered]@{
        DefaultPrompts = 'prompts/default.json'; ZhPrompts = 'prompts/zh.json'
        ChineseBio = 'bios-zh/Penny.json'; World = 'world/GameSummary.json'
    }
    $assets = [ordered]@{}
    $sources = [Collections.Generic.List[object]]::new()
    foreach ($pair in $relativeAssets.GetEnumerator()) {
        $path = Join-Path (Join-Path $AssetRepositoryRoot 'LivingNPCs/assets/dialogue') $pair.Value
        $raw = [IO.File]::ReadAllText($path)
        $assets[$pair.Key] = if ($pair.Key -in @('DefaultPrompts', 'ZhPrompts')) { ConvertFrom-Json -InputObject $raw -AsHashtable } else { $raw }
        $sources.Add([ordered]@{ Asset = $pair.Value; Sha256 = Get-FileSha $path; Characters = $raw.Length })
    }
    $merged = ConvertTo-PromptMap $assets.DefaultPrompts
    $localized = ConvertTo-PromptMap $assets.ZhPrompts
    $mergeMethod = (Get-ProductionType 'LivingNPCs.Dialogue.Content.DialogueContentSetup').GetMethod('MergeLocalizedPrompts', $flags)
    if ($null -eq $mergeMethod) { throw 'Current production assembly has no MergeLocalizedPrompts method.' }
    [void]$mergeMethod.Invoke($null, [object[]]@($merged, $localized))
    $assets.MergedZhPrompts = $merged
    $worldLookup = { param([string] $key); if ($merged.ContainsKey($key)) { return $merged[$key] }; return '' }.GetNewClosure()
    $rendererType = Get-ProductionType 'LivingNPCs.Dialogue.Content.WorldSummaryRenderer'
    $renderer = $rendererType.GetConstructors()[0].Invoke([object[]]@([Func[string,string]]$worldLookup, $null))
    $worldSummary = [Newtonsoft.Json.JsonConvert]::DeserializeObject([string]$assets.World, (Get-ProductionType 'LivingNPCs.Dialogue.Content.WorldSummary'))
    $indexType = Get-ProductionType 'LivingNPCs.Dialogue.Content.WorldEntryIndex'
    $worldIndex = $indexType.GetConstructors()[0].Invoke([object[]]@($worldSummary, $renderer))
    $definitions = @(
        @{ Id = 'sparse'; Kind = 'sparse'; PlayerText = '早上好，潘妮。'; ExpectedIds = @() },
        @{ Id = 'saturated-greeting'; Kind = 'saturated'; PlayerText = '早上好，潘妮。'; ExpectedIds = @() },
        @{ Id = 'older-experience'; Kind = 'saturated'; PlayerText = '还记得我们以前在图书馆整理地图集那次吗？'; ExpectedIds = @('experience-old-atlas') },
        @{ Id = 'cross-language-preference'; Kind = 'saturated'; PlayerText = '人多吵得我紧张，想清静一点。'; ExpectedIds = @('preference-quiet') },
        @{ Id = 'active-constraints'; Kind = 'active'; PlayerText = '今天聊聊你现在的想法吧。'; ExpectedIds = @('unclaimed-reward') }
    )
    $scenarios = [Collections.Generic.List[object]]::new()
    foreach ($definition in $definitions) {
        $query = New-Model 'LivingNPCs.Dialogue.Content.WorldRetrievalQuery' @{
            PlayerText = $definition.PlayerText; NpcName = 'Penny'; NpcDisplayName = '潘妮'
            LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'; Season = 'winter'; NearbyNpcNames = @()
        }
        $retrieved = $indexType.GetMethod('Retrieve').Invoke($worldIndex, [object[]]@($query, 8, 6000))
        if ($retrieved.FallbackReason -notin @('', 'NoRelevantEntries')) { throw "World fixture unexpectedly used full fallback: $($retrieved.FallbackReason)" }
        $scenarios.Add([ordered]@{
            Id = $definition.Id; Locale = 'zh'; PlayerText = $definition.PlayerText
            State = New-SyntheticState $definition.Kind; ExpectedIds = $definition.ExpectedIds
            Conversation = @(
                @{ Text = '今天有空聊一会儿吗？'; IsPlayer = $true; Id = 'prior-player' },
                @{ Text = '嗯，我能待一会儿。'; IsPlayer = $false; Id = 'prior-npc' },
                @{ Text = $definition.PlayerText; IsPlayer = $true; Id = 'current-player' }
            )
            HistoryLines = @('昨天 12:00，玩家与潘妮在镇上互相问候，随后各自离开。')
            RecentEntries = @(@{
                NpcName = 'Penny'; Kind = 'Conversation'; Action = 'brief greeting'; Reason = 'the farmer said hello'
                Year = 1; Season = 'winter'; Day = 16; TimeOfDay = 1200; TotalDays = 99
                LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'
            })
            World = @{
                CoreText = [string]$retrieved.CoreText; RetrievedText = [string]$retrieved.RetrievedText
                SelectedEntryCount = [int]$retrieved.SelectedEntryCount; TotalEntryCount = [int]$retrieved.TotalEntryCount
                RetrievalNote = [string]$retrieved.FallbackReason
            }
        })
    }
    Write-JsonFile $fixturePath ([ordered]@{
        Assets = $assets; AssetSources = $sources; Scenarios = $scenarios; HistoryFixtures = New-HistoryFixtures
        Scope = @(
            'Both production assemblies run in independent processes and consume identical fixture.json bytes; identical assembly hashes identify a smoke run only.'
            'Real MemoryRecallService budgets: 3 personal facts, 4 preferences, 2 community impressions. Real BehaviorPromptContextBuilder and PromptAssembler run for every case.'
            'The current production MergeLocalizedPrompts creates one captured Chinese prompt map; inherited English gender variants are removed according to production rules.'
            'WorldEntryIndex retrieves once per case with 8 entries / 6000 characters. Both versions receive the same captured core and entries, never the older full-world prompt.'
            'Penny biography and repository prompt assets are real. States, previous dialogue, history lines and the current player input are synthetic; sample dialogue and runtime portrait matching are not included.'
            'Saturated cases contain at least eight entries per historical store; the low-salience atlas experience is outside the old top four. The cross-language target preference is English and the query is Chinese.'
            'The active stress case has six current behavior influences, six active/recovering conflicts and an unclaimed reward. It may legitimately grow when all constraints are preserved.'
            'Offered/Pending help requests are not sampled: their real builder path enters HelpRequestAdvisor and live WorldContext. This tool does not stub that path or initialize a game; unit tests must cover those obligations.'
            'ConcisePromptContext=false, Full context routing, no optimized prompts. Query and follow-up-mark parameters are detected by name for compatibility; every case has fresh state.'
            'HistoryLines and the full three-turn current conversation are identical in both assembled versions. Separate HistorySampler stats use 25 short transcripts plus one 40-day-old topic transcript, with null/query and an oversized-newest variant; they do not change live fixtures.'
            'Provider fixture order is System plus GameConstantContext + CacheableNpcContext + Tail, with ResponseStart excluded. Live fixtures omit model, credentials and thinking_type.'
            'Character savings are not model token counts or evidence of lower latency. No network calls, builds, deployments, configuration, secrets, saves or real player logs are used.'
        )
    })
}

$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json -AsHashtable
$config = (Get-ProductionType 'LivingNPCs.ModEntry').GetProperty('ActiveConfig', $flags).GetValue($null)
Set-Value $config 'ConcisePromptContext' $false
$builder = (Get-ProductionType 'LivingNPCs.Behavior.BehaviorPromptContextBuilder').GetMethod('BuildPromptContext', $flags)
$recallType = Get-ProductionType 'LivingNPCs.Behavior.MemoryRecallService'
$historicalType = $assembly.GetType('LivingNPCs.Behavior.HistoricalContextRecallPlan', $false)
$assemblerType = Get-ProductionType 'LivingNPCs.Dialogue.Engine.PromptAssembler'
$turnType = Get-ProductionType 'LivingNPCs.Dialogue.ConversationTurn'
$turnConstructor = $turnType.GetConstructor([Type[]]@([string], [bool], [string]))
$map = ConvertTo-PromptMap $fixture.Assets.MergedZhPrompts
$bio = [Newtonsoft.Json.JsonConvert]::DeserializeObject([string]$fixture.Assets.ChineseBio, (Get-ProductionType 'LivingNPCs.Dialogue.Content.NpcBio'))
[void]$bio.GetType().GetMethod('NormalizeNullFields').Invoke($bio, @())

function Get-FactId([Collections.IDictionary] $Scenario, [string] $Store, [object] $Fact) {
    foreach ($entry in $Scenario.State[$Store]) {
        if ([string]$entry.Summary -ceq [string]$Fact.Summary) { return [string]$entry.AuditId }
    }
    throw "Selected fact is not in the synthetic store: $($Scenario.Id)/$Store"
}
function Get-SelectedFacts([Collections.IDictionary] $Scenario, [string] $Store, [object] $Selections) {
    return ,@($Selections | ForEach-Object { [ordered]@{
        Id = Get-FactId $Scenario $Store $_.Memory; Score = [int]$_.Score
    } })
}
$rows = [Collections.Generic.List[object]]::new()
foreach ($scenario in $fixture.Scenarios) {
    $state = New-Model 'LivingNPCs.Behavior.LivingNpcState' $scenario.State
    $world = New-Model 'LivingNPCs.Behavior.WorldContextSnapshot' @{
        LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'; Season = 'winter'; DayOfMonth = 17
        TimeOfDay = 1200; FriendshipHearts = 6; NearbyNpcNames = @(); PromptLabel = 'quiet afternoon in Pelican Town'
        DebugLabel = 'synthetic town'; Reason = 'synthetic scene'; ApproachModifier = 0; EmoteModifier = 0
        StateInfluence = @{ Mood = ''; Inclination = ''; Priority = 0; AttentionDelta = 0; OpennessDelta = 0; Reason = ''; DebugLabel = '' }
        Progression = @{
            Year = 1; Route = 'undecided'; ResidentStage = 'first_year_settling_in'
            ProfessionFocuses = @(); FarmScale = 'small'; SpouseNames = @()
            ModProgression = @{ Sve = @{ Installed = $false }; PromptLabel = 'no installed expansion progression'; DebugLabel = '无扩展进度'; ReplyGuidance = 'no expansion guidance' }
            PromptLabel = 'ordinary first-year town progression'; DebugLabel = '普通第一年进度'; ReplyGuidance = 'keep requests modest'
        }
        ProgressionKnowledge = @{
            ObservationDomains = @(); LearnedDomains = @(); AttitudeTraits = @(); KnownProfessionFocuses = @()
            ModProgression = @{ PromptLabel = 'no expansion knowledge'; DebugLabel = '无扩展认知'; ReplyGuidance = 'no expansion guidance' }
            KnowsFarmScale = $false; TrustedRelationship = $true
            PromptLabel = 'knows ordinary early town context'; DebugLabel = '知道普通城镇背景'; ReplyGuidance = 'no special guidance'
        }
    }
    $entriesType = (Get-ProductionType 'LivingNPCs.Behavior.BehaviorMemoryEntry').MakeArrayType()
    $recentJson = ConvertTo-Json -InputObject $scenario.RecentEntries -Depth 12 -Compress
    $recent = [Newtonsoft.Json.JsonConvert]::DeserializeObject($recentJson, $entriesType)
    $recall = Invoke-Named ($recallType.GetMethod('BuildPlan', $flags)) @{
        state = $state; world = $world; recentEntries = $recent; longTermCount = 3; preferenceCount = 4
        currentTotalDays = 100; currentPlayerText = $scenario.PlayerText
    }
    $community = Invoke-Named ($recallType.GetMethod('BuildCommunityImpressionPlan', $flags)) @{
        state = $state; maxCount = 2; currentTotalDays = 100; currentPlayerText = $scenario.PlayerText
    }
    $selected = [ordered]@{
        LongTerm = Get-SelectedFacts $scenario 'LongTermMemories' $recall.LongTermMemories
        Preferences = Get-SelectedFacts $scenario 'PlayerPreferenceMemories' $recall.PlayerPreferences
        Community = Get-SelectedFacts $scenario 'CommunityImpressions' $community
    }
    if ($selected.LongTerm.Count -gt 3 -or $selected.Preferences.Count -gt 4 -or $selected.Community.Count -gt 2) { throw 'Recall exceeded configured budgets.' }
    if ($null -ne $historicalType) {
        $historical = Invoke-Named ($historicalType.GetMethod('Build', $flags)) @{
            state = $state; currentTotalDays = 100; currentPlayerText = $scenario.PlayerText
        }
        $selected.Historical = [ordered]@{}
        foreach ($pair in (@{ BehaviorInfluences = 'DialogueBehaviorInfluences'; SharedExperiences = 'SharedExperiences'; HelpRequests = 'HelpRequests'; Conflicts = 'Conflicts' }).GetEnumerator()) {
            $selected.Historical[$pair.Key] = @($historical.($pair.Key) | ForEach-Object { Get-FactId $scenario $pair.Value $_ })
        }
    }
    $npc = [Activator]::CreateInstance($builder.GetParameters()[0].ParameterType, $true)
    Set-Value $npc 'Name' 'Penny'
    Set-Value $npc 'displayName' '潘妮'
    $disposition = New-Model 'LivingNPCs.Behavior.NpcDispositionProfile' @{
        PromptLabel = 'careful temperament'; DebugLabel = 'synthetic'; ApproachModifier = 0; EmoteModifier = 0
        PassiveEmoteId = 16; Reason = 'synthetic profile'; SourceLabel = 'synthetic profile'; SourceDebugLabel = '合成资料'
        BackgroundPrompt = 'a thoughtful tutor who cares about her neighbors'; DialoguePrompt = 'gentle phrasing'
    }
    $expression = New-Model 'LivingNPCs.Behavior.EmotionalExpressionCue' @{
        Key = 'synthetic'; PromptLabel = 'soft-spoken style'; DebugLabel = 'synthetic'
        ConflictPromptLabel = 'state boundaries gently'; RepairPromptLabel = 'look for a specific repair'
        ReplyGuidance = 'speak gently and concretely'; Directness = 50; MemoryHold = 50
        EmotionDecayMultiplier = 1; ConflictDecayMultiplier = 1; RepairResponsivenessMultiplier = 1; ComplexRepairDelayAdjustmentDays = 0
    }
    $behavior = [string](Invoke-Named $builder @{
        npc = $npc; recentEntries = $recent; state = $state; world = $world; disposition = $disposition
        emotionalStyle = $expression; recallPlan = $recall; communityImpressions = $community
        maxPendingHelpRequestsPerNpc = 0; helpRequestCooldownDays = 3; currentTotalDays = 100; currentTimeOfDay = 1200
        currentPlayerText = $scenario.PlayerText; markFollowUpCues = $false
    })
    $counts = [ordered]@{}
    $occurrences = [ordered]@{}
    $required = [Collections.Generic.List[string]]::new()
    foreach ($store in $storeNames) {
        $counts[$store] = $scenario.State[$store].Count
        foreach ($entry in $scenario.State[$store]) {
            $occurrences[$entry.AuditId] = [regex]::Matches($behavior, [regex]::Escape([string]$entry.Summary)).Count
            if ($entry.Contains('Required') -and $entry.Required) { $required.Add([string]$entry.AuditId) }
        }
    }
    $missingRequired = @($required | Where-Object { $occurrences[$_] -eq 0 })
    $checks = [ordered]@{
        ExpectedIds = $scenario.ExpectedIds
        ExpectedFound = @($scenario.ExpectedIds | Where-Object { $occurrences[$_] -gt 0 })
        ExpectedMissing = @($scenario.ExpectedIds | Where-Object { $occurrences[$_] -eq 0 })
        RequiredIds = $required; RequiredCount = $required.Count; RequiredMissing = $missingRequired
        AllRequiredPreserved = $missingRequired.Count -eq 0; Occurrences = $occurrences
    }
    $snapshot = New-Model 'LivingNPCs.Dialogue.Engine.GameStateSnapshot' @{
        Year = 1; SeasonIndex = 3; SeasonName = 'winter'; DayOfMonth = 17; TimeOfDay = 1200
        FarmerName = '小林'; FarmerMoney = 6500; FarmerIsMale = $true; FriendshipPoints = 1500
        LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'; NpcIsDatable = $true; NpcIsAdult = $true
        HomeLocationName = 'Trailer'; ScheduleAvailability = 'Available'; CurrentActivity = 'taking a short break'
        NextScheduleLocation = 'Museum'; MinutesUntilNextSchedule = 60; KentAbsent = $true
    }
    $turns = [Array]::CreateInstance($turnType, $scenario.Conversation.Count)
    for ($index = 0; $index -lt $scenario.Conversation.Count; $index++) {
        $turn = $scenario.Conversation[$index]
        $turns.SetValue($turnConstructor.Invoke([object[]]@([string]$turn.Text, [bool]$turn.IsPlayer, [string]$turn.Id)), $index)
    }
    $request = New-ProductionObject 'LivingNPCs.Dialogue.GenerationRequest'
    foreach ($pair in (@{
        NpcName = 'Penny'; NpcDisplayName = '潘妮'; Trigger = 'Conversation'; CurrentPlayerText = $scenario.PlayerText
        BehaviorContext = $behavior; Snapshot = $snapshot; Conversation = $turns
    }).GetEnumerator()) { Set-Value $request $pair.Key $pair.Value }
    $routing = (Get-ProductionType 'LivingNPCs.Dialogue.Engine.ContextRoutingPlan').GetMethod('Full', $flags).Invoke($null, @())
    $inputObject = New-ProductionObject 'LivingNPCs.Dialogue.Engine.PromptAssemblyInput'
    foreach ($pair in (@{
        Request = $request; Plan = $routing; Bio = $bio; NpcName = 'Penny'; NpcDisplayName = '潘妮'; NpcGender = 'Female'
        Conversation = $turns; HistoryLines = [string[]]$scenario.HistoryLines; Locale = 'zh'; UseOptimizedPrompts = $false
        WorldSummaryFull = $scenario.World.CoreText; WorldSummaryBrief = $scenario.World.CoreText
        RetrievedWorldContext = $scenario.World.RetrievedText; Lookup = New-PromptLookup $map $bio
    }).GetEnumerator()) { Set-Value $inputObject $pair.Key $pair.Value }
    $assembler = $assemblerType.GetConstructors()[0].Invoke([object[]]@($inputObject))
    $prompt = $assemblerType.GetMethod('Assemble').Invoke($assembler, @())
    $segments = [ordered]@{}
    $lengths = [ordered]@{}
    foreach ($name in @('System', 'GameConstantContext', 'NpcConstantContext', 'CorePrompt', 'Instructions', 'Command', 'ResponseStart')) {
        $segments[$name] = [string]$prompt.GetType().GetProperty($name).GetValue($prompt)
        $lengths[$name] = $segments[$name].Length
    }
    $cacheableNpc = [string]$prompt.GetType().GetProperty('CacheableNpcContext').GetValue($prompt)
    $tail = [string]$prompt.GetType().GetProperty('Tail').GetValue($prompt)
    $providerUser = $segments.GameConstantContext + $cacheableNpc + $tail
    $total = [int]$prompt.GetType().GetProperty('TotalCharacters').GetValue($prompt)
    if ($total -ne ($segments.System.Length + $providerUser.Length)) { throw "Assembled/provider length mismatch for $($scenario.Id)." }
    if ($lengths.System -eq 0 -or $lengths.Command -eq 0 -or -not $providerUser.Contains($scenario.PlayerText)) { throw "Missing required prompt sections for $($scenario.Id)." }
    $sectionLengths = [ordered]@{}
    foreach ($pair in $prompt.SectionLengths.GetEnumerator()) { $sectionLengths[$pair.Key] = $pair.Value }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory "$WorkerMode-$($scenario.Id)-behavior.txt"), $behavior, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $OutputDirectory "$WorkerMode-$($scenario.Id)-assembled.txt"), ($segments.System + "`n`n" + $providerUser), [Text.UTF8Encoding]::new($false))
    # Raw-only capture lets response validators replay the actual request contract and state.
    # These objects are deliberately excluded from comparison.json's explicit field whitelist.
    $capturedSnapshot = ConvertFrom-Json -InputObject ([Newtonsoft.Json.JsonConvert]::SerializeObject($snapshot)) -AsHashtable
    $capturedContract = ConvertFrom-Json -InputObject ([Newtonsoft.Json.JsonConvert]::SerializeObject($prompt.ActionContract)) -AsHashtable
    $rows.Add([ordered]@{
        Id = $scenario.Id; InputCounts = $counts; Selection = $selected; Checks = $checks
        Snapshot = $capturedSnapshot; ActionContract = $capturedContract
        BehaviorCharacters = $behavior.Length; BehaviorContext = $behavior; TotalCharacters = $total
        Segments = $segments; SegmentLengths = $lengths; SectionLengths = $sectionLengths
        CacheableNpcContext = $cacheableNpc; ProviderUser = $providerUser
        PayloadSha256 = Get-TextSha ($segments.System + "`n" + $providerUser)
        WorldMeasurement = @{
            CoreCharacters = $scenario.World.CoreText.Length; RetrievedCharacters = $scenario.World.RetrievedText.Length
            SelectedEntries = $scenario.World.SelectedEntryCount; TotalEntries = $scenario.World.TotalEntryCount
            RetrievalNote = $scenario.World.RetrievalNote
            CoreSha256 = Get-TextSha $scenario.World.CoreText; RetrievedSha256 = Get-TextSha $scenario.World.RetrievedText
        }
    })
}
$historySampler = (Get-ProductionType 'LivingNPCs.Dialogue.Engine.HistorySampler').GetMethod('Sample', $flags)
$historyRows = [Collections.Generic.List[object]]::new()
$historyNow = New-Model 'LivingNPCs.Dialogue.StardewTime' $fixture.HistoryFixtures.Now
$historyLookupBlock = {
    param([string] $key)
    foreach ($candidate in @(($key + '.FemaleNpc'), $key)) {
        if ($map.ContainsKey($candidate)) { return $map[$candidate] }
    }
    return $null
}.GetNewClosure()
foreach ($historyCase in $fixture.HistoryFixtures.Cases) {
    foreach ($useQuery in @($false, $true)) {
        $history = New-Model 'LivingNPCs.Dialogue.Persistence.StardewEventHistory' $historyCase.History
        $storedBefore = [Newtonsoft.Json.JsonConvert]::SerializeObject($history)
        $query = if ($useQuery) { [string]$fixture.HistoryFixtures.Query } else { $null }
        $lines = Invoke-Named $historySampler @{
            history = $history; activeDialogueEvents = [Collections.Generic.KeyValuePair[string,int][]]@()
            now = $historyNow; npcDisplayName = '潘妮'; currentConversationId = 'separate-current-conversation'
            getPrompt = [Func[string,string]]$historyLookupBlock; currentPlayerText = $query
        }
        $storedAfter = [Newtonsoft.Json.JsonConvert]::SerializeObject($history)
        $selectedCharacters = 0
        $targetFound = $false
        foreach ($line in $lines) {
            $selectedCharacters += $line.Length
            if ($line.Contains([string]$fixture.HistoryFixtures.TargetText)) { $targetFound = $true }
        }
        if ($lines.Count -gt 20 -or $selectedCharacters -gt 4000) { throw 'HistorySampler exceeded its entry/character budget.' }
        $historyRows.Add([ordered]@{
            Id = $historyCase.Id + $(if ($useQuery) { '-query' } else { '-null' })
            InputRecordCount = $historyCase.History.ConversationHistory.Count
            QuerySupplied = $useQuery; QueryParameterSupported = 'currentPlayerText' -in $historySampler.GetParameters().Name
            SelectedRecordCount = $lines.Count; SelectedCharacters = $selectedCharacters
            JoinedCharacters = ([string]::Join([Environment]::NewLine, $lines)).Length
            TargetAgeDays = $fixture.HistoryFixtures.TargetAgeDays; TargetFound = $targetFound
            StoredHistoryUnchanged = $storedBefore -ceq $storedAfter
            StoredBeforeSha256 = Get-TextSha $storedBefore; StoredAfterSha256 = Get-TextSha $storedAfter
        })
    }
}
Write-JsonFile (Join-Path $OutputDirectory ($WorkerMode + '.json')) ([ordered]@{
    Assembly = @{ Sha256 = $loadedAssemblySha256; Mvid = $assembly.ManifestModule.ModuleVersionId; Runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription }
    FixtureSha256 = Get-FileSha $fixturePath; AssetSources = $fixture.AssetSources; Scope = $fixture.Scope
    Capabilities = @{
        BuilderParameters = @($builder.GetParameters() | ForEach-Object { $_.Name })
        HistoricalPlanAvailable = $null -ne $historicalType
        HistorySamplerParameters = @($historySampler.GetParameters() | ForEach-Object { $_.Name })
    }
    Scenarios = $rows; HistorySampling = $historyRows
})
[AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
