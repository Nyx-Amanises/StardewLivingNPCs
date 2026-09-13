#requires -Version 7.2
<#
.SYNOPSIS
Compare complete prompt assembly from two built LivingNPCs versions without model calls.
.DESCRIPTION
Each version runs in a separate process, consumes the same synthetic fixture, and loads its
own repository prompt rules. The fixture's biography, world facts, state, memory, dialogue,
history and player input are identical. No game, configuration file, save or provider is opened.
Full-builder cases use production recall and BehaviorPromptContextBuilder. Capability cases
use production fragments and BuildHelpRequestContextLines with a captured synthetic item-fit
label, avoiding HelpRequestAdvisor's live-world dependency. Delivery cases call the real
runtime hand-in renderer. These paths are reported separately and never silently pooled.
Every case uses the real PromptAssembler. TotalCharacters counts one system plus one user
content string, not the three diagnostic export views, JSON wire overhead, tokens or latency.
Raw synthetic inputs and outputs are restricted to Temp. The benchmark report contains only
scenario IDs, counts, hashes, preservation checks and source/measurement metadata.
.EXAMPLE
./tools/measure_prompt_compaction.ps1 -BaselineRepositoryRoot C:/Temp/baseline -BaselineOnly
.EXAMPLE
./tools/measure_prompt_compaction.ps1 -BaselineRepositoryRoot C:/Temp/baseline
.EXAMPLE
./tools/measure_prompt_compaction.ps1 -BaselineRepositoryRoot C:/Temp/baseline -OutputDirectory C:/Temp/prior-measurement -ReuseBaseline
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BaselineRepositoryRoot,
    [string] $CurrentRepositoryRoot = '',
    [string] $BaselineAssemblyPath = '',
    [string] $CurrentAssemblyPath = '',
    [string] $OutputDirectory = '',
    [string] $ReportPath = '',
    [switch] $BaselineOnly,
    [switch] $ReuseBaseline,
    [Parameter(DontShow)] [ValidateSet('driver', 'baseline', 'current')] [string] $WorkerMode = 'driver'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$nl = [Environment]::NewLine
if ([string]::IsNullOrWhiteSpace($CurrentRepositoryRoot)) { $CurrentRepositoryRoot = Split-Path $PSScriptRoot -Parent }
$BaselineRepositoryRoot = [IO.Path]::GetFullPath($BaselineRepositoryRoot)
$CurrentRepositoryRoot = [IO.Path]::GetFullPath($CurrentRepositoryRoot)
if ([string]::IsNullOrWhiteSpace($BaselineAssemblyPath)) { $BaselineAssemblyPath = Join-Path $BaselineRepositoryRoot 'LivingNPCs/bin/Release/net6.0/LivingNPCs.dll' }
if ([string]::IsNullOrWhiteSpace($CurrentAssemblyPath)) { $CurrentAssemblyPath = Join-Path $CurrentRepositoryRoot 'LivingNPCs/bin/Release/net6.0/LivingNPCs.dll' }
$BaselineAssemblyPath = [IO.Path]::GetFullPath($BaselineAssemblyPath)
$CurrentAssemblyPath = [IO.Path]::GetFullPath($CurrentAssemblyPath)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('livingnpcs-prompt-compaction-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $OutputDirectory.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Raw fixture/output directory must be a child of the system Temp directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($ReportPath)) { $ReportPath = Join-Path $CurrentRepositoryRoot 'docs/benchmarks/prompt-compaction-20260913.json' }
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
$fixturePath = Join-Path $OutputDirectory 'fixture.json'
$storeNames = @('LongTermMemories', 'PlayerPreferenceMemories', 'CommunityImpressions', 'SharedExperiences', 'DialogueBehaviorInfluences', 'HelpRequests', 'Conflicts')
$familyNames = @('IncludeTravel', 'IncludeGifts', 'IncludeNewHelp', 'IncludeHelpUpdates', 'IncludeMoney', 'IncludeFestival')

function Write-JsonFile([string] $Path, [object] $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 70), [Text.UTF8Encoding]::new($false))
}
function Get-FileSha([string] $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Get-TextSha([string] $Text) { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))) }
function Copy-Data([object] $Value) { return ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Value -Depth 60 -Compress) -AsHashtable }

