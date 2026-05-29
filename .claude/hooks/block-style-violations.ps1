#!/usr/bin/env pwsh
# PostToolUse hook: hard-block Edit/Write/MultiEdit calls whose new content
# ships known abbreviation violations called out in csharp/SKILL.md.
#
# We check the new_string (Edit/MultiEdit) or the full content (Write),
# never the wider file — so a small edit on a file that already has legacy
# violations elsewhere doesn't get blocked.
#
# The rule list is deliberately short. Add patterns by appending to
# $violations below. Each pattern must be a regex that matches the
# offender as the new_string text would contain it.

param()

$ErrorActionPreference = 'Stop'

$stdin = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($stdin)) { exit 0 }

$payload = $stdin | ConvertFrom-Json
$toolName = $payload.tool_name
$filePath = $payload.tool_input.file_path
if ([string]::IsNullOrWhiteSpace($filePath)) { exit 0 }
if (-not ($filePath -like '*.cs')) { exit 0 }

$candidates = New-Object System.Collections.Generic.List[string]
switch ($toolName) {
    'Edit' {
        if ($payload.tool_input.new_string) {
            $candidates.Add([string]$payload.tool_input.new_string) | Out-Null
        }
    }
    'Write' {
        if ($payload.tool_input.content) {
            $candidates.Add([string]$payload.tool_input.content) | Out-Null
        }
    }
    'MultiEdit' {
        foreach ($edit in $payload.tool_input.edits) {
            if ($edit.new_string) {
                $candidates.Add([string]$edit.new_string) | Out-Null
            }
        }
    }
    default { exit 0 }
}
if ($candidates.Count -eq 0) { exit 0 }

$violations = @(
    @{ pattern = '\bCancellationToken\s+ct\b';   message = "abbreviated parameter 'ct' for CancellationToken — use 'cancellationToken'" }
    @{ pattern = '\bIServiceProvider\s+sp\b';    message = "abbreviated parameter 'sp' for IServiceProvider — use 'serviceProvider'" }
)

$hits = New-Object System.Collections.Generic.List[string]
foreach ($text in $candidates) {
    foreach ($rule in $violations) {
        $matches = [regex]::Matches($text, $rule.pattern)
        if ($matches.Count -gt 0) {
            $hits.Add("- " + $rule.message + " (matched " + $matches.Count + "x)") | Out-Null
        }
    }
}

if ($hits.Count -eq 0) { exit 0 }

$reason = "Style violations in this edit to ${filePath}:`n" + ($hits -join "`n") + "`n`nFix the identifiers and retry. See CLAUDE.md > Conventions and the csharp skill for the full rule."

$output = [pscustomobject]@{
    decision = 'block'
    reason   = $reason
}
$output | ConvertTo-Json -Depth 5 -Compress
