#!/usr/bin/env pwsh
# PreToolUse hook: inject the relevant skill body as additionalContext when
# Claude is about to Edit/Write/MultiEdit a file Claude wouldn't otherwise
# reliably pull the skill in for. Multiple skills can match one file; their
# bodies stack.

param()

$ErrorActionPreference = 'Stop'

$stdin = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($stdin)) { exit 0 }

$payload = $stdin | ConvertFrom-Json
$filePath = $payload.tool_input.file_path
if ([string]::IsNullOrWhiteSpace($filePath)) { exit 0 }

$projectDir = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }
$skillsDir = Join-Path $projectDir '.claude/skills'

$leaf = Split-Path $filePath -Leaf
$segments = $filePath -split '[\\/]'

function Test-PathHasSegmentLike($pattern) {
    foreach ($seg in $segments) {
        if ($seg -like $pattern) { return $true }
    }
    return $false
}

function Test-PathHasSegmentIn($names) {
    foreach ($seg in $segments) {
        if ($names -contains $seg) { return $true }
    }
    return $false
}

$isCs    = $leaf -like '*.cs'
$isRazor = $leaf -like '*.razor'
$isTest  = Test-PathHasSegmentLike '*.Tests'

$frameworkRoots = @(
    'Edict.Core', 'Edict.Azure', 'Edict.Contracts',
    'Edict.Substrate', 'Edict.Substrate.Azurite', 'Edict.Substrate.Kafka',
    'Edict.Kafka', 'Edict.Postgres'
)
$isFrameworkCode = $isCs -and -not $isTest -and (Test-PathHasSegmentIn $frameworkRoots)

$matched = New-Object System.Collections.Generic.List[string]
if ($isCs)              { $matched.Add('csharp')         | Out-Null }
if ($isRazor)           { $matched.Add('blazor')         | Out-Null }
if ($isCs -and $isTest) { $matched.Add('testing')        | Out-Null }
if ($isFrameworkCode)   { $matched.Add('surface-config') | Out-Null }

if ($matched.Count -eq 0) { exit 0 }

$bodies = New-Object System.Collections.Generic.List[string]
foreach ($skill in $matched) {
    $skillFile = Join-Path $skillsDir "$skill/SKILL.md"
    if (Test-Path -LiteralPath $skillFile) {
        $bodies.Add((Get-Content -LiteralPath $skillFile -Raw)) | Out-Null
    }
}
if ($bodies.Count -eq 0) { exit 0 }

$context = ($bodies -join "`n`n---`n`n")

$output = [pscustomobject]@{
    hookSpecificOutput = [pscustomobject]@{
        hookEventName     = 'PreToolUse'
        additionalContext = $context
    }
}
$output | ConvertTo-Json -Depth 5 -Compress
