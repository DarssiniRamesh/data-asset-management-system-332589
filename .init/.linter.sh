#!/bin/bash
cd /home/kavia/workspace/code-generation/data-asset-management-system-332589/data_asset_backend

# Ensure packages are available even when dependencies change during codegen.
dotnet restore -v quiet -nologo
RESTORE_EXIT_CODE=$?
if [ $RESTORE_EXIT_CODE -ne 0 ]; then
  exit 1
fi

dotnet build --no-restore -v quiet -nologo -consoleloggerparameters:NoSummary /p:TreatWarningsAsErrors=false
LINT_EXIT_CODE=$?
if [ $LINT_EXIT_CODE -ne 0 ]; then
  exit 1
fi

