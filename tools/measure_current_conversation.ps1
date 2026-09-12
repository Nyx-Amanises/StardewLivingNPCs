#requires -Version 7.2
<#
.SYNOPSIS
Measure bounded current-conversation prompts using one built production DLL, offline.
.DESCRIPTION
Uses real English/Chinese prompts, Penny biographies, localized prompt merging, world retrieval
and PromptAssembler. All conversations and snapshots are synthetic. Writes only sanitized JSON
counts, hashes and checks; never writes raw prompts. Does not build, deploy, call a provider,
initialize a game, or read configuration, credentials, saves or player logs.
The reconstructed baseline replaces only CurrentConversation with CurrentConversationBeforeBudget;
it is an arithmetic reconstruction, not a run of an old assembly or a token/latency measurement.
.EXAMPLE
pwsh -NoProfile -File tools/measure_current_conversation.ps1 -AssemblyPath C:/Temp/build/LivingNPCs.dll
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AssemblyPath,
    [string] $RepositoryRoot = '',
    [string] $OutputDirectory = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = Split-Path $PSScriptRoot -Parent }
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if (-not [IO.File]::Exists($AssemblyPath)) { throw 'The supplied built production DLL does not exist.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('LivingNPCs-current-conversation-' + [Guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$outputPath = Join-Path $OutputDirectory 'current-conversation.json'
if ([IO.File]::Exists($outputPath)) { throw 'Use an output directory without an existing current-conversation.json.' }
if (@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'LivingNPCs' }).Count) {
    throw 'Run this script in a fresh pwsh -NoProfile process to avoid a previously loaded production DLL.'
}

# Resolve dependencies from build outputs only. Byte loading avoids locking the production DLL.
$dependencyRoots = [string[]]@([IO.Path]::GetDirectoryName($AssemblyPath),
    (Join-Path $RepositoryRoot 'LivingNPCs.Tests/bin/Debug/net6.0'),
    (Join-Path $RepositoryRoot 'LivingNPCs.Tests/bin/Release/net6.0'))
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

function Get-TextSha([string] $Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text)))
}
function Get-ProductionType([string] $Name) { return $assembly.GetType('LivingNPCs.Dialogue.' + $Name, $true) }
function New-ProductionObject([string] $Name, [Collections.IDictionary] $Values) {
    $type = Get-ProductionType $Name
    $value = [Activator]::CreateInstance($type, $true)
    foreach ($pair in $Values.GetEnumerator()) {
        $property = $type.GetProperty($pair.Key, $flags)
        if ($null -eq $property) { throw "Missing fixture property: $Name.$($pair.Key)" }
        $property.SetValue($value, [Management.Automation.LanguagePrimitives]::ConvertTo($pair.Value, $property.PropertyType))
    }
    return ,$value
}
function ConvertTo-PromptMap([string] $Json) {
    $map = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($pair in (ConvertFrom-Json -InputObject $Json -AsHashtable).GetEnumerator()) { $map[$pair.Key] = [string]$pair.Value }
    return ,$map
}
function New-PromptLookup([Collections.Generic.Dictionary[string,string]] $Map, [object] $Bio) {
    $replace = (Get-ProductionType 'Content.PromptTable').GetMethod('ReplaceTokens', $flags)
    $lookupBlock = {
        param([string] $key, [object] $tokens, [bool] $optimized)
        $keys = if ($optimized) { @(($key + 'Optimized'), $key) } else { @($key) }
        foreach ($baseKey in $keys) {
            if ($Bio.PromptOverrides.ContainsKey($baseKey) -and -not [string]::IsNullOrWhiteSpace($Bio.PromptOverrides[$baseKey])) {
                return [string]$replace.Invoke($null, [object[]]@($Bio.PromptOverrides[$baseKey], $tokens))
            }
            foreach ($candidate in @(($baseKey + '.FemaleNpc'), $baseKey)) {
                if ($Map.ContainsKey($candidate)) { return [string]$replace.Invoke($null, [object[]]@($Map[$candidate], $tokens)) }
            }
        }
        return $null
    }.GetNewClosure()
    return [Management.Automation.LanguagePrimitives]::ConvertTo($lookupBlock, (Get-ProductionType 'Engine.PromptTextLookup'))
}

