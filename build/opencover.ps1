$packageConfig = [xml](Get-Content ..\.nuget\packages.config)
$packages_folder = '..\packages'

$opencover_version = $packageConfig.SelectSingleNode('/packages/package[@id="OpenCover"]').version
$opencover_console = "$packages_folder\OpenCover.$opencover_version\tools\OpenCover.Console.exe"

$codecov_version = $packageConfig.SelectSingleNode('/packages/package[@id="Codecov"]').version
$codecov = "$packages_folder\Codecov.$codecov_version\tools\codecov.exe"

$reportgenerator_version = $packageConfig.SelectSingleNode('/packages/package[@id="ReportGenerator"]').version
$report_generator = "$packages_folder\ReportGenerator.$reportgenerator_version\tools\ReportGenerator.exe"

$xunitrunner_version = $packageConfig.SelectSingleNode('/packages/package[@id="xunit.runner.console"]').version
$xunit_runner_console = "$packages_folder\xunit.runner.console.$xunitrunner_version\tools\xunit.console.x86.exe"

$report_folder = '.\OpenCoverReports'
If (Test-Path $report_folder) {
	Remove-Item -Recurse -Force $report_folder
}

mkdir $report_folder | Out-Null

$report_file = "$report_folder\portent_coverage.xml"

$target_dll = "..\portent.Test\bin\Release\netcoreapp3.0\portent.Test.dll"

&$opencover_console `
	-register:user `
	-threshold:1 -oldStyle `
	-returntargetcode `
	-hideskipped:All `
	-filter:"+[StyleCop*]*" `
	-excludebyattribute:*.ExcludeFromCodeCoverage* `
	-excludebyfile:*\*Designer.cs `
	-output:"$report_file" `
	-mergebyhash -mergeoutput `
	-target:"$xunit_runner_console" `
	-targetargs:"$target_dll -noshadow -appveyor"

# Do I even need to do this if I'm only targeting C# 8?
&$report_generator -targetdir:$report_folder -reports:"$report_file"

If (-not $?) {
	$host.UI.WriteErrorLine('Build failed; coverage analysis aborted.')
	Exit $LASTEXITCODE
}

&$codecov -f "$report_file"