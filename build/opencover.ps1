﻿﻿# Copyright (c) Tunnel Vision Laboratories, LLC. All rights reserved.

# Licensed under the Apache License, Version 2.0 (the "License"); you may not use
# these files except in compliance with the License. You may obtain a copy of the
# License at

# http://www.apache.org/licenses/LICENSE-2.0

# Unless required by applicable law or agreed to in writing, software distributed
# under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
# CONDITIONS OF ANY KIND, either express or implied. See the License for the
# specific language governing permissions and limitations under the License.

# https://github.com/DotNetAnalyzers/StyleCopAnalyzers

$packageConfig = [xml](Get-Content ..\.nuget\packages.config)
$packages_folder = '..\packages'

$opencover_version = $packageConfig.SelectSingleNode('/packages/package[@id="OpenCover"]').version
$opencover_console = "$packages_folder\OpenCover.$opencover_version\tools\OpenCover.Console.exe"

$codecov_version = $packageConfig.SelectSingleNode('/packages/package[@id="Codecov"]').version
$codecov = "$packages_folder\Codecov.$codecov_version\tools\codecov.exe"

# This one has a targetFramework
$report_generator_node = $packageConfig.SelectSingleNode('/packages/package[@id="ReportGenerator"]')
$report_generator_version = $report_generator_node.version
$report_generator_framework = $report_generator_node.targetFramework
$report_generator = "$packages_folder\ReportGenerator.$report_generator_version\tools\$report_generator_framework\ReportGenerator.dll"

# This one has a targetFramework
$xunit_runner_node = $packageConfig.SelectSingleNode('/packages/package[@id="xunit.runner.console"]')
$xunit_runner_version = $xunit_runner_node.version
$xunit_runner_framework = $xunit_runner_node.targetFramework
$xunit_runner_console = "$packages_folder\xunit.runner.console.$xunit_runner_version\tools\$xunit_runner_framework\xunit.console.dll"

$report_folder = '.\OpenCoverReports'
If (Test-Path $report_folder) {
	Remove-Item -Recurse -Force $report_folder
}

mkdir $report_folder | Out-Null

$report_file = "$report_folder\portent_coverage.xml"

$target_dll = "..\Portent.Test\bin\Release\netcoreapp3.0\Portent.Test.dll"

&$opencover_console `
	-register:user `
	-threshold:1 -oldStyle `
	-returntargetcode `
	-hideskipped:All `
	-filter:"+[Portent*]* -[Portent.Test*]*" `
	-excludebyattribute:*.ExcludeFromCodeCoverage* `
	-excludebyfile:*\*Designer.cs `
	-output:"$report_file" `
	-mergebyhash -mergeoutput `
	-target:"$xunit_runner_console" `
	-targetargs:"$target_dll -noshadow -appveyor"

# Do I even need to do this if I'm only targeting C# 8?
.\$report_generator -targetdir:$report_folder -reports:"$report_file"

If (-not $?) {
	$host.UI.WriteErrorLine('Build failed; coverage analysis aborted.')
	Exit $LASTEXITCODE
}

&$codecov -f "$report_file"