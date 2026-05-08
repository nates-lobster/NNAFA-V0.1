@echo off
setlocal
echo =======================================
echo    NeuroMemoryStudy Startup Script
echo =======================================

:: 1. Check for .NET SDK
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK not found. Please install .NET 10.0.
    pause
    exit /b
)

:: 2. Check Python Dependencies
echo [1/3] Checking Python Dependencies...
python -m pip install numpy scipy websockets pylsl

:: 3. Build the Project
echo [2/3] Building C# Frontend...
dotnet build "src/Frontend/App/App.csproj" -c Debug
if %errorlevel% neq 0 (
    echo [ERROR] Build failed.
    pause
    exit /b
)

:: 4. Run the Project
echo [3/3] Launching Application...
echo Note: The Python engine will be started automatically by the App.
dotnet run --project "src/Frontend/App/App.csproj" --no-build

pause
