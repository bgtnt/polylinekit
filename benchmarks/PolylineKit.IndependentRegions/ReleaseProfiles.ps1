function Get-IndependentReleaseProfile {
    param([ValidateSet('alpha.2', 'alpha.3')][string]$Release = 'alpha.2')
    $profiles = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-profiles.json') -Raw | ConvertFrom-Json -AsHashtable
    $profile = $profiles[$Release]
    if ($null -eq $profile -or $profile.Name -cne $Release -or $profile.Version -cne "0.1.0-$Release" -or
        $profile.SourceRevision -cnotmatch '^[0-9a-f]{40}$' -or $profile.CoreNet10Sha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $profile.LockFile -cnotin @('packages.lock.json', 'packages-alpha.3.lock.json')) {
        throw 'The published release profile is missing or malformed.'
    }
    return $profile
}
