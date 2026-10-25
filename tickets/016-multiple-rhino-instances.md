# 016 - MULTIPLE RHINO INSTANCES CANNOT RUN THE SERVER

## Severity
Critical

## Description

`ScriptSyncStart` bound the fixed port `58258`. Starting a second Rhino instance
failed with:

```
Error: another process is using port 58258
```

Two sockets cannot share a live listening port on Windows. `SO_REUSEADDR` (which
the plug-in already sets) only allows rebinding a port in `TIME_WAIT`, it does
not allow sharing a port with a live listener.

Solving the port conflict alone was not enough: the VSCode extension hardcoded
`RHINO_TCP_PORT = 58258` and connected to `127.0.0.1`, so it would not have
known which instance to talk to. Both the allocation and the routing had to be
addressed.

## Affected Files
- `CsRhino/ScriptSyncStart.cs`
- `CsRhino/ScriptSyncStop.cs`
- `CsRhino/ScriptSyncInstanceRegistry.cs` (new)
- `CsRhino/ScriptSyncPortAllocator.cs` (new)
- `VSCode/scriptsync/src/extension.ts`
- `VSCode/scriptsync/src/instances.ts` (new)
- `VSCode/scriptsync/src/selection.ts` (new)
- `VSCode/scriptsync/package.json`
- `scriptsync_instances.py` (new)
- `sync_to_rhino.py`, `rhipy_sync.py`, `sync_to_rhino_wsl.py`, `test_sync.py`
- `CsRhino/client.py`

## Resolution

- Each Rhino instance binds the first free port in `58258...58279`. A single
  instance keeps port 58258, so the existing behaviour and all the tooling that
  assumes that port keep working.
- Every instance publishes an entry in `%LOCALAPPDATA%\script-sync\instances.json`
  (`Pid`, `Port`, `Title`, `DocumentPath`, `StartedAt`). Entries of processes
  that no longer exist are pruned on read.
- The VSCode extension gained the command `scriptsync.selectInstance`, which
  shows a quick pick of the running instances. The choice is remembered in
  `workspaceState` (with a `globalState` fallback), so it is configured once and
  every later `F4` goes to the same instance. `scriptsync.listInstances` and
  `scriptsync.clearInstance` complete the set.
- The python helpers accept `--port` and `--instance` and share the registry
  reading logic through `scriptsync_instances.py`.

## Bugs found and fixed along the way

- The listener was started twice: once in `RunCommand` and once in the worker
  thread `Run()`. The second `Start()` threw and was swallowed, leaving
  `IsRunning = false`. The port has to be allocated before the listener starts,
  so the call in `Run()` was removed.
- The registry read-modify-write was not atomic: two Rhino instances starting at
  the same time both read the old list and the second write dropped the entry of
  the first one. The whole read-modify-write now happens under a lock file.
- `rhipy_sync.py` and `sync_to_rhino_wsl.py` polled `<script>.py.error` to
  detect a failure, but the plug-in *deletes* that file on success and never
  writes an empty one. A successful run therefore always timed out. They now
  read the TCP response instead.
- `CsRhino/client.py` hardcoded `F:\script-sync\...` paths and could not work.
  It is now a thin wrapper over the shared helpers.

## Not covered

The Grasshopper side (port 58260) is still single instance:
- `GH/PyGH/components/scriptsynccpy/code.py` hardcodes `vscode_server_port = 58260`.
- The extension drops a GH message unless the basename of `script_path` matches
  the active editor, so output coming from another Rhino instance is lost.

## Status
- [ ] Open
- [ ] In Progress
- [x] Resolved