function New-Fixture {
    $baseState = [ordered]@{
        NpcName = 'Penny'; RelationshipTrust = 80; Familiarity = 55; InteractionComfortTier = 'Trusted'
        Mood = 'Calm'; Attention = 60; Openness = 55; CurrentEmotion = 'Calm'; CurrentInclination = 'chat'
        LastFriendshipHearts = 6; InteractionRhythm = 'DailyRoutine'; ConversationsToday = 1
        ConsecutiveConversationDays = 3; LastInteraction = '昨天在图书馆互相问好'
        RelationshipImpression = '她记得玩家按约定归还借来的书，信任来自这些小事。'
        LastHelpRequestTotalDays = 99
        LongTermMemories = @(
            @{ AuditId = 'memory-book'; Kind = 'fact'; Subject = 'library book'; Summary = '玩家上周按时归还了借来的红色故事书。'; Importance = 95; CreatedTotalDays = 90; LastUpdatedTotalDays = 99; TimesReinforced = 2; Tags = @('books') },
            @{ AuditId = 'memory-garden'; Kind = 'fact'; Subject = 'garden'; Summary = '玩家在农舍门边种了一小片虞美人。'; Importance = 90; CreatedTotalDays = 92; LastUpdatedTotalDays = 98; TimesReinforced = 1; Tags = @('flowers') },
            @{ AuditId = 'memory-tutor'; Kind = 'fact'; Subject = 'teaching'; Summary = '玩家替孩子们修好了教室的木凳。'; Importance = 85; CreatedTotalDays = 93; LastUpdatedTotalDays = 97; TimesReinforced = 1; Tags = @('teaching') }
        )
        PlayerPreferenceMemories = @(
            @{ AuditId = 'preference-quiet'; PreferenceKind = 'value'; Subject = 'quiet settings'; Summary = 'The farmer prefers quiet evenings and avoids crowds.'; Importance = 95; CreatedTotalDays = 90; LastUpdatedTotalDays = 99; TimesReinforced = 2; Tags = @('quiet') },
            @{ AuditId = 'preference-book'; PreferenceKind = 'liked_item_category'; Subject = 'adventure books'; Summary = '玩家喜欢有地图插图的冒险故事。'; Importance = 90; CreatedTotalDays = 91; LastUpdatedTotalDays = 98; TimesReinforced = 1; Tags = @('books') }
        )
        CommunityImpressions = @(
            @{ AuditId = 'community-pam'; SubjectNpcName = 'Pam'; SubjectDisplayName = '潘姆'; Summary = '潘姆见过玩家帮忙搬图书馆的书箱。'; Source = 'CloseCircle'; Visibility = 'Personal'; Confidence = 75; Importance = 90; TransmissionDepth = 1; HeardFromNpcName = 'Pam'; CircleKey = 'family'; CreatedTotalDays = 98; LastUpdatedTotalDays = 99; ExpiresTotalDays = 105; TimesReinforced = 1 }
        )
        SharedExperiences = @(
            @{ AuditId = 'shared-reading'; Key = 'shared-reading'; Type = 'conversation'; Summary = '昨天潘妮和玩家一起给孩子们挑选了两本故事书。'; LocationName = 'Museum'; LocationLabel = '图书馆'; Importance = 90; CreatedTotalDays = 99; LastUpdatedTotalDays = 99; FollowUpEligibleTotalDays = -1; FollowUpShownTotalDays = 99; TimesReinforced = 1 }
        )
        DialogueBehaviorInfluences = @()
        HelpRequests = @(
            @{ AuditId = 'completed-favor'; Type = 'item_request'; Summary = '昨天已经把修书用的木材送到图书馆。'; RequestedItemId = '(O)388'; RequestedItemLabel = 'Wood'; Status = 'Fulfilled'; Resolution = 'delivered'; CreatedTotalDays = 96; LastUpdatedTotalDays = 99; DueTotalDays = 100; FulfilledTotalDays = 99; LastMentionedTotalDays = 99; FollowUpPotential = 'none'; TimesReinforced = 1 }
        )
        Conflicts = @()
    }
    $world = [ordered]@{
        LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'; Season = 'winter'; DayOfMonth = 17; TimeOfDay = 1200
        FriendshipHearts = 6; NearbyNpcNames = @(); PromptLabel = 'quiet afternoon in Pelican Town'
        DebugLabel = 'synthetic town'; Reason = 'synthetic scene'; ApproachModifier = 0; EmoteModifier = 0
        StateInfluence = @{ Mood = ''; Inclination = ''; Priority = 0; AttentionDelta = 0; OpennessDelta = 0; Reason = ''; DebugLabel = '' }
        Progression = @{
            Year = 1; Route = 'undecided'; ResidentStage = 'first_year_settling_in'; ProfessionFocuses = @(); FarmScale = 'small'; SpouseNames = @()
            ModProgression = @{ Sve = @{ Installed = $false }; PromptLabel = 'no installed expansion progression'; DebugLabel = '无扩展进度'; ReplyGuidance = 'no expansion guidance' }
            PromptLabel = 'ordinary first-year town progression'; DebugLabel = '普通第一年进度'; ReplyGuidance = 'keep requests modest'
        }
        ProgressionKnowledge = @{
            ObservationDomains = @(); LearnedDomains = @(); AttitudeTraits = @(); KnownProfessionFocuses = @()
            ModProgression = @{ PromptLabel = 'no expansion knowledge'; DebugLabel = '无扩展认知'; ReplyGuidance = 'no expansion guidance' }
            KnowsFarmScale = $false; TrustedRelationship = $true; PromptLabel = 'knows ordinary early town context'; DebugLabel = '普通城镇背景'; ReplyGuidance = 'no special guidance'
        }
    }
    $snapshot = [ordered]@{
        Year = 1; SeasonIndex = 3; SeasonName = 'winter'; DayOfMonth = 17; TimeOfDay = 1200
        FarmerName = '小林'; FarmerMoney = 6500; FarmerIsMale = $true; FriendshipPoints = 1500
        LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'; NpcIsDatable = $true; NpcIsAdult = $true
        HomeLocationName = 'Trailer'; ScheduleAvailability = 'Available'; CurrentActivity = 'taking a short break'
        NextScheduleLocation = 'Museum'; MinutesUntilNextSchedule = 60; KentAbsent = $true
    }
    $pending = [ordered]@{
        AuditId = 'pending-favor'; Type = 'item_request'; Summary = '先带石英，再带糖；两步都是已经约定的教学材料。'
        RequestedItemId = '(O)80'; RequestedItemLabel = 'Quartz'; Status = 'Pending'; Resolution = ''
        CreatedTotalDays = 99; LastUpdatedTotalDays = 100; DueTotalDays = 102; LastMentionedTotalDays = 99; TimesReinforced = 1
        RewardMoney = 120; RewardMoneyClaimQueued = $false; RewardMoneyGranted = $false; CurrentStepIndex = 0
        Steps = @(
            @{ Type = 'item_request'; Summary = '第一步带一块石英作为教学样本。'; RequestedItemId = '(O)80'; RequestedItemLabel = 'Quartz'; Status = 'Pending' },
            @{ Type = 'item_request'; Summary = '第二步带一份糖用于课后点心。'; RequestedItemId = '(O)245'; RequestedItemLabel = 'Sugar'; Status = 'Pending' }
        )
    }
    $definitions = @(
        @{ Id = 'ordinary-chat'; Path = 'full-builder'; Player = '今天的教学顺利吗？' },
        @{ Id = 'active-agreement'; Path = 'full-builder'; Player = '傍晚的约定还是原来的时间，对吗？' },
        @{ Id = 'refusal-boundary'; Path = 'full-builder'; Player = '我知道你现在不愿出门，我会尊重你的决定。' },
        @{ Id = 'travel-in-progress'; Path = 'full-builder'; Player = '路边的树在冬天也很好看。' },
        @{ Id = 'existing-help-no-new'; Path = 'captured-help-capability'; Player = '上次已经答应的材料还需要哪一种？' },
        @{ Id = 'new-help-allowed'; Path = 'captured-help-capability'; Player = '今天有什么需要我帮忙的？' },
        @{ Id = 'multi-step-existing-help'; Path = 'captured-help-capability'; Player = '第一份已经送到，接下来还需要什么？' },
        @{ Id = 'gift-handin-complete'; Path = 'runtime-gift-handin'; Player = '约好的石英带来了。' },
        @{ Id = 'gift-handin-next-step'; Path = 'runtime-gift-handin'; Player = '这是第一份材料。' },
        @{ Id = 'immediate-handin'; Path = 'runtime-immediate-handin'; Player = '约好的材料已经交给你了。' }
    )
    $scenarios = foreach ($definition in $definitions) {
        $state = Copy-Data $baseState
        $sceneSnapshot = Copy-Data $snapshot
        $required = [Collections.Generic.List[object]]::new()
        if ($definition.Path -eq 'full-builder') {
            $required.Add(@{ Id = 'relationship-impression'; Text = $state.RelationshipImpression })
            foreach ($fact in $state.LongTermMemories + $state.PlayerPreferenceMemories + $state.CommunityImpressions + $state.SharedExperiences) {
                $required.Add(@{ Id = $fact.AuditId; Text = $fact.Summary })
            }
        }
        if ($definition.Id -eq 'active-agreement') {
            $state.DialogueBehaviorInfluences = @(@{
                AuditId = 'agreement'; Type = 'visit_location'; Summary = '已经约好今天傍晚在图书馆门口见面。'; TargetLocation = 'Museum'; TargetLocationLabel = '图书馆'
                Status = 'Active'; Intensity = 75; CreatedTotalDays = 99; LastUpdatedTotalDays = 100; ExpiresTotalDays = 101; MaxTriggers = 1; TimesReinforced = 1
            })
            $required.Add(@{ Id = 'active-agreement'; Text = $state.DialogueBehaviorInfluences[0].Summary })
        }
        if ($definition.Id -eq 'refusal-boundary') {
            $state.Conflicts = @(@{
                AuditId = 'refusal'; CauseKind = 'boundary'; Summary = '潘妮明确拒绝今晚去农场；玩家必须尊重这次拒绝。'; Status = 'Active'; Severity = 65
                PeakSeverity = 65; RepairStage = 'NeedsApology'; RequiresComplexRepair = $true; CreatedTotalDays = 99; LastUpdatedTotalDays = 100
                MinimumRepairTotalDays = 102; TimesReinforced = 1
            })
            $required.Add(@{ Id = 'refusal-boundary'; Text = $state.Conflicts[0].Summary })
        }
        if ($definition.Id -in @('existing-help-no-new', 'multi-step-existing-help', 'gift-handin-complete', 'gift-handin-next-step', 'immediate-handin')) {
            $requestFact = Copy-Data $pending
            if ($definition.Id -in @('multi-step-existing-help', 'gift-handin-next-step')) {
                $requestFact.Steps[0].Status = 'Fulfilled'
                $requestFact.Steps[0].CompletedTotalDays = 100
                $requestFact.Steps[0].CompletedTimeOfDay = 1200
                $requestFact.CurrentStepIndex = 1
                $requestFact.RequestedItemId = '(O)245'
                $requestFact.RequestedItemLabel = 'Sugar'
            }
            if ($definition.Id -in @('gift-handin-complete', 'immediate-handin')) {
                $requestFact.Summary = '交付约定的石英教学样本，委托已经完成。'
                $requestFact.Status = 'Fulfilled'; $requestFact.Resolution = 'delivered'; $requestFact.Steps = @()
                $requestFact.FulfilledTotalDays = 100; $requestFact.FulfilledTimeOfDay = 1200
                $requestFact.RewardMoneyClaimQueued = $true
            }
            $state.HelpRequests = @($requestFact)
            $required.Add(@{ Id = 'request-summary'; Text = $requestFact.Summary })
            if ($definition.Id -in @('gift-handin-complete', 'immediate-handin')) { $required.Add(@{ Id = 'queued-reward'; Text = '120' }) }
            if ($definition.Id -in @('multi-step-existing-help', 'gift-handin-next-step')) {
                $required.Add(@{ Id = 'next-item'; Text = '(O)245' })
                $required.Add(@{ Id = 'next-step-summary'; Text = $requestFact.Steps[1].Summary })
            }
        }
        if ($definition.Id -eq 'new-help-allowed') { $state.LastHelpRequestTotalDays = -1; $state.HelpRequests = @() }
        if ($definition.Id -eq 'travel-in-progress') {
            $sceneSnapshot.IsTravelling = $true; $sceneSnapshot.CurrentTravelDestination = 'Farm'; $sceneSnapshot.CurrentActivity = 'walking toward the farm'
            $required.Add(@{ Id = 'outing-target'; Text = '农场' })
            $required.Add(@{ Id = 'outing-phase'; Text = 'traveling naturally toward the destination' })
        }
        [ordered]@{
            Id = $definition.Id; Path = $definition.Path; Locale = 'zh'; PlayerText = $definition.Player
            Trigger = $(if ($definition.Path -eq 'runtime-gift-handin') { 'Gift' } else { 'Conversation' })
            State = $state; World = $world; Snapshot = $sceneSnapshot; RequiredFacts = @($required)
            Gift = @{ ItemId = '(O)80'; ItemName = '石英'; TasteLabel = 'neutral'; TastePromptLabel = 'neutral'; TasteScore = 0; IsBirthdayGift = $false }
            Conversation = @(
                @{ Text = '今天有空聊一会儿吗？'; IsPlayer = $true; Id = 'prior-player' },
                @{ Text = '嗯，我能待一会儿。'; IsPlayer = $false; Id = 'prior-npc' },
                @{ Text = $definition.Player; IsPlayer = $true; Id = 'current-player' }
            )
        }
    }
    $biographyPath = Join-Path $BaselineRepositoryRoot 'LivingNPCs/assets/dialogue/bios-zh/Penny.json'
    return [ordered]@{
        SchemaVersion = 1; Locale = 'zh'; CurrentTotalDays = 100; CurrentTimeOfDay = 1200
        BiographyJson = [IO.File]::ReadAllText($biographyPath); BiographySha256 = Get-FileSha $biographyPath
        WorldCore = '合成世界事实：鹈鹕镇的图书馆向居民开放，潘妮在那里教孩子读书。山坡上的木牌写着“安静阅读角”。农场在镇子的西边，居民沿道路和地图出口步行来往。'
        WorldRetrieved = '合成检索事实：馆藏地图册必须在星期五归还；课堂的故事书收在门边的低书架。'
        HistoryLines = @(
            '昨天 12:00，玩家与潘妮在图书馆互相问好，随后各自离开。',
            '前天 13:00，玩家把借来的故事书交还给潘妮；她确认书页完整。'
        )
        RecentEntries = @(@{
            NpcName = 'Penny'; Kind = 'Conversation'; Action = 'brief greeting'; Reason = 'the farmer said hello'
            Year = 1; Season = 'winter'; Day = 16; TimeOfDay = 1200; TotalDays = 99; LocationName = 'Town'; LocationDisplayName = '鹈鹕镇'
        })
        CapturedHelpFit = 'theme books and teaching; currently reasonable item requests: Quartz (O)80, Sugar (O)245; allowed help request type: item_request only; request depth: at most two ordered steps'
        Outing = @{
            NpcName = 'Penny'; TotalDays = 100; TargetLocation = 'Farm'; TargetLocationLabel = '农场'; ActivityStyle = 'chat'; Reason = 'an agreed visit'
            PlannedStayMinutes = 60; AnchorTile = @{ X = 30; Y = 20 }; AnchorFacingDirection = 2; AnchorLabel = 'near the farmhouse'
            OriginalIgnoreScheduleToday = $false; OriginalFollowSchedule = $true; Phase = 'Traveling'
        }
        Scenarios = @($scenarios)
    }
}

