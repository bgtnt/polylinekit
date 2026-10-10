$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageReadme.ps1')
$version = '0.1.0-alpha.2'
[xml]$valid = '<metadata><description>Numeric overlap.</description><tags>polygon iou overlap</tags><license type="expression">MIT</license><readme>README.md</readme></metadata>'
$text = "Install PolylineKit.Core --prerelease. Version $version."
Assert-PackageReadmeMetadata -Metadata $valid.DocumentElement -Readme $text -Version $version
$passed = 1
function Reject([Xml.XmlNode]$Metadata, [string]$Readme, [string]$Reason) {
    $rejected = $false
    try { Assert-PackageReadmeMetadata -Metadata $Metadata -Readme $Readme -Version $version }
    catch { $rejected = $true }
    if (!$rejected) { throw "Package README guard accepted $Reason." }
    $script:passed++
}
foreach ($phrase in @('Candidate', 'candidates', "prepared`nfor evaluation", 'Until publication on NuGet.org', 'local feed', 'verified pair', 'earlier locally built')) {
    Reject $valid.DocumentElement ($text + ' ' + $phrase) $phrase
}
Reject $valid.DocumentElement 'See https://www.nuget.org/packages/PolylineKit.Core/0.1.0-alpha.1' 'an old version in a link'
Reject $valid.DocumentElement '' 'empty README'
foreach ($name in @('description', 'tags', 'license', 'readme')) {
    [xml]$missing = $valid.OuterXml
    [void]$missing.DocumentElement.RemoveChild($missing.DocumentElement.SelectSingleNode($name))
    Reject $missing.DocumentElement $text "missing $name"
    [xml]$empty = $valid.OuterXml
    $empty.DocumentElement.SelectSingleNode($name).InnerText = ' '
    Reject $empty.DocumentElement $text "empty $name"
}
foreach ($name in @('polygon', 'iou', 'overlap')) {
    [xml]$missingTag = $valid.OuterXml
    $missingTag.DocumentElement.tags = ($missingTag.DocumentElement.tags -split ' ' | Where-Object { $_ -ne $name }) -join ' '
    Reject $missingTag.DocumentElement $text "missing $name tag"
}
Write-Output "PASS: $passed package README/metadata controls, including rejected misleading text and missing metadata."
