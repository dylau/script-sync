# 002 - SILENT EXCEPTION SWALLOWING

## Severity
🔴 Critical

## Description
Multiple `catch { }` blocks in `ScriptSyncStart.cs` silently swallow exceptions without logging. This makes debugging impossible when errors occur.

## Affected Files
- `CsRhino/ScriptSyncStart.cs` (lines ~126, 133, 170, 190, 207)

## Current Code
```csharp
try { if (File.Exists(errorFilePath)) File.Delete(errorFilePath); } catch { }
```

## Recommended Fix
Replace empty catch blocks with logging:
```csharp
try { if (File.Exists(errorFilePath)) File.Delete(errorFilePath); } 
catch (Exception ex) { RhinoApp.WriteLine("Warning: Could not delete error file: " + ex.Message); }
```

## Status
- [ ] Open
- [ ] In Progress
- [x] Resolved

## Resolution
Replaced all 8 empty `catch { }` blocks in `CsRhino/ScriptSyncStart.cs` with logging via `RhinoApp.WriteLine()`:
- Init script run → `ScriptSync warning: could not run init script: {ex.Message}`
- Error file deletion → `ScriptSync warning: could not delete error file: {ex.Message}`
- Syntax check runner → `ScriptSync warning: could not run syntax check: {ex.Message}`
- Syntax check file deletion → `ScriptSync warning: could not delete syntax check file: {ex.Message}`
- Wrapped script run → `ScriptSync warning: could not run wrapped script: {ex.Message}`
- Wrapper script deletion → `ScriptSync warning: could not delete wrapper script: {ex.Message}`
- Non-Python script run → `ScriptSync warning: could not run script: {ex.Message}`
- TCP response send → `ScriptSync warning: could not send response: {ex.Message}`
