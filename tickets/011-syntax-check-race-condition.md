# 011 - RACE CONDITION IN SYNTAX CHECKING

## Severity
🟡 Medium

## Description
The syntax checking mechanism relies on an arbitrary 300ms sleep before checking for the error file:

```csharp
RhinoApp.InvokeOnUiThread(new Action(() => { /* run syntax check */ }));
Thread.Sleep(300);  // Arbitrary delay!
if (File.Exists(errorFilePath)) {
```

This is unreliable:
- If syntax check takes >300ms, the error is not detected
- If syntax check completes in <300ms, we're just wasting time

## Affected Files
- `CsRhino/ScriptSyncStart.cs` (line ~170-180)

## Expected Behavior
The syntax check should be synchronized using proper threading primitives (events, callbacks, or polling with timeout) rather than arbitrary delays.

## Status
- [x] Open
- [ ] In Progress
- [ ] Resolved