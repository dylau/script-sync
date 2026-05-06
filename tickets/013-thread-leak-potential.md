# 013 - THREAD LEAK POTENTIAL IN GH COMPONENT

## Severity
🟡 Medium

## Description
The Grasshopper Python component initializes threads in `RunScript` without atomic protection:

```python
if self.client_thread_name not in [t.name for t in threading.enumerate()]:
    ClientThread(...).start()
```

During rapid component recomputes, there is a potential race condition where multiple threads with the same name could be created if the check and start aren't atomic.

## Affected Files
- `GH/PyGH/components/scriptsynccpy/code.py`

## Questions
- Is there already thread protection elsewhere in the component lifecycle?
- Should a lock be used around the thread creation check?

## Status
- [x] Open
- [ ] In Progress
- [ ] Resolved