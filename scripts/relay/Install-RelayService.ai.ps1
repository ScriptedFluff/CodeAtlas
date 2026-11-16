param (
    [int]$Port = 9123
)

& "$PSScriptRoot\Install-RelayService.ps1" @PSBoundParameters
