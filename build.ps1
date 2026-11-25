param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference="Stop"
$dest=(Resolve-Path -LiteralPath $AppDirectory).Path
Push-Location $PSScriptRoot
try {
    $compiler=Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$dest\ToritsuStudio.exe" /win32icon:Tokyo.ico /resource:Toolbar.png,Toolbar.png /resource:CreateHeader.png,CreateHeader.png /resource:PreviewCard.png,PreviewCard.png /r:Microsoft.CSharp.dll /r:Microsoft.VisualBasic.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll "/r:$dest\Microsoft.Web.WebView2.Core.dll" "/r:$dest\Microsoft.Web.WebView2.WinForms.dll" Editor.cs Extras.cs Studio.cs Preview.cs Rooms.cs Guide.cs Style.cs Learning.cs Javascript.cs SourceBox.cs Modes.cs CreateLayout.cs Workspace.cs CreatePanels.cs Course.cs
    if($LASTEXITCODE){throw "ビルドに失敗しました。"}
    Copy-Item preview.py -Destination $dest -Force
} finally {Pop-Location}
