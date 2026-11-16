<#
.SYNOPSIS
    Publishes the CodeAtlas server and CLI projects.
.DESCRIPTION
    Forwards execution to the centralized scripts/publish.ps1 script.
.PARAMETER Configuration
    The build configuration to use (e.g., Release, Debug). Defaults to Release.
#>
param (
    [string]$Configuration = "Release"
)

& "$PSScriptRoot\scripts\publish.ps1" @PSBoundParameters
