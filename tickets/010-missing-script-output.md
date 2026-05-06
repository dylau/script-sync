# 010 - SCRIPT OUTPUT NEVER RETURNED TO VSCODE

## Severity
🔴 Critical

## Description
Python scripts that execute successfully have their output captured but **never returned** to the VSCode extension. The TCP response only contains:

```json
{"success":true,"error":""}
```

The `output` field that VSCode expects (line 94-96 of `extension.ts`) is always empty. The error file mechanism only handles **errors**, not normal `print()` output.

## Affected Files
- `CsRhino/ScriptSyncStart.cs` (TCP response handling)
- `VSCode/scriptsync/src/extension.ts` (expecting `response.output`)

## Expected Behavior
Script output (`stdout`) should be returned to VSCode so users can see print statements in the output channel.

## Design Decision Required
1. Should output be returned via the same TCP connection?
2. Should it be written to a `.output` file similar to `.error` file?
3. Should output streaming be supported (long-running scripts)?

## Status
- [x] Open
- [ ] In Progress
- [ ] Resolved