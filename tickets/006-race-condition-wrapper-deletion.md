# 006 - RACE CONDITION IN WRAPPER FILE DELETION

## Severity
🟡 Medium

## Description
The temporary wrapper script (`.scsy_wrapper__.py`) may still be open by Rhino when deletion is attempted 500ms after invocation.

## Affected Files
- `CsRhino/ScriptSyncStart.cs`

## Current Code
```csharp
RhinoApp.InvokeOnUiThread(new Action(() => {
    RhinoApp.RunScript("_-ScriptEditor _Run \"" + wrappedScriptPath + "\"", true);
}));
Thread.Sleep(500);  // Fixed delay - may be insufficient
try { if (File.Exists(wrappedScriptPath)) File.Delete(wrappedScriptPath); } catch { }
```

## Recommended Fix
1. Use a retry loop with exponential backoff for deletion
2. Or generate unique wrapper names and use a cleanup thread
3. Or leave wrapper files and clean up on next startup

## Status
- [ ] Open
- [ ] In Progress
- [x] Resolved

## Resolution
Replaced the single `try/catch` block after running the wrapper script with a retry loop that uses exponential backoff (5 retries, starting at 100ms, doubling each time). This handles cases where Rhino's script editor takes longer to release the file handle.
