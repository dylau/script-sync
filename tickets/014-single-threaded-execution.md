# 014 - SINGLE-THREADED SCRIPT EXECUTION BOTTLENECK

## Severity
🟢 Low

## Description
The TCP server in `ScriptSyncStart.cs` processes scripts sequentially:

```csharp
while (IsRunning)
{
    TcpClient client = _server.AcceptTcpClient();
    // ... process one script ...
}
```

If a script hangs or takes a long time, all subsequent scripts queue behind it.

## Affected Files
- `CsRhino/ScriptSyncStart.cs`

## Questions
- Is concurrent script execution a desired feature?
- If yes, a thread pool or task queue would be needed
- If no, a timeout mechanism should be added to prevent hangs

## Status
- [x] Open
- [ ] In Progress
- [ ] Resolved