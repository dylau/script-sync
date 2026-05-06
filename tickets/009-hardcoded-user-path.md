# 009 - HARDCODED USER-SPECIFIC PATH

## Severity
🔴 Critical

## Description
The Python initialization script path is hardcoded to a specific user's Rhinocode directory:

```csharp
string initScriptPath = @"C:\Users\uk083720\.rhinocode\py39-rh8\lib\importlib\__init__.py";
```

This will fail for any user other than `uk083720`.

## Affected Files
- `CsRhino/ScriptSyncStart.cs` (line ~115)

## Expected Behavior
The Python init script path should be resolved programmatically based on the current user's environment.

## Questions
- Is there a RhinoCommon API to locate the Rhinocode/py39-rh8 directory?
- Or should this default to `%USERPROFILE%\.rhinocode\py39-rh8\lib\importlib\__init__.py`?

## Status
- [x] Open
- [ ] In Progress
- [ ] Resolved