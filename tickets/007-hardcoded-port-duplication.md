# 007 - HARDCODE PORT DUPLICATION IN VSCODE EXTENSION

## Severity
🟡 Medium

## Description
The TCP port is hardcoded in multiple places within `extension.ts` instead of being defined as a constant.

## Affected Files
- `VSCode/scriptsync/src/extension.ts`

## Current Code
```typescript
const port = 58258;
// ... later ...
client.connect(58258, '127.0.0.1', () => {
```

## Recommended Fix
Define port as a module-level constant:
```typescript
const RHINO_TCP_PORT = 58258;
const RHINO_TCP_HOST = '127.0.0.1';
```

## Status
- [ ] Open
- [ ] In Progress
- [x] Resolved

## Resolution
Defined port as module-level constants (`const port = 58258` and `const host = '127.0.0.1'`) and replaced the hardcoded values in `client.connect()` call in `VSCode/scriptsync/src/extension.ts`.
