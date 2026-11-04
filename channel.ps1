# Dot-sourced by build.ps1 and the installer scripts: the build channel, prod or test.
# The test branches (test, test-no-uac, test-*) build the test channel, which installs next to prod with its own exe,
# settings and autostart (see src\AlienHelper\AppId.cs); everything else builds prod. A -Channel argument wins.
if (-not $Channel) {
    $branch = git -C $PSScriptRoot rev-parse --abbrev-ref HEAD 2>$null
    $Channel = if ($branch -like 'test*') { 'test' } else { 'prod' }
}
if ($Channel -notin 'prod', 'test') { throw "unknown channel '$Channel' (prod or test)" }
$AppKey = if ($Channel -eq 'test') { 'AlienHelperTest' } else { 'AlienHelper' }
