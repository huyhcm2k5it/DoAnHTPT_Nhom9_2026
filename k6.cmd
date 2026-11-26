@echo off
rem Lenh ngat: k6 1 | k6 2 | k6 3 | k6 all | k6 build | k6 test
set "PS=powershell -NoProfile -ExecutionPolicy Bypass -File"
set "S=%~dp0scripts"
if "%~1"=="" goto usage
if /i "%~1"=="build" goto build
if /i "%~1"=="all" goto all
if /i "%~1"=="test" goto test
if "%~1"=="1" goto one
if "%~1"=="2" goto one
if "%~1"=="3" goto one
:usage
echo Cach dung:
echo   k6 1      mo quay C1 (cum gia lap, tai khoan A, mat khau demo)
echo   k6 2      mo quay C2 (tai khoan B)
echo   k6 3      mo quay C3 (tai khoan C)
echo   k6 all    mo ca ba quay
echo   k6 build  build ca solution
echo   k6 test   build va chay test
exit /b 1
:one
%PS% "%S%\start-client.ps1" -ClientId %~1 -Demo
exit /b %errorlevel%
:all
%PS% "%S%\start-demo-clients.ps1"
exit /b %errorlevel%
:build
%PS% "%S%\build.ps1"
exit /b %errorlevel%
:test
%PS% "%S%\test.ps1" -Configuration Release
exit /b %errorlevel%