try {
    $assemblyBytes = [IO.File]::ReadAllBytes($AssemblyPath)
    $assemblySha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($assemblyBytes))
    $assembly = [Reflection.Assembly]::Load($assemblyBytes)
    $assemblerType = Get-ProductionType 'Engine.PromptAssembler'
    $turnType = Get-ProductionType 'ConversationTurn'
    $turnConstructor = $turnType.GetConstructor([Type[]]@([string], [bool], [string]))
    $budget = (Get-ProductionType 'Engine.CurrentConversationProjector').GetField('CharacterBudget', $flags).GetRawConstantValue()
    $escape = (Get-ProductionType 'Engine.PromptDataBoundary').GetMethod('Escape', $flags)
    $assets = @{}
    $sources = [Collections.Generic.List[object]]::new()
    foreach ($relative in @('prompts/default.json', 'prompts/zh.json', 'bios/Penny.json', 'bios-zh/Penny.json', 'world/GameSummary.json')) {
        $path = Join-Path (Join-Path $RepositoryRoot 'LivingNPCs/assets/dialogue') $relative
        $assets[$relative] = [IO.File]::ReadAllText($path)
        $sources.Add([ordered]@{ Asset = $relative; Characters = $assets[$relative].Length; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash })
    }
    $rows = [Collections.Generic.List[object]]::new()
    $shortPrefixes = @{}
    foreach ($locale in @('zh', 'en')) {
        $isChinese = $locale -eq 'zh'
        $map = ConvertTo-PromptMap $assets['prompts/default.json']
        if ($isChinese) {
            $localized = ConvertTo-PromptMap $assets['prompts/zh.json']
            [void](Get-ProductionType 'Content.DialogueContentSetup').GetMethod('MergeLocalizedPrompts', $flags).Invoke($null, [object[]]@($map, $localized))
        }
        $bioKey = if ($isChinese) { 'bios-zh/Penny.json' } else { 'bios/Penny.json' }
        $bio = [Newtonsoft.Json.JsonConvert]::DeserializeObject($assets[$bioKey], (Get-ProductionType 'Content.NpcBio'))
        [void]$bio.GetType().GetMethod('NormalizeNullFields').Invoke($bio, @())
        $lookup = New-PromptLookup $map $bio
        $npcName = if ($isChinese) { '潘妮' } else { 'Penny' }
        $query = if ($isChinese) { '石英收藏现在放在哪里？' } else { 'Where is the quartz collection now?' }
        $evidence = if ($isChinese) {
            @('你把石英收藏放到哪里了？', '石英收藏在书架最上层，蓝色花瓶旁边。',
              '明天可以在湖边见面吗？', '我答应明天在湖边见面。',
              '取消那次见面吧，我改主意了，明天不要在湖边等我。', '明白，湖边的见面已经取消。')
        } else {
            @('Where did you put the quartz collection?', 'The quartz collection is on the upper shelf beside the blue vase.',
              'Could we meet by the lake tomorrow?', 'I promise to meet by the lake tomorrow.',
              'Cancel that meeting. I changed my mind; do not meet me by the lake tomorrow.', 'Understood, the lake meeting is cancelled.')
        }
        $worldLookup = { param([string] $key); if ($map.ContainsKey($key)) { return $map[$key] }; return '' }.GetNewClosure()
        $renderer = (Get-ProductionType 'Content.WorldSummaryRenderer').GetConstructors()[0].Invoke([object[]]@([Func[string,string]]$worldLookup, $null))
        $worldModel = [Newtonsoft.Json.JsonConvert]::DeserializeObject($assets['world/GameSummary.json'], (Get-ProductionType 'Content.WorldSummary'))
        $indexType = Get-ProductionType 'Content.WorldEntryIndex'
        $worldIndex = $indexType.GetConstructors()[0].Invoke([object[]]@($worldModel, $renderer))
        $worldQuery = New-ProductionObject 'Content.WorldRetrievalQuery' @{
            PlayerText = $query; NpcName = 'Penny'; NpcDisplayName = $npcName; LocationName = 'Town'; Season = 'winter'
        }
        $world = $indexType.GetMethod('Retrieve').Invoke($worldIndex, [object[]]@($worldQuery, 8, 6000))
        if ($world.FallbackReason -notin @('', 'NoRelevantEntries')) { throw 'World fixture unexpectedly used a full fallback.' }
        foreach ($profile in @('ordinary', 'long-lines')) {
            foreach ($completed in @(4, 40, 200)) {
                $caseId = "$locale-$profile-$completed"
                $turns = [Array]::CreateInstance($turnType, $completed * 2 + 1)
                for ($index = 0; $index -lt $completed; $index++) {
                    if ($index -lt 3) { $player = $evidence[$index * 2]; $npc = $evidence[$index * 2 + 1] }
                    else {
                        $unit = if ($isChinese) { '叶' } else { 'c' }
                        $padding = $unit * 90
                        if ($profile -eq 'long-lines') {
                            # Exercise UTF-16 surrogate pairs, line endings and escaped angle brackets.
                            $padding = (($unit + '🌿') * 160) + "`r`n<note> & detail</note>"
                        }
                        $player = if ($isChinese) { "普通闲聊第${index}次：我看了窗外的云。$padding" } else { "Routine question ${index}: I watched clouds outside. $padding" }
                        $npc = if ($isChinese) { "普通回复第${index}次：小路还是很安静。$padding" } else { "Routine answer ${index}: The path is quiet. $padding" }
                    }
                    $turns.SetValue($turnConstructor.Invoke([object[]]@($player, $true, "player-$index")), $index * 2)
                    $turns.SetValue($turnConstructor.Invoke([object[]]@($npc, $false, "npc-$index")), $index * 2 + 1)
                }
                $turns.SetValue($turnConstructor.Invoke([object[]]@($query, $true, 'current-player')), $completed * 2)
                $snapshot = New-ProductionObject 'Engine.GameStateSnapshot' @{
                    Year = 1; SeasonIndex = 3; SeasonName = 'winter'; DayOfMonth = 17; TimeOfDay = 1200
                    FarmerName = 'Alex'; FarmerMoney = 6500; FarmerIsMale = $true; FriendshipPoints = 1500
                    LocationName = 'Town'; LocationDisplayName = 'Pelican Town'; NpcIsDatable = $true; NpcIsAdult = $true
                    HomeLocationName = 'Trailer'; ScheduleAvailability = 'Available'; CurrentActivity = 'taking a short break'; KentAbsent = $true
                }
                $request = New-ProductionObject 'GenerationRequest' @{
                    NpcName = 'Penny'; NpcDisplayName = $npcName; Trigger = 'Conversation'; CurrentPlayerText = $query
                    Snapshot = $snapshot; Conversation = $turns; BehaviorContext = 'Synthetic scene: a calm afternoon conversation.'
                }
                $inputBefore = Get-TextSha ([Newtonsoft.Json.JsonConvert]::SerializeObject($request))
                $plan = (Get-ProductionType 'Engine.ContextRoutingPlan').GetMethod('Full', $flags).Invoke($null, @())
                $assemblyInput = New-ProductionObject 'Engine.PromptAssemblyInput' @{
                    Request = $request; Plan = $plan; Bio = $bio; NpcName = 'Penny'; NpcDisplayName = $npcName; NpcGender = 'Female'
                    Conversation = $turns; HistoryLines = [string[]]@('Synthetic past visit: a brief greeting yesterday.')
                    Locale = $locale; UseOptimizedPrompts = $false; Lookup = $lookup
                    WorldSummaryFull = $world.CoreText; WorldSummaryBrief = $world.CoreText; RetrievedWorldContext = $world.RetrievedText
                }
                $assembler = $assemblerType.GetConstructors()[0].Invoke([object[]]@($assemblyInput))
                $prompt = $assemblerType.GetMethod('Assemble').Invoke($assembler, @())
                $inputAfter = Get-TextSha ([Newtonsoft.Json.JsonConvert]::SerializeObject($request))
                $before = [int]$prompt.SectionLengths['CurrentConversationBeforeBudget']
                $actual = [int]$prompt.SectionLengths['CurrentConversation']
                $core = [string]$prompt.CorePrompt
                $match = [regex]::Match($core, '<untrusted_data source="conversation_history">[\s\S]*?</untrusted_data>\r?\n')
                if (-not $match.Success -or $match.Index + $match.Length -lt $actual) { throw "Missing conversation boundary: $caseId" }
                $conversationText = $core.Substring($match.Index + $match.Length - $actual, $actual)
                $checks = [ordered]@{ WithinBudget = $actual -le $budget; InputUnchanged = $inputBefore -ceq $inputAfter }
                $evidenceLabels = @('OldRelatedPlayer', 'OldRelatedNpc', 'OldPromisePlayer', 'OldPromiseNpc', 'OldCancellation', 'OldCancellationReply', 'LatestPlayer')
                $lastPosition = -1
                $orderedEvidence = $true
                $requiredTexts = @($evidence) + @($query)
                for ($index = 0; $index -lt $requiredTexts.Count; $index++) {
                    $quoted = [string]$escape.Invoke($null, [object[]]@($requiredTexts[$index]))
                    $position = $match.Value.IndexOf($quoted, [StringComparison]::Ordinal)
                    $checks[$evidenceLabels[$index] + 'Present'] = $position -ge 0
                    $orderedEvidence = $orderedEvidence -and $position -gt $lastPosition
                    $lastPosition = $position
                }
                $checks.RequiredEvidenceInOriginalOrder = $orderedEvidence
                $presentTurns = @($turns | Where-Object { $match.Value.Contains([string]$escape.Invoke($null, [object[]]@($_.Text))) }).Count
                $prefixes = [ordered]@{}
                foreach ($name in @('System', 'GameConstantContext', 'CacheableNpcContext')) {
                    $prefixes[$name] = Get-TextSha ([string]$prompt.GetType().GetProperty($name).GetValue($prompt))
                }
                $shortKey = "$locale-$profile"
                if ($completed -eq 4) { $shortPrefixes[$shortKey] = $prefixes }
                foreach ($name in $prefixes.Keys) { $checks[$name + 'EqualToFourExchangeCase'] = $prefixes[$name] -ceq $shortPrefixes[$shortKey][$name] }
                $checks.ShortIntactOrLongCompacted = if ($completed -eq 4) { $actual -eq $before -and $presentTurns -eq $turns.Length } else { $actual -lt $before -and $match.Value.Contains('exchange(s) omitted') }
                $total = [int]$prompt.TotalCharacters
                $providerUser = $prompt.GameConstantContext + $prompt.CacheableNpcContext + $prompt.Tail
                $checks.TotalMatchesProviderOrder = $total -eq $prompt.System.Length + $providerUser.Length
                $unresolvedTemplates = [regex]::Matches($prompt.System + $providerUser, '\{\{\s*[\w.\-]+\s*\}\}').Count
                $unresolvedNames = [regex]::Matches($prompt.System + $providerUser, '(?i)\{\{\s*(Name|NpcName|PlayerName|farmerName)\s*\}\}').Count
                $checks.StandardNameTokensResolved = $unresolvedNames -eq 0
                $sectionLengths = [ordered]@{}
                foreach ($pair in $prompt.SectionLengths.GetEnumerator()) { $sectionLengths[$pair.Key] = $pair.Value }
                $failed = @($checks.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object { $_.Key })
                $rows.Add([ordered]@{
                    Id = $caseId; Locale = $locale; Profile = $profile; CompletedExchanges = $completed; InputTurns = $turns.Length
                    InputTextCharacters = ($turns | Measure-Object -Property { $_.Text.Length } -Sum).Sum
                    CurrentConversationBeforeBudget = $before; CurrentConversation = $actual; TotalCharacters = $total
                    ReconstructedBaselineTotalCharacters = $total - $actual + $before; SavedCharacters = $before - $actual
                    FullTurnQuotesPresent = $presentTurns; Checks = $checks; FailedChecks = $failed
                    UnresolvedTemplateTokenCount = $unresolvedTemplates; UnresolvedNameTokenCount = $unresolvedNames
                    InputBeforeSha256 = $inputBefore; InputAfterSha256 = $inputAfter; FixedPrefixSha256 = $prefixes
                    CurrentConversationSha256 = Get-TextSha $conversationText; PayloadSha256 = Get-TextSha ($prompt.System + "`n" + $providerUser)
                    SectionLengths = $sectionLengths
                })
            }
        }
    }
    $failedCases = @($rows | Where-Object { $_.FailedChecks.Count -gt 0 } | ForEach-Object { $_.Id })
    $report = [ordered]@{
        Unit = 'UTF-16 code units (.NET String.Length), including rendered boundaries; not tokens or latency'
        BaselineLabel = 'reconstructed baseline'; BaselineMethod = 'TotalCharacters - CurrentConversation + CurrentConversationBeforeBudget; not an actual old assembly'
        Assembly = @{ Sha256 = $assemblySha; Mvid = $assembly.ManifestModule.ModuleVersionId; Runtime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription }
        ScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash; AssetSources = $sources
        CharacterBudget = $budget; NetworkCalls = 0; AllChecksPassed = $failedCases.Count -eq 0; FailedCases = $failedCases
        Scope = @('One production assembly; real localized prompt merge, token substitution, world index and PromptAssembler.',
            'Synthetic conversation, scene and prior history; same current topic, old fact, promise and cancellation across lengths.',
            'Full context routing, ordinary prompts, no game content pipeline or runtime language helper, no sample dialogue or portrait matching.',
            'PromptAssembler supplies Name/farmerName and section tokens to production ReplaceTokens. Unknown tokens stay unchanged; counts expose any leftovers, and name tokens must resolve.',
            'Original GenerationRequest hashes include every conversation turn ID and text. Only sanitized measurements are written.')
        Scenarios = $rows
    }
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    [IO.File]::WriteAllText($outputPath, (ConvertTo-Json -InputObject $report -Depth 30), [Text.UTF8Encoding]::new($false))
    if ($failedCases.Count -gt 0) { throw "Offline measurement checks failed: $($failedCases -join ', '). See $outputPath" }
    [ordered]@{ Status = 'Offline measurement completed'; Scenarios = $rows.Count; AllChecksPassed = $true; OutputPath = $outputPath } | ConvertTo-Json
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
