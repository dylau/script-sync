# 003 - ASCII PATH ENCODING BUG

## Severity
🟠 High

## Description
The TCP listener uses `Encoding.ASCII` to decode the incoming script path. Paths containing non-ASCII characters (e.g., `C:\Users\david\文档\script.py`) will be corrupted or rejected.

## Affected Files
- `CsRhino/ScriptSyncStart.cs` line ~128

## Current Code
```csharp
string scriptPath = Encoding.ASCII.GetString(data, 0, bytesRead).Trim();
```

## Recommended Fix
Use UTF-8 encoding:
```csharp
string scriptPath = Encoding.UTF8.GetString(data, 0, bytesRead).Trim();
```

## Status
- [ ] Open
- [ ] In Progress
- [ ] Resolved
