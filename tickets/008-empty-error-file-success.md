# 008 - EMPTY ERROR FILE TREATED AS SUCCESS

## Severity
🟡 Medium

## Description
The polling logic in `sync_to_rhino.py` treats an empty error file as success. If Rhino creates the `.error` file but hasn't finished writing to it when polling occurs, a false success is reported.

## Affected Files
- `C:\Users\uk083720\.pi\agent\skills\rhipy\sync_to_rhino.py`

## Current Code
```python
if os.path.exists(error_file):
    with open(error_file) as f:
        content = f.read().strip()
    if content:
        # ... error case
        sys.exit(1)
    else:
        print("OK: script finished with no errors.")
        sys.exit(0)
```

## Recommended Fix
1. Check file size is stable before reading
2. Add a minimum wait after file creation before reading
3. Consider using file locks or rename-on-complete pattern

## Status
- [ ] Open
- [ ] In Progress
- [ ] Resolved
