#requires -Version 7.2
<#
.SYNOPSIS
Capture real memory/history selection and assembled prompts for synthetic accuracy cases.
.DESCRIPTION
Loads exactly one supplied LivingNPCs.dll and its build dependencies. Never builds, deploys,
initializes a game, reads player data/configuration, or sends network requests. Evaluator truth
and criteria are never passed to production prompt builders. Raw synthetic prompts, replies to
be collected later, and evidence belong in a new Temp output directory. Use separate processes
for baseline/candidate DLLs and the same fixture/assets for comparisons.
TransportOnly returns production thinking/output-budget field choices for non-secret inputs.
ValidateReportPath runs the production text and strict inline metadata parsers offline, using
the matching capture's synthetic snapshot/context. It never invokes classifier fallbacks.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AssemblyDirectory,
    [string] $RepositoryRoot = '',
    [string] $FixturesPath = '',
    [string] $OutputDirectory = '',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')] [string] $Variant = 'candidate',
    [switch] $TransportOnly,
    [string] $TransportModelName = '',
    [string] $TransportThinkingLevel = 'Auto',
    [string] $ValidateReportPath = '',
    [string] $CapturePath = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Python callers decode redirected stdout as UTF-8, independently of the Windows console page.
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
if (-not $RepositoryRoot) { $RepositoryRoot = Split-Path $PSScriptRoot -Parent }
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$AssemblyDirectory = [IO.Path]::GetFullPath($AssemblyDirectory)
$assemblyPath = Join-Path $AssemblyDirectory 'LivingNPCs.dll'
if (-not [IO.File]::Exists($assemblyPath)) { throw 'Supply an isolated build directory containing LivingNPCs.dll.' }
if (@([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'LivingNPCs' }).Count) { throw 'Use a fresh pwsh process for each assembly.' }
$dependencyRoots = [string[]]@($AssemblyDirectory, (Join-Path $RepositoryRoot 'LivingNPCs.Tests/bin/Debug/net6.0'), (Join-Path $RepositoryRoot 'LivingNPCs.Tests/bin/Release/net6.0'))
$resolverBlock = {
    param([object] $sender, [ResolveEventArgs] $request)
    $name = [Reflection.AssemblyName]::new($request.Name).Name
    foreach ($loaded in [AppDomain]::CurrentDomain.GetAssemblies()) { if ($loaded.GetName().Name -eq $name) { return $loaded } }
    foreach ($directory in $dependencyRoots) {
        $candidate = [IO.Path]::Combine($directory, $name + '.dll')
        if ([IO.File]::Exists($candidate)) { return [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($candidate)) }
    }
    return $null
}.GetNewClosure()
$resolver = [ResolveEventHandler]$resolverBlock
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
function Get-Sha([string] $Text) { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))) }
function Get-Type([string] $Name) { return $assembly.GetType('LivingNPCs.' + $Name, $true) }
function New-Model([string] $Name, [object] $Data) {
    return ,([Newtonsoft.Json.JsonConvert]::DeserializeObject((ConvertTo-Json -InputObject $Data -Depth 60 -Compress), (Get-Type $Name)))
}
function Set-Value([object] $Object, [string] $Name, [object] $Value) {
    $type = [object].GetMethod('GetType').Invoke($Object, @())
    $property = $type.GetProperty($Name, $flags)
    if ($null -ne $property) { $property.SetValue($Object, [Management.Automation.LanguagePrimitives]::ConvertTo($Value, $property.PropertyType)); return }
    $field = $type.GetField($Name, $flags)
    if ($null -eq $field) { throw "Missing fixture member $Name" }
    $field.SetValue($Object, [Management.Automation.LanguagePrimitives]::ConvertTo($Value, $field.FieldType))
}
function New-ObjectWith([string] $Name, [Collections.IDictionary] $Data) {
    $value = [Activator]::CreateInstance((Get-Type $Name), $true)
    foreach ($pair in $Data.GetEnumerator()) { Set-Value $value $pair.Key $pair.Value }
    return ,$value
}
function Invoke-Named([Reflection.MethodInfo] $Method, [Collections.IDictionary] $Values) {
    $parameters = $Method.GetParameters(); $arguments = [object[]]::new($parameters.Length)
    for ($index = 0; $index -lt $parameters.Length; $index++) {
        $parameter = $parameters[$index]
        if ($Values.Contains($parameter.Name)) {
            $value = $Values[$parameter.Name]
            if ($null -eq $value) { $arguments[$index] = $null }
            else { $arguments.SetValue([Management.Automation.LanguagePrimitives]::ConvertTo($value, $parameter.ParameterType), $index) }
        } elseif ($parameter.HasDefaultValue) { $arguments[$index] = $parameter.DefaultValue }
        else { throw "Missing fixture argument $($Method.Name).$($parameter.Name)" }
    }
    return ,$Method.Invoke($null, $arguments)
}
function New-PromptMap([string] $Json) {
    $map = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($pair in (ConvertFrom-Json -InputObject $Json -AsHashtable).GetEnumerator()) { $map[$pair.Key] = [string]$pair.Value }
    return ,$map
}
function New-Lookup([object] $Map, [object] $Bio) {
    $replace = (Get-Type 'Dialogue.Content.PromptTable').GetMethod('ReplaceTokens', $flags)
    $lookupBlock = {
        param([string] $key, [object] $tokens, [bool] $optimized)
        $keys = if ($optimized) { @(($key + 'Optimized'), $key) } else { @($key) }
        foreach ($baseKey in $keys) {
            if ($Bio.PromptOverrides.ContainsKey($baseKey) -and -not [string]::IsNullOrWhiteSpace($Bio.PromptOverrides[$baseKey])) { return [string]$replace.Invoke($null, [object[]]@($Bio.PromptOverrides[$baseKey], $tokens)) }
            foreach ($candidate in @(($baseKey + '.FemaleNpc'), $baseKey)) { if ($Map.ContainsKey($candidate)) { return [string]$replace.Invoke($null, [object[]]@($Map[$candidate], $tokens)) } }
        }
        return $null
    }.GetNewClosure()
    return [Management.Automation.LanguagePrimitives]::ConvertTo($lookupBlock, (Get-Type 'Dialogue.Engine.PromptTextLookup'))
}
function Resolve-TempOutput([string] $Directory) {
    if (-not $Directory) { $Directory = Join-Path ([IO.Path]::GetTempPath()) ('LivingNPCs-memory-accuracy-' + [Guid]::NewGuid().ToString('N')) }
    $resolved = [IO.Path]::GetFullPath($Directory)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Raw synthetic artifacts must stay in a new Temp subdirectory.' }
    return $resolved
}
function Invoke-ReplyValidation([string] $ReportPath, [string] $SourceCapturePath, [string] $Directory) {
    $capture = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($SourceCapturePath)) -AsHashtable
    $sourceReport = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($ReportPath)) -AsHashtable
    if ($capture.SchemaVersion -ne 1 -or $capture.SyntheticOnly -ne $true -or
        $sourceReport.SchemaVersion -ne 1 -or $sourceReport.SyntheticOnly -ne $true -or
        $sourceReport.Results.Count -gt 24) { throw 'Only bounded synthetic captures and run reports are accepted.' }
    if ($capture.AssemblySha256 -cne $assemblySha) { throw 'Parser assembly must match the supplied capture exactly.' }
    if ($sourceReport.FixtureSha256 -cne $capture.FixtureSha256) { throw 'Run report and capture must use identical fixtures.' }
    $Directory = Resolve-TempOutput $Directory
    $validationPath = Join-Path $Directory ($capture.Variant + '-parser-validation.json')
    if ([IO.File]::Exists($validationPath)) { throw 'Choose a new parser validation path; prior artifacts are not overwritten.' }
    $selected = @($sourceReport.Results | Where-Object { $_.Variant -ceq $capture.Variant })
    if ($selected.Count -eq 0) { throw 'Run report has no replies matching this capture variant.' }
    $parser = (Get-Type 'Dialogue.Engine.ResponseParser').GetMethod('Parse', $flags)
    $inlineParser = (Get-Type 'Dialogue.Engine.LivingNpcMetadataExtractionPass').GetMethod('ParseInlineResponse', $flags)
    $snapshotType = Get-Type 'Dialogue.Engine.GameStateSnapshot'
    $policy = Get-Type 'Dialogue.Engine.RsvAiPolicy'
    $blockedReference = $policy.GetMethod('ContainsBlockedReference', $flags)
    $removeBlockedLines = $policy.GetMethod('RemoveBlockedLines', $flags)
    $emptyAliases = [Array]::CreateInstance((Get-Type 'Behavior.HelpRequestItemAlias'), 0)
    $validated = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($reply in $selected) {
        if (-not $seen.Add([string]$reply.RequestId)) { throw 'Duplicate request ID in parser validation input.' }
        if ($reply.AssemblySha256 -cne $assemblySha) { throw 'Each reply must match the supplied parser assembly exactly.' }
        $matches = @($capture.Cases | Where-Object { $_.Id -ceq $reply.CaseId })
        if ($matches.Count -ne 1) { throw 'Each reply must match exactly one captured case.' }
        $row = $matches[0]
        if ($reply.PromptSha256 -cne $row.PromptSha256 -or
            -not [string]::Equals([string]$reply.Transport.prompt_sha256, [string]$row.PromptSha256, [StringComparison]::OrdinalIgnoreCase) -or
            (Get-Sha ($row.System + [char]10 + $row.User)) -cne $row.PromptSha256) { throw 'Reply/capture prompt hash mismatch.' }
        if (-not $row.Contains('Snapshot') -or -not $row.Contains('ParserProfile')) { throw 'Recapture with Snapshot and ParserProfile before production validation.' }
        $profile = $row.ParserProfile
        if ($profile.Trigger -cne 'Conversation' -or $profile.HelpItemAliases.Count -ne 0) { throw 'Only the captured synthetic conversation parser profile is supported.' }
        $snapshot = New-Model 'Dialogue.Engine.GameStateSnapshot' $row.Snapshot
        $actionContract = New-Model 'Dialogue.Engine.SceneActionContractPlan' $row.ActionContract
        $chatHistory = @($row.OriginalConversation | ForEach-Object {
            $text = if ($blockedReference.Invoke($null, [object[]]@([string]$_.Text))) { '' } else { [string]$_.Text }
            @{ Id = [string]$_.Id; Text = $text; IsPlayerLine = [bool]$_.IsPlayer }
        })
        $context = New-Model 'Dialogue.Engine.DialogueContext' @{
            Accept = $null; ChatHistory = $chatHistory
            LivingNpcExtraPrompt = [string]$removeBlockedLines.Invoke($null, [object[]]@([string]$row.BehaviorContext))
            Hearts = if ($snapshot.FriendshipExists) { $snapshot.Hearts } else { $null }
            Location = $snapshot.LocationName; TimeOfDay = [string]$snapshotType.GetMethod('ClockText', $flags).Invoke($null, [object[]]@($snapshot.TimeOfDay))
            AbsoluteDay = $snapshot.Now.GetType().GetMethod('ToAbsoluteDays').Invoke($snapshot.Now, @())
            Weather = [string[]]$snapshot.WeatherFlags; CurrentActivity = $snapshot.CurrentActivity
            NextScheduleLocation = $snapshot.NextScheduleLocation; MinutesUntilNextSchedule = $snapshot.MinutesUntilNextSchedule
        }
        $portraits = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($portrait in $profile.ValidPortraits) { [void]$portraits.Add([string]$portrait) }
        if ($portraits.Count -eq 0) { [void]$portraits.Add('0') }
        $raw = [string]$reply.Transport.content
        $before = Get-Sha ([Newtonsoft.Json.JsonConvert]::SerializeObject(@($snapshot, $actionContract, $context)))
        $body = Invoke-Named $parser @{ rawText = $raw; validPortraits = $portraits; farmerName = [string]$snapshot.FarmerName; fixPunctuation = [bool]$profile.FixPunctuation; debug = $false }
        $playerText = if ($blockedReference.Invoke($null, [object[]]@([string]$row.CurrentPlayerText))) { '' } else { [string]$row.CurrentPlayerText }
        $metadata = Invoke-Named $inlineParser @{ responseText = $raw; playerText = $playerText; visibleNpcReply = [string]$body.DialogueLine; context = $context; actionContract = $actionContract; helpItemAliases = $emptyAliases }
        $after = Get-Sha ([Newtonsoft.Json.JsonConvert]::SerializeObject(@($snapshot, $actionContract, $context)))
        $leaks = [Collections.Generic.List[string]]::new()
        $visible = @([string]$body.DialogueLine) + @($body.Options)
        for ($index = 0; $index -lt $visible.Count; $index++) {
            $location = if ($index -eq 0) { 'dialogue' } else { 'option-' + $index }
            if ($visible[$index].IndexOf('LIVINGNPCS_META', [StringComparison]::OrdinalIgnoreCase) -ge 0) { $leaks.Add('metadata-marker-in-' + $location) }
            if ([regex]::IsMatch($visible[$index], '(?i)</?(?:think|analysis|reasoning)(?:\s|>)')) { $leaks.Add('hidden-reasoning-tag-in-' + $location) }
        }
        $bodyNonEmpty = -not [string]::IsNullOrWhiteSpace([string]$body.DialogueLine)
        $validated.Add([ordered]@{
            RequestId = $reply.RequestId; CaseId = $reply.CaseId; Variant = $reply.Variant; PromptSha256 = $row.PromptSha256; ReplySha256 = Get-Sha $raw
            TransportStatus = $reply.Transport.status; ParserInputsUnchanged = $before -ceq $after
            BodyParser = [ordered]@{ Success = [bool]$body.Success; FailureReason = [string]$body.FailureReason; BodyNonEmpty = $bodyNonEmpty
                RawStartsWithDialoguePrefix = $raw.TrimStart().StartsWith('-', [StringComparison]::Ordinal); DialogueLine = [string]$body.DialogueLine; Options = @($body.Options) }
            VisibleHiddenMarkerLeaks = $leaks.ToArray()
            InlineMetadata = [ordered]@{ Success = [bool]$metadata.Success; FailureReason = [string]$metadata.FailureReason
                Analysis = if ($metadata.Success) { ConvertFrom-Json -InputObject $metadata.Analysis.ToJson() -AsHashtable } else { $null } }
            StrictPipelineParseSuccess = [bool]$body.Success -and $bodyNonEmpty -and $metadata.Success -and $leaks.Count -eq 0 -and $before -ceq $after
        })
    }
    $validation = [ordered]@{
        SchemaVersion = 1; SyntheticOnly = $true; Mode = 'offline production parser validation'; NetworkCalls = 0
        AssemblySha256 = $assemblySha; CaptureSha256 = (Get-FileHash -LiteralPath $SourceCapturePath -Algorithm SHA256).Hash
        SourceReportSha256 = (Get-FileHash -LiteralPath $ReportPath -Algorithm SHA256).Hash
        FixtureSha256 = $capture.FixtureSha256; Variant = $capture.Variant
        ClassifierFallbackInvoked = $false; GameActionsExecuted = $false
        Scope = @('Production ResponseParser.Parse and strict LivingNpcMetadataExtractionPass.ParseInlineResponse only.',
            'DialogueContext maps the captured Snapshot, full original synthetic conversation, BehaviorContext and current player text as DialogueEngine prepares them.',
            'Uses captured bio portrait markers and punctuation policy; no runtime portrait texture or game item-alias catalogue.',
            'Parser acceptance is separate from semantic accuracy, renderer behavior and runtime action authorization.')
        Results = $validated
    }
    [void][IO.Directory]::CreateDirectory($Directory)
    [IO.File]::WriteAllText($validationPath, (ConvertTo-Json -InputObject $validation -Depth 60), [Text.UTF8Encoding]::new($false))
    [ordered]@{ Status = 'Validated replies with production parsers offline'; Replies = $validated.Count
        BodyPassed = @($validated | Where-Object { $_.BodyParser.Success }).Count
        InlinePassed = @($validated | Where-Object { $_.InlineMetadata.Success }).Count
        StrictPipelinePassed = @($validated | Where-Object { $_.StrictPipelineParseSuccess }).Count
        NetworkCalls = 0; OutputPath = $validationPath } | ConvertTo-Json
}
try {
    $assemblyBytes = [IO.File]::ReadAllBytes($assemblyPath)
    $assembly = [Reflection.Assembly]::Load($assemblyBytes)
    $assemblySha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($assemblyBytes))
    if ($TransportOnly -and ($ValidateReportPath -or $CapturePath)) { throw 'TransportOnly and reply validation are separate offline modes.' }
    if ($TransportOnly) {
        if ([string]::IsNullOrWhiteSpace($TransportModelName)) { throw 'TransportModelName is required for TransportOnly.' }
        $thinking = Get-Type 'Dialogue.Llm.LlmThinking'
        $body = [Newtonsoft.Json.Linq.JObject]::new()
        [void]$thinking.GetMethod('AddOpenAiCompatibleThinkingParameters', $flags).Invoke($null, [object[]]@($body, $TransportModelName, $TransportThinkingLevel))
        [ordered]@{ AssemblySha256 = $assemblySha; Parameters = ConvertFrom-Json -InputObject $body.ToString() -AsHashtable
            OutputBudgetField = $thinking.GetMethod('OpenAiMaxTokensFieldName', $flags).Invoke($null, [object[]]@($TransportModelName)) } | ConvertTo-Json -Depth 8
        return
    }
    if ($ValidateReportPath -or $CapturePath) {
        if (-not $ValidateReportPath -or -not $CapturePath) { throw 'Reply validation requires both ValidateReportPath and CapturePath.' }
        Invoke-ReplyValidation $ValidateReportPath $CapturePath $OutputDirectory
        return
    }
    if (-not $FixturesPath) { $FixturesPath = Join-Path $PSScriptRoot 'fixtures/memory_agreement_accuracy.json' }
    $fixture = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($FixturesPath)) -AsHashtable
    if ($fixture.SchemaVersion -ne 1 -or $fixture.SyntheticOnly -ne $true -or $fixture.Cases.Count -gt 12) { throw 'Expected the bounded synthetic accuracy fixture schema.' }
    $OutputDirectory = Resolve-TempOutput $OutputDirectory
    $outputPath = Join-Path $OutputDirectory ($Variant + '-capture.json')
    if ([IO.File]::Exists($outputPath)) { throw 'Choose a new capture path; captures are not overwritten.' }
    $assets = @{}; $assetHashes = [ordered]@{}
    foreach ($name in @('prompts/default.json', 'prompts/zh.json', 'bios/Penny.json', 'bios-zh/Penny.json', 'world/GameSummary.json')) {
        $assetPath = Join-Path (Join-Path $RepositoryRoot 'LivingNPCs/assets/dialogue') $name
        $assets[$name] = [IO.File]::ReadAllText($assetPath); $assetHashes[$name] = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
    }
    Set-Value ((Get-Type 'ModEntry').GetProperty('ActiveConfig', $flags).GetValue($null)) 'ConcisePromptContext' $false
    $now = New-Model 'Dialogue.StardewTime' $fixture.Defaults.Now
    $currentDay = [int]$now.GetType().GetMethod('ToAbsoluteDays').Invoke($now, @())
    $builder = (Get-Type 'Behavior.BehaviorPromptContextBuilder').GetMethod('BuildPromptContext', $flags)
    $recallType = Get-Type 'Behavior.MemoryRecallService'
    $historySampler = (Get-Type 'Dialogue.Engine.HistorySampler').GetMethod('Sample', $flags)
    $turnType = Get-Type 'Dialogue.ConversationTurn'; $turnConstructor = $turnType.GetConstructor([Type[]]@([string], [bool], [string]))
    $escape = (Get-Type 'Dialogue.Engine.PromptDataBoundary').GetMethod('Escape', $flags)
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($case in $fixture.Cases) {
        $npcDisplay = if ($case.Locale -eq 'zh') { '潘妮' } else { 'Penny' }
        $map = New-PromptMap $assets['prompts/default.json']
        if ($case.Locale -eq 'zh') { [void](Get-Type 'Dialogue.Content.DialogueContentSetup').GetMethod('MergeLocalizedPrompts', $flags).Invoke($null, [object[]]@($map, (New-PromptMap $assets['prompts/zh.json']))) }
        $bioName = if ($case.Locale -eq 'zh') { 'bios-zh/Penny.json' } else { 'bios/Penny.json' }
        $bio = [Newtonsoft.Json.JsonConvert]::DeserializeObject($assets[$bioName], (Get-Type 'Dialogue.Content.NpcBio'))
        [void]$bio.GetType().GetMethod('NormalizeNullFields').Invoke($bio, @())
        $lookup = New-Lookup $map $bio
        $plainLookup = { param([string] $key); foreach ($candidate in @(($key + '.FemaleNpc'), $key)) { if ($map.ContainsKey($candidate)) { return $map[$candidate] } }; return '' }.GetNewClosure()
        $sourceText = [ordered]@{}; $conversation = [Collections.Generic.List[object]]::new(); $fillerIndex = 0
        foreach ($block in $case.Conversation) {
            if ($block.Contains('Filler')) {
                for ($index = 0; $index -lt $block.Filler; $index++) {
                    $fillerIndex++
                    $playerFiller = if ($case.Locale -eq 'en') { "Routine question ${fillerIndex}: clouds passed the roof. " + ('c' * 80) } else { "普通闲聊第${fillerIndex}次：窗边有云影，路上很安静。" + ('叶' * 80) }
                    $npcFiller = if ($case.Locale -eq 'en') { "Routine answer ${fillerIndex}: leaves moved beside the path. " + ('l' * 80) } else { "普通回复第${fillerIndex}次：我看见路边树叶轻轻摇动。" + ('云' * 80) }
                    $conversation.Add(@{ Id = "filler-$fillerIndex-player"; Text = $playerFiller; IsPlayer = $true })
                    $conversation.Add(@{ Id = "filler-$fillerIndex-npc"; Text = $npcFiller; IsPlayer = $false })
                }
            } else {
                $npcText = [string]$block.Npc
                if ($block.Contains('RepeatText')) { $npcText += [string]$block.RepeatText * [int]$block.RepeatCount }
                foreach ($line in @(@{ Id = $block.Id + '-player'; Text = [string]$block.Player; IsPlayer = $true }, @{ Id = $block.Id + '-npc'; Text = $npcText; IsPlayer = $false })) {
                    $conversation.Add($line); $sourceText[$line.Id] = $line.Text
                }
            }
        }
        $conversation.Add(@{ Id = 'current-player'; Text = $case.Query; IsPlayer = $true }); $sourceText['current-player'] = $case.Query
        $turns = [Array]::CreateInstance($turnType, $conversation.Count)
        for ($index = 0; $index -lt $conversation.Count; $index++) { $line = $conversation[$index]; $turns.SetValue($turnConstructor.Invoke([object[]]@($line.Text, [bool]$line.IsPlayer, $line.Id)), $index) }
        $records = [Collections.Generic.List[object]]::new()
        for ($age = 1; $age -le $case.HistoryDistractors; $age++) {
            $records.Add(@{ Item1 = $now.GetType().GetMethod('AddDays').Invoke($now, [object[]]@(-$age)); Item2 = @{ ConversationElements = @(
                @{ Id = "archive-$age-player"; Text = "第$age 次归档闲聊：我看到白云经过屋顶。"; IsPlayerLine = $true },
                @{ Id = "archive-$age-npc"; Text = '镇上的小路很安静。'; IsPlayerLine = $false }) } })
        }
        foreach ($record in $case.History) {
            $elements = @($record.Turns | ForEach-Object { $sourceText[$_.Id] = $_.Text; @{ Id = $_.Id; Text = $_.Text; IsPlayerLine = $_.IsPlayer } })
            $records.Add(@{ Item1 = $now.GetType().GetMethod('AddDays').Invoke($now, [object[]]@(-[int]$record.AgeDays)); Item2 = @{ ConversationElements = $elements } })
        }
        $history = New-Model 'Dialogue.Persistence.StardewEventHistory' @{ schemaVersion = 1; npcName = 'Penny'; ConversationHistory = $records.ToArray() }
        $stateData = @{ NpcName = 'Penny'; RelationshipTrust = 80; Familiarity = 45; InteractionComfortTier = 'Trusted'; CurrentEmotion = 'Calm'; LastFriendshipHearts = 6; LastInteraction = 'a brief greeting' }
        foreach ($store in @('LongTermMemories', 'PlayerPreferenceMemories', 'SharedExperiences', 'CommunityImpressions', 'HelpRequests', 'Conflicts', 'DialogueBehaviorInfluences')) { $stateData[$store] = [Collections.Generic.List[object]]::new() }
        for ($index = 0; $index -lt $case.StateDistractors; $index++) {
            $stateData.LongTermMemories.Add(@{ Kind = 'fact'; Subject = "cloth-$index"; Summary = "The farmer keeps cloth sample $index in a drawer."; Importance = 90 - $index; CreatedTotalDays = $currentDay - 10; LastUpdatedTotalDays = $currentDay - 2; TimesReinforced = 1 })
            $stateData.SharedExperiences.Add(@{ Key = "cloud-$index"; Type = 'none'; Summary = "Penny and the farmer watched ordinary cloud $index pass over town."; Importance = 90 - $index; CreatedTotalDays = $currentDay - 10; LastUpdatedTotalDays = $currentDay - 2; FollowUpEligibleTotalDays = -1; TimesReinforced = 1 })
        }
        foreach ($store in $case.State.Keys) {
            foreach ($entry in $case.State[$store]) {
                if ($store -eq 'HelpRequests' -and $entry.Status -in @('Offered', 'Pending')) { throw 'Active help requires runtime game services and is outside this offline capture.' }
                $entry.CreatedTotalDays = $currentDay - [int]$entry.AgeDays
                $entry.LastUpdatedTotalDays = if ($entry.Contains('LastUpdatedAgeDays')) { $currentDay - [int]$entry.LastUpdatedAgeDays } else { $entry.CreatedTotalDays }
                $entry.TimesReinforced = 1
                if ($store -eq 'SharedExperiences') { $entry.FollowUpEligibleTotalDays = -1 }
                if ($entry.Contains('FulfilledAgeDays')) { $entry.FulfilledTotalDays = $currentDay - [int]$entry.FulfilledAgeDays; $entry.LastMentionedTotalDays = $currentDay - 1; $entry.DueTotalDays = $currentDay - 1 }
                $stateData[$store].Add($entry); $sourceText['state:' + $entry.AuditId] = [string]$entry.Summary
            }
        }
        $state = New-Model 'Behavior.LivingNpcState' $stateData
        $stateBeforeNormalization = Get-Sha ([Newtonsoft.Json.JsonConvert]::SerializeObject($state))
        foreach ($pair in @{ LongTermMemories = 'LongTermMemoryStore'; PlayerPreferenceMemories = 'PlayerPreferenceMemoryStore' }.GetEnumerator()) {
            $normalize = (Get-Type ('Behavior.' + $pair.Value)).GetMethod('NormalizeForStore', $flags)
            foreach ($memory in $state.($pair.Key)) { [void]$normalize.Invoke($null, [object[]]@($memory)) }
        }
        $storedBefore = Get-Sha ([Newtonsoft.Json.JsonConvert]::SerializeObject(@($state, $history, $turns)))
        $world = New-Model 'Behavior.WorldContextSnapshot' @{
            LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'; Season = 'summer'; DayOfMonth = 15; TimeOfDay = 1200; FriendshipHearts = 6; NearbyNpcNames = @()
            PromptLabel = 'a quiet afternoon in town'; DebugLabel = 'synthetic'; Reason = 'synthetic'; StateInfluence = @{ Mood = ''; Inclination = ''; Priority = 0; Reason = '' }
            Progression = @{ Year = 2; Route = 'undecided'; ProfessionFocuses = @(); SpouseNames = @(); ModProgression = @{ Sve = @{ Installed = $false } }; PromptLabel = 'ordinary town context'; ReplyGuidance = 'keep requests modest' }
            ProgressionKnowledge = @{ ObservationDomains = @(); LearnedDomains = @(); AttitudeTraits = @(); KnownProfessionFocuses = @(); ModProgression = @{ PromptLabel = 'no expansion knowledge' }; TrustedRelationship = $true; PromptLabel = 'ordinary town knowledge' }
        }
        $recent = [Array]::CreateInstance((Get-Type 'Behavior.BehaviorMemoryEntry'), 0)
        $recall = Invoke-Named ($recallType.GetMethod('BuildPlan', $flags)) @{ state = $state; world = $world; recentEntries = $recent; longTermCount = 3; preferenceCount = 4; currentTotalDays = $currentDay; currentPlayerText = $case.Query }
        $community = Invoke-Named ($recallType.GetMethod('BuildCommunityImpressionPlan', $flags)) @{ state = $state; maxCount = 2; currentTotalDays = $currentDay; currentPlayerText = $case.Query }
        $npc = [Activator]::CreateInstance($builder.GetParameters()[0].ParameterType, $true); Set-Value $npc 'Name' 'Penny'; Set-Value $npc 'displayName' $npcDisplay
        $disposition = New-Model 'Behavior.NpcDispositionProfile' @{ PromptLabel = 'careful temperament'; BackgroundPrompt = 'a thoughtful tutor'; DialoguePrompt = 'gentle phrasing' }
        $expression = New-Model 'Behavior.EmotionalExpressionCue' @{ Key = 'synthetic'; PromptLabel = 'soft-spoken'; ReplyGuidance = 'speak gently and concretely'; Directness = 50; MemoryHold = 50; EmotionDecayMultiplier = 1; ConflictDecayMultiplier = 1; RepairResponsivenessMultiplier = 1 }
        $behavior = [string](Invoke-Named $builder @{ npc = $npc; recentEntries = $recent; state = $state; world = $world; disposition = $disposition; emotionalStyle = $expression; recallPlan = $recall; communityImpressions = $community; maxPendingHelpRequestsPerNpc = 0; helpRequestCooldownDays = 3; currentTotalDays = $currentDay; currentTimeOfDay = 1200; currentPlayerText = $case.Query; markFollowUpCues = $false })
        $historyLines = Invoke-Named $historySampler @{ history = $history; activeDialogueEvents = [Collections.Generic.KeyValuePair[string,int][]]@(); now = $now; npcDisplayName = $npcDisplay; currentConversationId = [string]$conversation[0].Id; getPrompt = [Func[string,string]]$plainLookup; currentPlayerText = $case.Query }
        $renderer = (Get-Type 'Dialogue.Content.WorldSummaryRenderer').GetConstructors()[0].Invoke([object[]]@([Func[string,string]]$plainLookup, $null))
        $worldModel = [Newtonsoft.Json.JsonConvert]::DeserializeObject($assets['world/GameSummary.json'], (Get-Type 'Dialogue.Content.WorldSummary'))
        $indexType = Get-Type 'Dialogue.Content.WorldEntryIndex'; $worldIndex = $indexType.GetConstructors()[0].Invoke([object[]]@($worldModel, $renderer))
        $worldQuery = New-Model 'Dialogue.Content.WorldRetrievalQuery' @{ PlayerText = $case.Query; NpcName = 'Penny'; NpcDisplayName = $npcDisplay; LocationName = 'Town'; Season = 'summer' }
        $worldResult = $indexType.GetMethod('Retrieve').Invoke($worldIndex, [object[]]@($worldQuery, 8, 6000))
        $snapshot = New-Model 'Dialogue.Engine.GameStateSnapshot' @{ Year = 2; SeasonIndex = 1; SeasonName = 'summer'; DayOfMonth = 15; TimeOfDay = 1200; FarmerName = $fixture.Defaults.FarmerName; FarmerMoney = 6500; FarmerIsMale = $true; FriendshipPoints = 1500; LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'; NpcIsDatable = $true; NpcIsAdult = $true; HomeLocationName = 'Trailer'; ScheduleAvailability = 'Available'; CurrentActivity = 'taking a short break' }
        $request = New-ObjectWith 'Dialogue.GenerationRequest' @{ NpcName = 'Penny'; NpcDisplayName = $npcDisplay; Trigger = 'Conversation'; CurrentPlayerText = $case.Query; Snapshot = $snapshot; Conversation = $turns; BehaviorContext = $behavior }
        $plan = (Get-Type 'Dialogue.Engine.ContextRoutingPlan').GetMethod('Full', $flags).Invoke($null, @())
        $assemblyInput = New-ObjectWith 'Dialogue.Engine.PromptAssemblyInput' @{ Request = $request; Plan = $plan; Bio = $bio; NpcName = 'Penny'; NpcDisplayName = $npcDisplay; NpcGender = 'Female'; Conversation = $turns; HistoryLines = $historyLines; Locale = $case.Locale; ApplyTranslation = $true; UseOptimizedPrompts = $false; Lookup = $lookup; WorldSummaryFull = $worldResult.CoreText; WorldSummaryBrief = $worldResult.CoreText; RetrievedWorldContext = $worldResult.RetrievedText }
        $assemblerType = Get-Type 'Dialogue.Engine.PromptAssembler'; $assembler = $assemblerType.GetConstructors()[0].Invoke([object[]]@($assemblyInput))
        $prompt = $assemblerType.GetMethod('Assemble').Invoke($assembler, @())
        $llmRequest = $prompt.GetType().GetMethod('CreateLlmRequest', $flags).Invoke($prompt, [object[]]@($null, $null))
        $providerUser = [string]$llmRequest.GetType().GetMethod('ConcatenatedUserContent', $flags).Invoke($llmRequest, @())
        $currentMatch = [regex]::Match($prompt.CorePrompt, '<untrusted_data source="conversation_history">[\s\S]*?</untrusted_data>')
        $historyText = [string]::Join([Environment]::NewLine, $historyLines)
        $evidence = @($sourceText.GetEnumerator() | ForEach-Object {
            $quoted = [string]$escape.Invoke($null, [object[]]@($_.Value))
            [ordered]@{ Id = $_.Key; Text = $_.Value; InCurrentConversation = $currentMatch.Value.Contains($quoted); InHistory = $historyText.Contains([string]$_.Value); InBehavior = $behavior.Contains([string]$_.Value) }
        })
        $presentEvidenceIds = @($evidence | Where-Object { $_.InCurrentConversation -or $_.InHistory -or $_.InBehavior } | ForEach-Object { $_.Id })
        $diagnostics = [ordered]@{ MissingRequiredEvidence = @($case.RequiredEvidence | Where-Object { $_ -notin $presentEvidenceIds }) }
        if ($case.Contains('ExpectedOmissions')) { $diagnostics.ExpectedOmissions = $case.ExpectedOmissions; $diagnostics.UnexpectedlyPresentOmissions = @($case.ExpectedOmissions | Where-Object { $_ -in $presentEvidenceIds }) }
        if ($case.Contains('ExpectedMissingContextNotice')) { $diagnostics.ExpectedMissingContextNotice = $case.ExpectedMissingContextNotice; $diagnostics.MissingContextNoticePresent = $prompt.CorePrompt.Contains([string]$case.ExpectedMissingContextNotice) }
        $lengths = [ordered]@{}; foreach ($pair in $prompt.SectionLengths.GetEnumerator()) { $lengths[$pair.Key] = $pair.Value }
        $storedAfter = Get-Sha ([Newtonsoft.Json.JsonConvert]::SerializeObject(@($state, $history, $turns)))
        $historyCharacters = 0; foreach ($line in $historyLines) { $historyCharacters += $line.Length }
        $checks = @{ CurrentConversationBounded = $lengths.CurrentConversation -le 8000; HistoryBounded = $historyCharacters -le 4000 -and $historyLines.Count -le 20
            RecallCountsBounded = $recall.LongTermMemories.Count -le 3 -and $recall.PlayerPreferences.Count -le 4 -and $community.Count -le 2
            ProviderLengthMatches = $prompt.TotalCharacters -eq $llmRequest.SystemPrompt.Length + $providerUser.Length }
        $rows.Add([ordered]@{
            Id = $case.Id; Variant = $Variant; Locale = $case.Locale; System = [string]$llmRequest.SystemPrompt; User = $providerUser; ResponseStart = [string]$llmRequest.ResponseStart
            MaxTokens = $llmRequest.MaxTokens; DisableThinking = $llmRequest.DisableThinking; AllowRetry = $llmRequest.AllowRetry; Checks = $checks
            PromptSha256 = Get-Sha ($prompt.System + "`n" + $providerUser); TotalCharacters = $prompt.TotalCharacters; SectionLengths = $lengths
            HistorySelectedCount = $historyLines.Count; HistorySelectedCharacters = $historyCharacters; BehaviorCharacters = $behavior.Length; SourceEvidence = $evidence; EvidenceDiagnostics = $diagnostics
            OriginalConversation = $conversation; OriginalHistory = ConvertFrom-Json -InputObject ([Newtonsoft.Json.JsonConvert]::SerializeObject($history)) -AsHashtable
            OriginalState = ConvertFrom-Json -InputObject ([Newtonsoft.Json.JsonConvert]::SerializeObject($state)) -AsHashtable
            CurrentPlayerText = $case.Query; BehaviorContext = $behavior; SelectedHistoryLines = $historyLines
            Snapshot = ConvertFrom-Json -InputObject ([Newtonsoft.Json.JsonConvert]::SerializeObject($snapshot)) -AsHashtable
            ParserProfile = [ordered]@{ Trigger = 'Conversation'; ValidPortraits = @($bio.ValidPortraits); FixPunctuation = $case.Locale.StartsWith('zh', [StringComparison]::OrdinalIgnoreCase); HelpItemAliases = @() }
            StateBeforeNormalizationSha256 = $stateBeforeNormalization
            InputBeforeSha256 = $storedBefore; InputAfterSha256 = $storedAfter; InputUnchanged = $storedBefore -ceq $storedAfter
            ActionContract = ConvertFrom-Json -InputObject ([Newtonsoft.Json.JsonConvert]::SerializeObject($prompt.ActionContract)) -AsHashtable
            UnresolvedNameTokens = [regex]::Matches($prompt.System + $providerUser, '(?i)\{\{\s*(Name|NpcName|PlayerName|farmerName)\s*\}\}').Count
        })
    }
    $report = [ordered]@{
        SchemaVersion = 1; SyntheticOnly = $true; Variant = $Variant; FixtureSha256 = (Get-FileHash -LiteralPath $FixturesPath -Algorithm SHA256).Hash
        AssemblySha256 = $assemblySha; ScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash; AssetSha256 = $assetHashes
        Scope = @('Real CurrentConversationProjector, HistorySampler, MemoryRecallService, BehaviorPromptContextBuilder, WorldEntryIndex and PromptAssembler.',
            'Full routing; non-optimized prompt assets; ApplyTranslation=true; no game content pipeline, runtime language helper, dialogue samples or portrait matching.',
            'Current conversation, archived history and durable stores are synthetic. Offered/Pending help states are not supported offline.',
            'Production fact-store normalization runs before the input hash snapshot, matching already-normalized stored facts; all subsequent selection/assembly must leave those inputs unchanged.',
            'Evaluator GroundTruth/Criteria are excluded from provider prompts. Evidence presence is a diagnostic, never a factual-accuracy pass.',
            'Production AssembledPrompt.CreateLlmRequest and LlmRequest.ConcatenatedUserContent supply actual System/User/MaxTokens; ResponseStart is excluded from provider messages.',
            'Snapshot and ParserProfile are local-only inputs for offline production reply parsing and are not additional provider messages.',
            'Character counts are UTF-16 units, not tokens. No calls, builds or deployments.')
        Cases = $rows
    }
    [void][IO.Directory]::CreateDirectory($OutputDirectory)
    [IO.File]::WriteAllText($outputPath, (ConvertTo-Json -InputObject $report -Depth 60), [Text.UTF8Encoding]::new($false))
    [ordered]@{ Status = 'Captured synthetic production prompts'; Cases = $rows.Count; AssemblySha256 = $assemblySha; OutputPath = $outputPath } | ConvertTo-Json
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
