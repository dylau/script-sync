# 015 - PORT CONFIGURATION HARDCODED

## Severity
🟢 Low

## Description
Both the Rhino plugin and VSCode extension have hardcoded port values:

| Component | Port | Location |
|-----------|------|----------|
| C# Plugin (Rhino→) | 58258 | `ScriptSyncStart.cs:29` |
| C# Plugin (←VSCode) | 58260 | `extension.ts:9` |
| GH Component (←VSCode) | 58260 | `code.py:185` |

While these appear consistent, there is no mechanism to configure them per-project or via settings.

## Affected Files
- `CsRhino/ScriptSyncStart.cs`
- `VSCode/scriptsync/src/extension.ts`
- `GH/PyGH/components/scriptsynccpy/code.py`

## Questions
- Should VSCode extension settings allow port configuration?
- Should the GH component read its port from a configuration source?

## Status
- [x] Open
- [ ] In Progress
- [ ] Resolved