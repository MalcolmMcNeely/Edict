#!/usr/bin/env pwsh

$inputData = [Console]::In.ReadToEnd()
$parsed = $inputData | ConvertFrom-Json -ErrorAction SilentlyContinue

$COMMAND = $null
if ($parsed) {
    $COMMAND = $parsed.tool_input.command
}

if (-not $COMMAND) {
    if ($inputData -match '"command":"([^"]*)"') {
        $COMMAND = $Matches[1]
    }
}

$DANGEROUS_PATTERNS = @(
    "git push",
    "git reset --hard",
    "git clean -fd",
    "git clean -f",
    "git branch -D",
    "git checkout \.",
    "git restore \.",
    "push --force",
    "reset --hard"
)

foreach ($pattern in $DANGEROUS_PATTERNS) {
    if ($COMMAND -match $pattern) {
        Write-Error "BLOCKED: '$COMMAND' matches dangerous pattern '$pattern'. The user has prevented you from doing this."
        exit 2
    }
}

exit 0
