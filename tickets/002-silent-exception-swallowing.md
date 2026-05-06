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
- [ ] Resolved
