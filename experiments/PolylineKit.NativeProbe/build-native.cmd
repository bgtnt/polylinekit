@echo off
setlocal
rem Run in a Visual Studio x64 Native Tools command prompt. No toolchain is installed by this script.
pushd "%~dp0"
if not exist "bin\native" mkdir "bin\native"
cl /nologo /O2 /std:c++20 /fp:precise /arch:AVX2 /MD /LD kernels.cpp /Fo:"bin\native\kernels.obj" /link /OUT:"bin\native\native-probe.dll" /IMPLIB:"bin\native\native-probe.lib"
set "probeResult=%errorlevel%"
popd
exit /b %probeResult%
