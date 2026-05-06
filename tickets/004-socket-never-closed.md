# 004 - SOCKET NEVER CLOSED IN VSCODE EXTENSION

## Severity
🟠 High

## Description
The TCP client in `extension.ts` is never explicitly closed after use, potentially causing socket leaks.

## Affected Files
- `VSCode/scriptsync/src/extension.ts`

## Current Code
```typescript
client.on('data', (data) => {
    // ... handle response
});
// No client.end() or client.destroy() call
```

## Recommended Fix
Close the client after handling response:
```typescript
client.on('data', (data) => {
    // ... handle response
    client.end();  // Close connection after response
});
```

## Status
- [ ] Open
- [ ] In Progress
- [ ] Resolved
