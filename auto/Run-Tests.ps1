param (
    $Cycles = 100,
    $Configuration = "Debug",
    $ErrorSound = "error.wav",
    [switch]$NoHalt
)

$myDir = [IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Definition)
$binDir = "$myDir\..\bin\$Configuration"
$tmpDir = "$myDir\..\testlogs"

function playErrorSound ()
{
    pushd $myDir
    $soundPath = Resolve-Path $ErrorSound
    popd
    $sound = New-Object System.Media.SoundPlayer
    $sound.SoundLocation = $soundPath
    $sound.PlaySync()
}

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
        $output = & $vstest $assembly "/TestAdapterPath:$binDir" # "/TestCaseFilter:TestCategory=Selected"
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
    if (!$NoHalt -and $errCount -gt 0) { break }
}

if ($errCount -gt 0)
{
    Write-Warning "$errCount Failed, $successCount Passed"
    playErrorSound
    exit 1
}
else
{
    Write-Host "$successCount Passed"
    exit 0
}