function Invoke-IsolatedWorker([string] $Mode) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $PSHOME 'pwsh.exe'
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8; $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $PSCommandPath,
        '-WorkerMode', $Mode, '-BaselineRepositoryRoot', $BaselineRepositoryRoot, '-CurrentRepositoryRoot', $CurrentRepositoryRoot,
        '-BaselineAssemblyPath', $BaselineAssemblyPath, '-CurrentAssemblyPath', $CurrentAssemblyPath,
        '-OutputDirectory', $OutputDirectory, '-ReportPath', $ReportPath)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult(); $errors = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "$Mode worker failed ($($process.ExitCode)):$nl$errors$nl$output" }
        if (-not [string]::IsNullOrWhiteSpace($errors)) { Write-Warning $errors.Trim() }
    }
    finally { $process.Dispose() }
}

if ($WorkerMode -eq 'driver') {
    if ($ReuseBaseline -and $BaselineOnly) { throw 'ReuseBaseline and BaselineOnly cannot be combined.' }
    if (-not $ReuseBaseline) {
        Write-JsonFile $fixturePath (New-Fixture)
        Invoke-IsolatedWorker 'baseline'
    }
    $baseline = Get-Content -LiteralPath (Join-Path $OutputDirectory 'baseline.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($ReuseBaseline) {
        if ($baseline.FixtureSha256 -cne (Get-FileSha $fixturePath)) { throw 'Cannot reuse a baseline with different fixture bytes.' }
        if ($baseline.Assembly.Sha256 -cne (Get-FileSha $BaselineAssemblyPath)) { throw 'Cannot reuse a baseline with a different production DLL.' }
        foreach ($asset in $baseline.RuleAssets) {
            $assetPath = Join-Path (Join-Path $BaselineRepositoryRoot 'LivingNPCs/assets/dialogue') $asset.Asset
            if ($asset.Sha256 -cne (Get-FileSha $assetPath)) { throw "Cannot reuse a baseline with changed rules: $($asset.Asset)." }
        }
    }
    if ($BaselineOnly) {
        $baselineFactsPreserved = @($baseline.Scenarios | Where-Object { -not $_.Checks.AllRequiredFactsPreserved }).Count -eq 0
        [ordered]@{
            Status = 'Baseline measured; no current measurement or benchmark report written'; Scenarios = $baseline.Scenarios.Count
            AllRequiredFactsPreserved = $baselineFactsPreserved; OutputDirectory = $OutputDirectory
        } | ConvertTo-Json
        if (-not $baselineFactsPreserved) { throw 'Baseline did not retain an expected fixture fact; inspect baseline.json before comparing versions.' }
        exit 0
    }
    Invoke-IsolatedWorker 'current'
    $current = Get-Content -LiteralPath (Join-Path $OutputDirectory 'current.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($baseline.FixtureSha256 -cne $current.FixtureSha256) { throw 'Workers did not consume identical fixture bytes.' }
    $beforeById = @{}
    foreach ($row in $baseline.Scenarios) { $beforeById[$row.Id] = $row }
    $comparisons = foreach ($after in $current.Scenarios) {
        $before = $beforeById[$after.Id]
        if ($before.InputSha256 -cne $after.InputSha256) { throw "Different scenario inputs: $($after.Id)." }
        [ordered]@{
            Scenario = $after.Id; ConstructionPath = $after.ConstructionPath; Trigger = $after.Trigger
            InputSha256 = $after.InputSha256
            BaselineTotalCharacters = $before.TotalCharacters; CurrentTotalCharacters = $after.TotalCharacters
            SavedCharacters = $before.TotalCharacters - $after.TotalCharacters
            SavedPercent = [Math]::Round(100.0 * ($before.TotalCharacters - $after.TotalCharacters) / $before.TotalCharacters, 2)
            BaselineBehaviorCharacters = $before.BehaviorCharacters; CurrentBehaviorCharacters = $after.BehaviorCharacters
            BaselineCoreContractCharacters = $before.CoreContractCharacters; CurrentCoreContractCharacters = $after.CoreContractCharacters
            BaselineSceneContractCharacters = $before.SceneContractCharacters; CurrentSceneContractCharacters = $after.SceneContractCharacters
            BaselineSegments = $before.SegmentLengths; CurrentSegments = $after.SegmentLengths
            BaselineSections = $before.SectionLengths; CurrentSections = $after.SectionLengths
            BaselineActionContract = $before.ActionContract; CurrentActionContract = $after.ActionContract
            BaselineChecks = $before.Checks; CurrentChecks = $after.Checks
            BaselineRecallSelection = $before.RecallSelection; CurrentRecallSelection = $after.RecallSelection
            SameSelectedMemoryIds = (ConvertTo-Json -InputObject $before.RecallSelection -Depth 5 -Compress) -ceq (ConvertTo-Json -InputObject $after.RecallSelection -Depth 5 -Compress)
            AllRequiredFactsPreserved = $before.Checks.AllRequiredFactsPreserved -and $after.Checks.AllRequiredFactsPreserved
            SameWorldContent = $before.WorldSha256 -ceq $after.WorldSha256
            SameNpcBiography = $before.NpcBiographySha256 -ceq $after.NpcBiographySha256
            BaselinePayloadSha256 = $before.PayloadSha256; CurrentPayloadSha256 = $after.PayloadSha256
        }
    }
    $identicalAssemblies = $baseline.Assembly.Sha256 -ceq $current.Assembly.Sha256
    $scope = @(
        'Unit: UTF-16 code units (.NET String.Length), including CR/LF; not tokens, HTTP bytes, cost, or game/model latency.'
        'One actual system content plus one actual user content is counted. ResponseStart and repeated diagnostic export views are excluded.'
        'Workers byte-load separate built production DLLs in isolated PowerShell processes. No build, deploy, model, network, configuration, credential or save access occurs.'
        'Each worker merges its own default/zh repository prompt assets with production MergeLocalizedPrompts. Rule wording may differ by version.'
        'The shared fixture fixes biography, world facts, relationship/state, memories, current player text, prior dialogue and event history. All scenario input hashes must match.'
        'Full-builder cases use production MemoryRecallService and BehaviorPromptContextBuilder with 3 personal / 4 preference / 2 community recall budgets, Full context, and ConcisePromptContext=false.'
        'Captured-help-capability cases use production state fragments, BuildHelpRequestContextLines and Facts.HelpRequest. The item-fit label is an identical captured synthetic value; HelpRequestAdvisor and live WorldContext are not run. These are not full behavior-builder measurements.'
        'Gift and immediate hand-ins call the production runtime hand-in renderer with the same already-updated request facts; gameplay delivery, reward payment and inventory mutation are not executed.'
        'Every case then uses the actual PromptAssembler, without optimized prompts. Synthetic world/retrieval inputs are fixed; world retrieval, runtime portrait discovery and game dialogue-sample loading are outside scope.'
        'Fact checks are exact retention checks of selected source facts, current input, world/history and critical task/travel data. They do not establish model response quality or semantic equivalence of rewritten instructions.'
        'SectionLengths contains diagnostics: InlineMetadataCore is already part of Instructions, and CurrentConversationBeforeBudget is not an additional sent section. Disjoint segment lengths are the additive totals.'
        'Scenarios use different construction paths and are reported individually, without a pooled speedup or a prediction of typical in-game prompt size. Identical DLL hashes indicate a self-comparison only.'
        'Raw synthetic fixtures and per-version output stay under Temp; this report contains no dialogue or prompt bodies.'
    )
    $report = [ordered]@{
        SchemaVersion = 1; MeasuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Status = $(if ($identicalAssemblies) { 'self-comparison-only' } else { 'offline-character-comparison' })
        Unit = 'UTF-16 code units'; ScriptSha256 = Get-FileSha $PSCommandPath; FixtureSha256 = $current.FixtureSha256
        BaselineAssembly = $baseline.Assembly; CurrentAssembly = $current.Assembly; IdenticalAssemblies = $identicalAssemblies
        BaselineReused = $ReuseBaseline.IsPresent
        BaselineRuleAssets = $baseline.RuleAssets; CurrentRuleAssets = $current.RuleAssets
        SharedBiographySha256 = $current.SharedBiographySha256; Scope = $scope
        Reproduction = [ordered]@{
            Script = 'tools/measure_prompt_compaction.ps1'
            Arguments = @('-BaselineRepositoryRoot', $BaselineRepositoryRoot, '-CurrentRepositoryRoot', $CurrentRepositoryRoot,
                '-BaselineAssemblyPath', $BaselineAssemblyPath, '-CurrentAssemblyPath', $CurrentAssemblyPath)
            Requires = 'Prebuilt production DLLs and corresponding Release test-output dependency DLLs; PowerShell 7.2 or newer.'
            NoNetworkCalls = $true
        }
        ScenarioCount = @($comparisons).Count
        AllRequiredFactsPreserved = @($comparisons | Where-Object { -not $_.AllRequiredFactsPreserved }).Count -eq 0
        Comparisons = @($comparisons)
    }
    Write-JsonFile (Join-Path $OutputDirectory 'comparison.json') $report
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath))
    Write-JsonFile $ReportPath $report
    [ordered]@{
        Status = $report.Status; Scenarios = $report.ScenarioCount; AllRequiredFactsPreserved = $report.AllRequiredFactsPreserved
        OutputDirectory = $OutputDirectory; ReportPath = $ReportPath
    } | ConvertTo-Json
    if (-not $report.AllRequiredFactsPreserved) { throw 'A required synthetic fact was not retained; inspect the reported checks before interpreting savings.' }
    exit 0
}

