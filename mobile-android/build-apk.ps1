param()
$ErrorActionPreference = "Stop"

$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$bt = "$sdk\build-tools\35.0.0"
$jar = "$sdk\platforms\android-36\android.jar"
$jdkHome = "$env:LOCALAPPDATA\Programs\jdk-17\jdk-17.0.20.1+1"
$env:JAVA_HOME = $jdkHome
$env:PATH = "$jdkHome\bin;$env:PATH"
$jdk = "$jdkHome\bin"
$aapt2 = "$bt\aapt2.exe"
$d8 = "$bt\d8.bat"
$zipalign = "$bt\zipalign.exe"
$apksigner = "$bt\apksigner.bat"
$javac = "$jdk\javac.exe"
$keytool = "$jdk\keytool.exe"
$jarTool = "$jdk\jar.exe"

$root = $PSScriptRoot
$build = Join-Path $root "build"

if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force (Join-Path $build "compiled_res") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $build "gen") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $build "classes") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $build "dex") | Out-Null

Write-Host "1. Compiling Android resources with AAPT2..." -ForegroundColor Cyan
& $aapt2 compile --dir "$root\src\main\res" -o "$build\compiled_res"
if ($LASTEXITCODE -ne 0) { throw "AAPT2 compile failed" }

Write-Host "2. Linking resources and generating R.java..." -ForegroundColor Cyan
$flatFiles = Get-ChildItem "$build\compiled_res\*.flat" | ForEach-Object { $_.FullName }
& $aapt2 link -I $jar --manifest "$root\src\main\AndroidManifest.xml" --java "$build\gen" -o "$build\app-unaligned.apk" -A "$root\src\main\assets" @flatFiles
if ($LASTEXITCODE -ne 0) { throw "AAPT2 link failed" }

Write-Host "3. Compiling Java sources..." -ForegroundColor Cyan
$javaSources = Get-ChildItem "$build\gen", "$root\src\main\java" -Recurse -Filter "*.java" | ForEach-Object { $_.FullName }
& $javac -source 1.8 -target 1.8 -classpath $jar -d "$build\classes" @javaSources
if ($LASTEXITCODE -ne 0) { throw "javac failed" }

Write-Host "4. Dexing classes with D8..." -ForegroundColor Cyan
$classFiles = Get-ChildItem "$build\classes" -Recurse -Filter "*.class" | ForEach-Object { $_.FullName }
& $d8 @classFiles --lib $jar --min-api 24 --output "$build\dex"
if ($LASTEXITCODE -ne 0) { throw "d8 failed" }

Write-Host "5. Packaging classes.dex into APK..." -ForegroundColor Cyan
& $jarTool -uf "$build\app-unaligned.apk" -C "$build\dex" classes.dex
if ($LASTEXITCODE -ne 0) { throw "jar update failed" }

Write-Host "6. Zipalign APK..." -ForegroundColor Cyan
& $zipalign -v -p 4 "$build\app-unaligned.apk" "$build\app-aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "zipalign failed" }

Write-Host "7. Signing APK with debug keystore..." -ForegroundColor Cyan
$keystore = Join-Path $root "debug.keystore"
if (-not (Test-Path $keystore)) {
    & $keytool -genkey -v -keystore $keystore -alias androiddebugkey -storepass android -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=CloudSSD,O=CloudSSD,C=US"
}

$outputApk = Join-Path $root "Cloud-SSD-Mobile.apk"
& $apksigner sign --ks $keystore --ks-pass pass:android --key-pass pass:android --out $outputApk "$build\app-aligned.apk"
if ($LASTEXITCODE -ne 0) { throw "apksigner failed" }

$sizeMb = [math]::Round((Get-Item $outputApk).Length / 1MB, 2)
Write-Host ("Successfully built: " + $outputApk + " (" + $sizeMb + " MB)") -ForegroundColor Green
