# Accepts every HTTP request on a port and prints it, so a webhook notifier can be watched end to end.
# Answers 200 with an empty body. Stops on Ctrl+C.

param(
    [int] $Port = 9099
)

$ErrorActionPreference = 'Stop'

# The every-address prefix needs an elevated shell or a netsh reservation; localhost is the fallback.
$prefixes = @(('http://+:{0}/' -f $Port), ('http://localhost:{0}/' -f $Port))

$listener = $null
$bound = $null
foreach ($prefix in $prefixes) {
    $candidate = [System.Net.HttpListener]::new()
    $candidate.Prefixes.Add($prefix)
    try {
        $candidate.Start()
        $listener = $candidate
        $bound = $prefix
        break
    }
    catch {
        $candidate.Close()
        $failure = $_.Exception.Message
    }
}

if (-not $listener) {
    throw ('Cannot listen on port {0}: {1}' -f $Port, $failure)
}

Write-Output ('Listening on {0} - Ctrl+C to stop.' -f $bound)

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request

        Write-Output ''
        Write-Output ('{0} {1} {2}' -f (Get-Date -Format 'u'), $request.HttpMethod, $request.Url)

        foreach ($name in $request.Headers.AllKeys) {
            Write-Output ('{0}: {1}' -f $name, $request.Headers[$name])
        }

        $encoding = if ($request.ContentEncoding) { $request.ContentEncoding } else { [System.Text.Encoding]::UTF8 }
        $reader = [System.IO.StreamReader]::new($request.InputStream, $encoding)
        try {
            Write-Output ''
            Write-Output $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $context.Response.StatusCode = 200
        $context.Response.ContentLength64 = 0
        $context.Response.Close()
    }
}
finally {
    $listener.Close()
}
