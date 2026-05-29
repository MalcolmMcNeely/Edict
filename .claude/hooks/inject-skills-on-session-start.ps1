#!/usr/bin/env pwsh
# SessionStart hook: inject the project's coding-convention skill bodies as
# additionalContext, so the rules are in Claude's context from turn 1 —
# before any Edit/Write/MultiEdit is composed.
#
# Why this lives on SessionStart, not PreToolUse: PreToolUse additionalContext
# is documented to appear "next to the tool result", i.e. after the tool
# executes. By then the new_string is already chosen and the skill can't
# influence the in-flight write. SessionStart fires once, before any
# response, so the skill body is present before any code is generated.
#
# We inject unconditionally — this is a solo, single-repo workflow where work
# happens directly on main, so git-status-based scoping would mostly miss.

param()

$ErrorActionPreference = 'Stop'

$projectDir = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }
$skillsDir = Join-Path $projectDir '.claude/skills'

$skills = @('csharp', 'blazor', 'testing', 'surface-config')

$bodies = New-Object System.Collections.Generic.List[string]
foreach ($skill in $skills) {
    $skillFile = Join-Path $skillsDir "$skill/SKILL.md"
    if (Test-Path -LiteralPath $skillFile) {
        $body = Get-Content -LiteralPath $skillFile -Raw
        $bodies.Add("# Skill body auto-injected for this session: $skill`n`n$body") | Out-Null
    }
}
if ($bodies.Count -eq 0) { exit 0 }

$context = ($bodies -join "`n`n---`n`n")

$output = [pscustomobject]@{
    hookSpecificOutput = [pscustomobject]@{
        hookEventName     = 'SessionStart'
        additionalContext = $context
    }
}
$output | ConvertTo-Json -Depth 5 -Compress