# Load one production version per isolated worker, without locking any build output.
$assemblyPath = if ($WorkerMode -eq 'baseline') { $BaselineAssemblyPath } else { $CurrentAssemblyPath }
$versionRoot = if ($WorkerMode -eq 'baseline') { $BaselineRepositoryRoot } else { $CurrentRepositoryRoot }
if (-not [IO.File]::Exists($assemblyPath)) { throw "Built production DLL missing: $assemblyPath" }
$dependencyRoots = [string[]]@([IO.Path]::GetDirectoryName($assemblyPath), (Join-Path $versionRoot 'LivingNPCs.Tests/bin/Release/net6.0'))
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
$assemblyBytes = [IO.File]::ReadAllBytes($assemblyPath)
$assemblyHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($assemblyBytes))
$assembly = [Reflection.Assembly]::Load($assemblyBytes)
function Get-ProductionType([string] $Name) { return $assembly.GetType($Name, $true) }
function New-ProductionObject([string] $Name) { return [Activator]::CreateInstance((Get-ProductionType $Name), $true) }
function New-Model([string] $Name, [object] $Data) {
    return [Newtonsoft.Json.JsonConvert]::DeserializeObject((ConvertTo-Json -InputObject $Data -Depth 60 -Compress), (Get-ProductionType $Name))
}
function Set-Value([object] $Object, [string] $Name, [object] $Value) {
    $type = [object].GetMethod('GetType').Invoke($Object, @())
    $property = $type.GetProperty($Name, $flags)
    if ($null -ne $property) {
        if ($null -eq $Value) { $property.SetValue($Object, $null) }
        else { $property.SetValue($Object, [Management.Automation.LanguagePrimitives]::ConvertTo($Value, $property.PropertyType)) }
        return
    }
    $field = $type.GetField($Name, $flags)
    if ($null -eq $field) { throw "Missing member $($type.FullName).$Name" }
    $field.SetValue($Object, [Management.Automation.LanguagePrimitives]::ConvertTo($Value, $field.FieldType))
}
function Invoke-Named([Reflection.MethodInfo] $Method, [Collections.IDictionary] $Values, [object] $Target = $null) {
    $parameters = $Method.GetParameters(); $arguments = [object[]]::new($parameters.Length)
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
    return ,$Method.Invoke($Target, $arguments)
}
function Invoke-Fragment([string] $NestedType, [string] $Method, [object[]] $Arguments) {
    return (Get-ProductionType ('LivingNPCs.Behavior.PromptFragments+' + $NestedType)).GetMethod($Method, $flags).Invoke($null, $Arguments)
}
function Get-FragmentField([string] $NestedType, [string] $Field) {
    return (Get-ProductionType ('LivingNPCs.Behavior.PromptFragments+' + $NestedType)).GetField($Field, $flags).GetValue($null)
}
function New-PromptLookup([Collections.Generic.Dictionary[string,string]] $Map, [object] $Bio) {
    $replace = (Get-ProductionType 'LivingNPCs.Dialogue.Content.PromptTable').GetMethod('ReplaceTokens', $flags)
    $lookupBlock = {
        param([string] $key, [object] $tokens, [bool] $optimized)
        $baseKeys = if ($optimized) { @(($key + 'Optimized'), $key) } else { @($key) }
        foreach ($baseKey in $baseKeys) {
            if ($Bio.PromptOverrides.ContainsKey($baseKey) -and -not [string]::IsNullOrWhiteSpace($Bio.PromptOverrides[$baseKey])) {
                return [string]$replace.Invoke($null, [object[]]@($Bio.PromptOverrides[$baseKey], $tokens))
            }
            foreach ($candidate in @(($baseKey + '.FemaleNpc'), $baseKey)) {
                if ($Map.ContainsKey($candidate)) { return [string]$replace.Invoke($null, [object[]]@($Map[$candidate], $tokens)) }
            }
        }
        return $null
    }.GetNewClosure()
    return [Management.Automation.LanguagePrimitives]::ConvertTo($lookupBlock, (Get-ProductionType 'LivingNPCs.Dialogue.Engine.PromptTextLookup'))
}
function Get-SelectedIds([object[]] $Source, [object] $Selections) {
    return ,@($Selections | ForEach-Object {
        $summary = [string]$_.Memory.Summary
        $sourceMatches = @($Source | Where-Object { $_.Summary -ceq $summary })
        if ($sourceMatches.Count -ne 1) { throw 'A recalled source fact cannot be mapped uniquely to the synthetic fixture.' }
        [string]$sourceMatches[0].AuditId
    })
}

$fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json -AsHashtable
$map = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$localized = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
$ruleAssets = foreach ($relative in @('prompts/default.json', 'prompts/zh.json')) {
    $path = Join-Path (Join-Path $versionRoot 'LivingNPCs/assets/dialogue') $relative
    $raw = [IO.File]::ReadAllText($path)
    $targetMap = if ($relative -eq 'prompts/default.json') { $map } else { $localized }
    foreach ($pair in (ConvertFrom-Json -InputObject $raw -AsHashtable).GetEnumerator()) { $targetMap[$pair.Key] = [string]$pair.Value }
    [ordered]@{ Asset = $relative; Sha256 = Get-FileSha $path; Characters = $raw.Length }
}
[void](Get-ProductionType 'LivingNPCs.Dialogue.Content.DialogueContentSetup').GetMethod('MergeLocalizedPrompts', $flags).Invoke($null, [object[]]@($map, $localized))
$bio = New-Model 'LivingNPCs.Dialogue.Content.NpcBio' (ConvertFrom-Json -InputObject $fixture.BiographyJson -AsHashtable)
[void]$bio.GetType().GetMethod('NormalizeNullFields').Invoke($bio, @())
$config = (Get-ProductionType 'LivingNPCs.ModEntry').GetProperty('ActiveConfig', $flags).GetValue($null)
Set-Value $config 'ConcisePromptContext' $false
$builderType = Get-ProductionType 'LivingNPCs.Behavior.BehaviorPromptContextBuilder'
$recallType = Get-ProductionType 'LivingNPCs.Behavior.MemoryRecallService'
$assemblerType = Get-ProductionType 'LivingNPCs.Dialogue.Engine.PromptAssembler'
$turnType = Get-ProductionType 'LivingNPCs.Dialogue.ConversationTurn'
$turnConstructor = $turnType.GetConstructor([Type[]]@([string], [bool], [string]))
$coreMethod = (Get-ProductionType 'LivingNPCs.Dialogue.Engine.LivingNpcMetadataContract').GetMethod('BuildInlineCoreInstructions', $flags)
$sceneMethod = (Get-ProductionType 'LivingNPCs.Dialogue.Engine.LivingNpcMetadataContract').GetMethod('BuildSceneInstructions', $flags)
$coreContract = [string]$coreMethod.Invoke($null, @())
$rows = [Collections.Generic.List[object]]::new()

