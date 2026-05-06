# 001 - PORT MISMATCH (Critical)

## Severity
🔴 Critical

## Description
The rhipy skill (`sync_to_rhino.py`) connects to port **58259**, but the Rhino plugin (`ScriptSyncStart.cs`) listens on port **58258**. This causes the skill to fail to communicate with Rhino.

## Affected Files
- `C:\Users\uk083720\.pi\agent\skills\rhipy\sync_to_rhino.py` (uses PORT = 58259)
- `CsRhino/ScriptSyncStart.cs` (uses Port = 58258)
- `VSCode/scriptsync/src/extension.ts` (uses port = 58258)

## Expected Behavior
Both client and server should use the same port (58258 per repository documentation).

## Fix Required
Update `sync_to_rhino.py` line 10:
```python
PORT = 58258  # Was 58259
```

## Status
- [x] Open
- [ ] In Progress
- [x] Resolved

## Resolution
Fixed port mismatch in `C:\Users\uk083720\.pi\agent\skills\rhipy\sync_to_rhino.py`: changed PORT from 58259 to 58258 to match Rhino plugin.
