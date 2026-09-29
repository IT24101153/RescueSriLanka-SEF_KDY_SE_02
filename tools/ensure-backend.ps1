$ErrorActionPreference = 'Continue'
$apiUrl = 'http://localhost:5093/api/resources/medical-supplies'

try {
    $response = Invoke-WebRequest -Uri $apiUrl -TimeoutSec 3 -UseBasicParsing
    if ($response.StatusCode -eq 200) {
        Write-Output 'API READY: http://localhost:5093'
        exit 0
    }
} catch {
    # Start the local API below when it is not already responding.
}

Write-Output 'Starting Component C backend'
$backendPath = Join-Path $PSScriptRoot '..\backend\RescueSriLanka.Api'
Push-Location $backendPath
try {
    & dotnet run 2>&1 | ForEach-Object {
        $line = $_.ToString()
        Write-Output $line
        if ($line -match '^Now listening on: http://localhost:5093/?$') {
            Write-Output 'API READY: http://localhost:5093'
        }
    }
    $runExitCode = $LASTEXITCODE
    exit $runExitCode
} finally {
    Pop-Location
}