foreach ($scenario in $fixture.Scenarios) {
    $state = New-Model 'LivingNPCs.Behavior.LivingNpcState' $scenario.State
    $world = New-Model 'LivingNPCs.Behavior.WorldContextSnapshot' $scenario.World
    $snapshot = New-Model 'LivingNPCs.Dialogue.Engine.GameStateSnapshot' $scenario.Snapshot
    $npc = [Activator]::CreateInstance($builderType.GetMethod('BuildPromptContext', $flags).GetParameters()[0].ParameterType, $true)
    Set-Value $npc 'Name' 'Penny'; Set-Value $npc 'displayName' '潘妮'
    $behaviorParts = [Collections.Generic.List[string]]::new()
    $recallSelection = [ordered]@{}
    if ($scenario.Path -eq 'full-builder') {
        $recent = [Newtonsoft.Json.JsonConvert]::DeserializeObject(
            (ConvertTo-Json -InputObject $fixture.RecentEntries -Depth 20 -Compress),
            (Get-ProductionType 'LivingNPCs.Behavior.BehaviorMemoryEntry').MakeArrayType())
        $recall = Invoke-Named ($recallType.GetMethod('BuildPlan', $flags)) @{
            state = $state; world = $world; recentEntries = $recent; longTermCount = 3; preferenceCount = 4; currentTotalDays = 100; currentPlayerText = $scenario.PlayerText
        }
        $community = Invoke-Named ($recallType.GetMethod('BuildCommunityImpressionPlan', $flags)) @{
            state = $state; maxCount = 2; currentTotalDays = 100; currentPlayerText = $scenario.PlayerText
        }
        $recallSelection.LongTerm = Get-SelectedIds $scenario.State.LongTermMemories $recall.LongTermMemories
        $recallSelection.Preferences = Get-SelectedIds $scenario.State.PlayerPreferenceMemories $recall.PlayerPreferences
        $recallSelection.Community = Get-SelectedIds $scenario.State.CommunityImpressions $community
        if ($recallSelection.LongTerm.Count -gt 3 -or $recallSelection.Preferences.Count -gt 4 -or $recallSelection.Community.Count -gt 2) {
            throw 'Production recall exceeded the explicit synthetic fixture budgets.'
        }
        $disposition = New-Model 'LivingNPCs.Behavior.NpcDispositionProfile' @{
            PromptLabel = 'careful temperament'; DebugLabel = 'synthetic'; ApproachModifier = 0; EmoteModifier = 0; PassiveEmoteId = 16
            Reason = 'synthetic profile'; SourceLabel = 'synthetic profile'; SourceDebugLabel = '合成资料'
            BackgroundPrompt = 'a thoughtful tutor who cares about her neighbors'; DialoguePrompt = 'gentle phrasing'
        }
        $expression = New-Model 'LivingNPCs.Behavior.EmotionalExpressionCue' @{
            Key = 'synthetic'; PromptLabel = 'soft-spoken style'; DebugLabel = 'synthetic'; ConflictPromptLabel = 'state boundaries gently'
            RepairPromptLabel = 'look for a specific repair'; ReplyGuidance = 'speak gently and concretely'; Directness = 50; MemoryHold = 50
            EmotionDecayMultiplier = 1; ConflictDecayMultiplier = 1; RepairResponsivenessMultiplier = 1; ComplexRepairDelayAdjustmentDays = 0
        }
        $behaviorParts.Add([string](Invoke-Named ($builderType.GetMethod('BuildPromptContext', $flags)) @{
            npc = $npc; recentEntries = $recent; state = $state; world = $world; disposition = $disposition; emotionalStyle = $expression
            recallPlan = $recall; communityImpressions = $community; maxPendingHelpRequestsPerNpc = 1; helpRequestCooldownDays = 3
            currentTotalDays = 100; currentTimeOfDay = 1200; currentPlayerText = $scenario.PlayerText; markFollowUpCues = $false
        }))
        $behaviorParts.Add([string](Invoke-Fragment 'GiftOpportunity' 'NoOpportunitySection' @()))
        if ($scenario.Id -eq 'travel-in-progress') {
            $outing = New-Model 'LivingNPCs.Behavior.PendingCompanionOuting' $fixture.Outing
            $behaviorParts.Add([string](Invoke-Fragment 'Outing' 'Section' @('潘妮', $outing, $true)))
        }
    }
    elseif ($scenario.Path -eq 'captured-help-capability') {
        $behaviorParts.Add([string](Invoke-Fragment 'Context' 'Header' @('潘妮')))
        $behaviorParts.Add([string](Get-FragmentField 'Context' 'Purpose'))
        $behaviorParts.Add([string](Get-FragmentField 'Context' 'CurrentStateHeading'))
        $behaviorParts.Add([string](Invoke-Fragment 'Context' 'SceneLine' @($world)))
        $behaviorParts.Add([string](Invoke-Fragment 'Context' 'MoodLine' @($state)))
        $behaviorParts.Add([string](Invoke-Fragment 'Context' 'TrustLine' @($state)))
        $behaviorParts.Add([string](Invoke-Fragment 'Context' 'RelationshipImpressionLine' @($state.RelationshipImpression)))
        $readiness = Invoke-Named ((Get-ProductionType 'LivingNPCs.Behavior.HelpRequestReadinessRules').GetMethod('Evaluate', $flags)) @{
            state = $state; friendshipHearts = 6; maxPendingHelpRequestsPerNpc = 1; helpRequestCooldownDays = 3; currentTotalDays = 100
        }
        $fitText = [string]$fixture.CapturedHelpFit
        $fitBlock = { return $fitText }.GetNewClosure()
        $fitFactory = [Func[string]]$fitBlock
        $helpLines = Invoke-Named ($builderType.GetMethod('BuildHelpRequestContextLines', $flags)) @{
            state = $state; readiness = $readiness; buildFitLabel = $fitFactory
        }
        foreach ($line in $helpLines) { $behaviorParts.Add([string]$line) }
        foreach ($fact in $state.HelpRequests) { $behaviorParts.Add([string](Invoke-Fragment 'Context' 'HelpRequestsLine' @((Invoke-Fragment 'Facts' 'HelpRequest' @($fact, 100))))) }
        if ($readiness.Allowed) { $behaviorParts.Add([string](Invoke-Fragment 'HelpRequestOpportunity' 'Section' @('潘妮'))) }
        $behaviorParts.Add([string](Invoke-Fragment 'GiftOpportunity' 'NoOpportunitySection' @()))
    }
    else {
        $gift = New-Model 'LivingNPCs.Behavior.GiftMemoryDetails' $scenario.Gift
        $facts = [Newtonsoft.Json.JsonConvert]::DeserializeObject(
            (ConvertTo-Json -InputObject $scenario.State.HelpRequests -Depth 25 -Compress),
            (Get-ProductionType 'LivingNPCs.Behavior.NpcHelpRequestFact').MakeArrayType())
        if ($scenario.Path -eq 'runtime-gift-handin') {
            $method = (Get-ProductionType 'LivingNPCs.Behavior.ValleyTalkContextService').GetMethod('BuildHelpRequestGiftResponsePrompt', $flags)
            $behaviorParts.Add([string](Invoke-Named $method @{ npc = $npc; gift = $gift; deliveredHelpRequests = $facts }))
        }
        else {
            $method = (Get-ProductionType 'LivingNPCs.Behavior.ConversationStartRecorder').GetMethod('BuildHelpRequestDeliveryPrompt', $flags)
            $behaviorParts.Add([string](Invoke-Named $method @{ npc = $npc; gift = $gift; changedHelpRequests = $facts }))
        }
    }
    $behavior = [string]::Join($nl, $behaviorParts)
    $turns = [Array]::CreateInstance($turnType, $scenario.Conversation.Count)
    for ($index = 0; $index -lt $scenario.Conversation.Count; $index++) {
        $turn = $scenario.Conversation[$index]
        $turns.SetValue($turnConstructor.Invoke([object[]]@([string]$turn.Text, [bool]$turn.IsPlayer, [string]$turn.Id)), $index)
    }
    $request = New-ProductionObject 'LivingNPCs.Dialogue.GenerationRequest'
    foreach ($pair in ([ordered]@{
        NpcName = 'Penny'; NpcDisplayName = '潘妮'; Trigger = $scenario.Trigger; CurrentPlayerText = $scenario.PlayerText
        BehaviorContext = $behavior; Snapshot = $snapshot; Conversation = $turns
        GiftItemId = $(if ($scenario.Trigger -eq 'Gift') { $scenario.Gift.ItemId } else { '' }); GiftTaste = 0
    }).GetEnumerator()) { Set-Value $request $pair.Key $pair.Value }
    $routing = (Get-ProductionType 'LivingNPCs.Dialogue.Engine.ContextRoutingPlan').GetMethod('Full', $flags).Invoke($null, @())
    $inputObject = New-ProductionObject 'LivingNPCs.Dialogue.Engine.PromptAssemblyInput'
    foreach ($pair in ([ordered]@{
        Request = $request; Plan = $routing; Bio = $bio; NpcName = 'Penny'; NpcDisplayName = '潘妮'; NpcGender = 'Female'
        Conversation = $turns; HistoryLines = [string[]]$fixture.HistoryLines; Locale = 'zh'; UseOptimizedPrompts = $false
        WorldSummaryFull = $fixture.WorldCore; WorldSummaryBrief = $fixture.WorldCore; RetrievedWorldContext = $fixture.WorldRetrieved
        GiftDisplayName = $(if ($scenario.Trigger -eq 'Gift') { '石英' } else { '' }); Lookup = New-PromptLookup $map $bio
    }).GetEnumerator()) { Set-Value $inputObject $pair.Key $pair.Value }
    $assembler = $assemblerType.GetConstructors()[0].Invoke([object[]]@($inputObject))
    $prompt = $assemblerType.GetMethod('Assemble').Invoke($assembler, @())
    $segments = [ordered]@{}; $lengths = [ordered]@{}
    foreach ($name in @('System', 'GameConstantContext', 'NpcConstantContext', 'CorePrompt', 'Instructions', 'Command', 'ResponseStart')) {
        $segments[$name] = [string]$prompt.GetType().GetProperty($name).GetValue($prompt)
        $lengths[$name] = $segments[$name].Length
    }
    $providerUser = $segments.GameConstantContext + [string]$prompt.CacheableNpcContext + [string]$prompt.Tail
    $total = [int]$prompt.TotalCharacters
    if ($total -ne ($segments.System.Length + $providerUser.Length)) { throw "Actual provider-content total mismatch: $($scenario.Id)." }
    if ($lengths.System -eq 0 -or $lengths.Command -eq 0) { throw "Empty required rule segment: $($scenario.Id)." }
    $sections = [ordered]@{}
    foreach ($pair in $prompt.SectionLengths.GetEnumerator()) { $sections[$pair.Key] = $pair.Value }
    $plan = [ordered]@{}
    foreach ($name in $familyNames + @('IsFallback', 'Reason')) { $plan[$name] = $prompt.ActionContract.GetType().GetProperty($name).GetValue($prompt.ActionContract) }
    $sceneContract = [string]$sceneMethod.Invoke($null, [object[]]@($prompt.ActionContract))
    $factChecks = [ordered]@{}
    foreach ($fact in $scenario.RequiredFacts) {
        # A fact must survive both behavior construction and final assembly. Its appearance in
        # player input or an unrelated schema example alone cannot satisfy this retention check.
        $factChecks[$fact.Id] = $behavior.Contains([string]$fact.Text, [StringComparison]::Ordinal) -and $providerUser.Contains([string]$fact.Text, [StringComparison]::Ordinal)
    }
    $factChecks['current-player-input'] = $providerUser.Contains([string]$scenario.PlayerText, [StringComparison]::Ordinal)
    $factChecks['world-core'] = $providerUser.Contains([string]$fixture.WorldCore, [StringComparison]::Ordinal)
    $factChecks['world-retrieved'] = $providerUser.Contains([string]$fixture.WorldRetrieved, [StringComparison]::Ordinal)
    $factChecks['event-history'] = @($fixture.HistoryLines | Where-Object { -not $providerUser.Contains([string]$_, [StringComparison]::Ordinal) }).Count -eq 0
    $normalizedUser = [regex]::Replace($providerUser, '\s+', ' ').Trim()
    $normalizedBiography = [regex]::Replace([string]$bio.Biography, '\s+', ' ').Trim()
    $factChecks['biography-body'] = $normalizedBiography.Length -gt 0 -and $normalizedUser.Contains($normalizedBiography, [StringComparison]::Ordinal)
    $missingFacts = @($factChecks.Keys | Where-Object { -not $factChecks[$_] })
    $stateCounts = [ordered]@{}
    foreach ($name in $storeNames) { $stateCounts[$name] = $scenario.State[$name].Count }
    $rawPayload = [ordered]@{ system = $segments.System; user = $providerUser }
    Write-JsonFile (Join-Path $OutputDirectory "$WorkerMode-$($scenario.Id)-request.json") $rawPayload
    [IO.File]::WriteAllText((Join-Path $OutputDirectory "$WorkerMode-$($scenario.Id)-behavior.txt"), $behavior, [Text.UTF8Encoding]::new($false))
    $rows.Add([ordered]@{
        Id = $scenario.Id; ConstructionPath = $scenario.Path; Trigger = $scenario.Trigger
        InputSha256 = Get-TextSha (ConvertTo-Json -InputObject $scenario -Depth 60 -Compress)
        TotalCharacters = $total; BehaviorCharacters = $behavior.Length
        CoreContractCharacters = $coreContract.Length; SceneContractCharacters = $sceneContract.Length
        SegmentLengths = $lengths; SectionLengths = $sections; ActionContract = $plan; RecallSelection = $recallSelection
        Checks = [ordered]@{ InputStoreCounts = $stateCounts; Facts = $factChecks; MissingFacts = $missingFacts; AllRequiredFactsPreserved = $missingFacts.Count -eq 0 }
        WorldSha256 = Get-TextSha ($fixture.WorldCore + $fixture.WorldRetrieved)
        NpcBiographySha256 = Get-TextSha $segments.NpcConstantContext
        PayloadSha256 = Get-TextSha (ConvertTo-Json -InputObject $rawPayload -Compress)
    })
}
Write-JsonFile (Join-Path $OutputDirectory "$WorkerMode.json") ([ordered]@{
    Assembly = [ordered]@{ Path = $assemblyPath; Sha256 = $assemblyHash; Version = $assembly.GetName().Version.ToString() }
    RuleAssets = @($ruleAssets); FixtureSha256 = Get-FileSha $fixturePath; SharedBiographySha256 = $fixture.BiographySha256
    Scenarios = @($rows)
})
