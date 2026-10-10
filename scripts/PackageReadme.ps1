function Assert-PackageReadmeMetadata {
    param([Xml.XmlNode]$Metadata, [string]$Readme, [string]$Version)
    $required = @('description', 'tags', 'license', 'readme')
    foreach ($name in $required) {
        $node = $Metadata.SelectSingleNode("*[local-name()='$name']")
        if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
            throw "Package metadata missing: $name"
        }
    }
    if ([string]::IsNullOrWhiteSpace($Readme)) { throw 'The packaged README is empty.' }
    $forbidden = @('\bcandidates?\b', 'prepared\s+for\s+evaluation', 'until\s+publication',
        'local\s+feed', 'verified\s+pair', 'earlier\s+locally\s+built')
    foreach ($phrase in $forbidden) {
        if ($Readme -match $phrase) { throw "Packaged README contains release-process wording: $phrase" }
    }
    foreach ($match in [regex]::Matches($Readme, '\b\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z.-]+)?')) {
        if ($match.Value -ne $Version) { throw "Packaged README names $($match.Value), but this package is $Version." }
    }
    $tags = $Metadata.SelectSingleNode("*[local-name()='tags']").InnerText -split '[;\s]+'
    foreach ($tag in @('polygon', 'iou', 'overlap')) {
        if ($tag -notin $tags) { throw "Package discovery tag missing: $tag" }
    }
}
