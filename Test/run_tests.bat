@echo off
setlocal EnableDelayedExpansion

@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "SERVER_CONFIG=..\FileSync.Server\Config\server_config.json"
set "SERVER_BIN=..\FileSync.Server\bin\Debug\net9.0\FileSync.Server.exe"
set "CLI_BIN=..\FileSync.Client\bin\Debug\net9.0"
set "CLIENT_A=ClientA"
set "CLIENT_B=ClientB"
set "SERVER_IP=127.0.0.1"

echo ==========================================
echo      FileSync Automated Tests
echo ==========================================

:: 0. Kill existing instances
taskkill /F /IM FileSync.Server.exe >nul 2>&1

:: 1. Start Server
echo [Setup] Starting Server...
start "FileSync Server" /MIN "%SERVER_BIN%"
timeout /t 5 /nobreak >nul

:: 2. Cleanup Old Clients
if exist "%CLIENT_A%" rmdir /s /q "%CLIENT_A%"
if exist "%CLIENT_B%" rmdir /s /q "%CLIENT_B%"

:: 2. Extract Port and Key
echo [Setup] Reading Server Config...
for /f "delims=" %%a in ('powershell -Command "Get-Content '%SERVER_CONFIG%' | ConvertFrom-Json | Select-Object -ExpandProperty Port"') do set SERVER_PORT=%%a
for /f "delims=" %%a in ('powershell -Command "Get-Content '%SERVER_CONFIG%' | ConvertFrom-Json | Select-Object -ExpandProperty PublicKey"') do set SERVER_KEY=%%a

echo [Setup] Port: %SERVER_PORT%
echo [Setup] Key: %SERVER_KEY:~0,20%...

:: 3. Prepare Clients
echo [Setup] Preparing Client A...
mkdir "%CLIENT_A%"
xcopy /E /I /Y /Q "%CLI_BIN%\*" "%CLIENT_A%\" >nul
pushd "%CLIENT_A%"
FileSync.Client.CLI.exe config --server %SERVER_IP% --port %SERVER_PORT% --key "%SERVER_KEY%" --root "Files"
popd

echo [Setup] Preparing Client B...
mkdir "%CLIENT_B%"
xcopy /E /I /Y /Q "%CLI_BIN%\*" "%CLIENT_B%\" >nul
pushd "%CLIENT_B%"
FileSync.Client.CLI.exe config --server %SERVER_IP% --port %SERVER_PORT% --key "%SERVER_KEY%" --root "Files"
popd

:: Create File Roots
:: Note: The CLI config sets root to just "Files" (relative to Client EXE/Run dir), which is cleaner given pushd.
mkdir "%CLIENT_A%\Files"
mkdir "%CLIENT_B%\Files"

echo.
echo ==========================================
echo CASE 1: File Creation
echo ==========================================
echo [Action] Creating file1.txt in A
echo Hello World > "%CLIENT_A%\Files\file1.txt"

echo [Sync] Client A Syncing...
pushd "%CLIENT_A%"
FileSync.Client.CLI.exe sync > "sync_create.log" 2>&1
popd

echo [Sync] Client B Syncing...
pushd "%CLIENT_B%"
FileSync.Client.CLI.exe sync > "sync_create.log" 2>&1
popd

if exist "%CLIENT_B%\Files\file1.txt" (
    echo [PASS] file1.txt found in Client B.
) else (
    echo [FAIL] file1.txt NOT found in Client B.
    echo --- Client A Log ---
    type "%CLIENT_A%\sync_create.log"
    echo --- Client B Log ---
    type "%CLIENT_B%\sync_create.log"
    goto :error
)

echo.
echo ==========================================
echo CASE 2: File Modification
echo ==========================================
echo [Action] Waiting to ensure timestamp diff...
timeout /t 2 /nobreak >nul
echo [Action] Modifying file1.txt in A
echo Modified Content >> "%CLIENT_A%\Files\file1.txt"

echo [Sync] Client A Syncing...
pushd "%CLIENT_A%"
FileSync.Client.CLI.exe sync > "sync_mod.log" 2>&1
popd

echo [Sync] Client B Syncing...
pushd "%CLIENT_B%"
FileSync.Client.CLI.exe sync > "sync_mod.log" 2>&1
popd

findstr "Modified" "%CLIENT_B%\Files\file1.txt" >nul
if %errorlevel%==0 (
    echo [PASS] Modification synced to Client B.
) else (
    echo [FAIL] Modification NOT synced to Client B.
    goto :error
)

echo.
echo ==========================================
echo CASE 3: File Deletion
echo ==========================================
echo [Action] Deleting file1.txt in A
del "%CLIENT_A%\Files\file1.txt"

echo [Sync] Client A Syncing...
pushd "%CLIENT_A%"
FileSync.Client.CLI.exe sync > "sync_del.log" 2>&1
popd

echo [Sync] Client B Syncing...
pushd "%CLIENT_B%"
FileSync.Client.CLI.exe sync > "sync_del.log" 2>&1
popd

if not exist "%CLIENT_B%\Files\file1.txt" (
    echo [PASS] file1.txt deleted from Client B.
) else (
    echo [FAIL] file1.txt STILL EXISTS in Client B.
    goto :error
)

echo.
echo ==========================================
echo CASE 4: Conflicting Modification (Last Write Wins)
echo ==========================================
echo [Action] Creating conflict.txt in A
echo Base Content > "%CLIENT_A%\Files\conflict.txt"
pushd "%CLIENT_A%"
FileSync.Client.CLI.exe sync >nul
popd
pushd "%CLIENT_B%"
FileSync.Client.CLI.exe sync >nul
popd

echo [Action] Modifying B (Old)
echo Old Content > "%CLIENT_B%\Files\conflict.txt"

echo [Action] Waiting...
timeout /t 2 /nobreak >nul

echo [Action] Modifying A (New - Should Win)
echo New Content > "%CLIENT_A%\Files\conflict.txt"

echo [Sync] Client B pushing 'Old'...
pushd "%CLIENT_B%"
FileSync.Client.CLI.exe sync > "sync_conflict_B1.log" 2>&1
popd

echo [Sync] Client A pushing 'New'...
pushd "%CLIENT_A%"
FileSync.Client.CLI.exe sync > "sync_conflict_A1.log" 2>&1
popd

echo [Sync] Client B pulling 'New'...
pushd "%CLIENT_B%"
FileSync.Client.CLI.exe sync > "sync_conflict_B2.log" 2>&1
popd

findstr "New" "%CLIENT_B%\Files\conflict.txt" >nul
if %errorlevel%==0 (
    echo [PASS] Client B has the NEW content.
) else (
    echo [FAIL] Client B does NOT have the NEW content.
    type "%CLIENT_B%\Files\conflict.txt"
    goto :error
)

echo.
echo ==========================================
echo ALL TESTS PASSED
echo ==========================================
taskkill /F /IM FileSync.Server.exe >nul 2>&1
rmdir /s /q "%CLIENT_A%"
rmdir /s /q "%CLIENT_B%"
exit /b 0

:error
echo ==========================================
echo TEST FAILED
echo ==========================================
taskkill /F /IM FileSync.Server.exe >nul 2>&1
exit /b 1
