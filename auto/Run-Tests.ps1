param (
    $Cycles = 100,
    $Configuration = "Debug"
)

$myDir = [IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Definition)
$binDir = "$myDir\..\bin\$Configuration"
$tmpDir = "$myDir\..\testlogs"

if (Test-Path $tmpDir)
{
    del $tmpDir -Recurse -Force
}
$_ = mkdir $tmpDir -Force

$vstest = "${env:ProgramFiles(X86)}\Microsoft Visual Studio\2017\Community\Common7\IDE\Extensions\TestPlatform\VSTest.Console.exe"
if (!(Test-Path $vstest))
{
    Write-Warning "Did not find the Visual Studio 2017 Console Test Runner."
    exit 1
}

$assemblies = @(
    "$binDir\Mastersign.Tasks.Test.dll"
)

foreach ($assembly in $assemblies)
{
    if (!(Test-Path $assembly)) 
    {
        Write-Warning "Did not find the test assembly $assembly."
    }
}

$errCount = 0
$successCount = 0

for ($i = 0; $i -lt $Cycles; $i++)
{
    foreach ($assembly in $assemblies)
    {
        $output = & $vstest $assembly "/TestAdapterPath:$binDir" "/TestCaseFilter:TestCategory=Concurrent"
        $status = $LastExitCode
        if ($status -ne 0)
        {
            $output | Out-File "$tmpDir\log_${i}.txt" -Encoding Default
            $errCount++;
            Write-Host "$i Failed"
        }
        else
        {
            $successCount++;
            Write-Host "$i Passed"
        }
    }
}

if ($errCount -gt 0)
{
    Write-Warning "$errCount Failed, $successCount Passed"
    [System.Media.SystemSounds]::Exclamation.Play();
    exit 1
}
else
{
    Write-Host "$successCount Passed"
    exit 0
}
