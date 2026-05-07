<p align="center">
    <img src="VSCode\scriptsync\logo\scriptsync_480.png" width="140">
</p>

<p align="center">
    <img src="https://github.com/ibois-epfl/script-sync/actions/workflows/rhinoplugin.yml/badge.svg">
    <img src="https://github.com/ibois-epfl/script-sync/actions/workflows/ghuserbuild.yml/badge.svg">
    <img src="https://github.com/ibois-epfl/script-sync/actions/workflows/yakbuild.yml/badge.svg">
    <img src="https://github.com/ibois-epfl/script-sync/actions/workflows/vscodeext.yml/badge.svg">
    <img alt="Dynamic JSON Badge" src="https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fyak.rhino3d.com%2Fpackages%2Fscript-sync&query=%24.version&logo=rhinoceros&label=Yak&color=%23a3d6ff">
</p>

# script-sync

**What is it?** Script-sync plug-in to run C# and Python (IronPython or CPython) scripts directly from VSCode into Rhino and Grasshopper. This project is a research utility from the [IBOIS lab](https://www.epfl.ch/labs/ibois/) at EPFL. It was developed and currently maintained by [Andrea Settimi](https://github.com/9and3).

**Why Script-sync?** Although Rhino8 has a wonderful IDE, we often miss the nice extensions and functions of a full-fledged IDE like VSCode. Script-sync allows you to run your scripts directly from VSCode, while keeping the Rhino/Grasshopper environment open. This is particularly useful if you have *AI-assisted* (e.g. GithubCoPilot) code completion.

Also, if Rhino crashes, your code is still safe and editable.

You can execute the folloing languages from VSCode with script-sync:

|               | CPython | IronPython  | C# |
| ------------- | ------  | ----------- | -- |
| Rhino         | ✅      | ✅          | ✅|
| Grasshopper   | ✅      | ❌           | ❌  |



<br>

<p float="left">
  <figure>
    <img src="https://github.com/ibois-epfl/script-sync/assets/50238678/7ccb2aa5-e646-45cd-9657-95776d24a48a" width="100%" />
    <figcaption><i>Script-sync in Rhino</i></figcaption>
  </figure>

  <figure>
    <img src="https://github.com/ibois-epfl/script-sync/blob/main/GH/PyGH/assets/vid/scriptsync_gh.gif?raw=true" width="100%" />
    <figcaption><i>Script-sync in Grasshopper</i></figcaption>
  </figure>
</p>


## Installation
🦏/🦗 **`Rhino/Grasshopper`**: Install script-sync rhino from food4rhino or the packageManager in Rhino (name: "script-sync"). For Grasshopper you might want to get rid of the old version of the plugin before installing the new one. Just right-click on the old icon and click *delete*.

👩‍💻 **`VScode`**: Install script-syncVSCode extension from the VSCode extension marketplace (name: "script-sync")

## How to use
🦏 **`Rhino`**: To start `script-sync` in RhinoV8, run the command `ScriptSyncStart` in RhinoV8. This will start a server that listens to commands from VSCode.
To close `script-sync` in RhinoV8, run the command `ScriptSyncStop` in RhinoV8.

🦗 **`Grasshopper`**: To start `script-sync` in Grasshopper, add the component script-sync: 
- <code>select_file</code>: click to open a file explorer and connect a script,
- <code>package_2_reload</code>: this can be empty in 90% of the cases, but if you develop a custom pypi package, (installed with editable pip mode) you can add the name of the package you are developing here to track the changes in its modules. Otherwise leave it empty.
- <code>x</code>: classical input parameter, you can add more, 
- <code>stdout</code>: all errors and print() is deviated here, 
- <code>a</code>: classical output parameter, you can add more.

<p  align="center">
    <img src="GH\PyGH\assets\img\gh_snap2.png" width="550">
</p>

> [!TIP]
> `script-sync` automatically converts lists and nested lists to Grasshopper data trees. Just return the python list as value. It also supports the `ghpythonlib.treehelpers` module. Example:
> ```python
>   # option 1
>   py_nlist = [
>       [[1, 2], [3, 4]],
>       [[5, 6], [7, 8]]
>   ]
>
>   # options 2
>   import ghpythonlib.treehelpers as th
>   gh_tree = th.list_to_tree(py_nlist)
>
>   o_as_nlist = py_nlist
>   o_as_tree = gh_tree
> ```
> <p  align="center">
>    <img src="GH/PyGH/assets/img/listtreeauto.png" width="550">
> </p>


👩‍💻 **`VScode`**: Open a script in VSCode and run it in RhinoV8 by pressing `F4` to run in Rhino or `shift+F4` for Grasshopper.
For Python files, add a `shebang` to the first line of the file to specify the interpreter to use, e.g.:
* `#! python3` to interpret it with CPython
* ⚠️ `#! python2` to interpret it with IronPython (only in Rhino)

> [!TIP]
> If you want your script-sync VSCode extension to automatically update, you should thick the autoinstall box in the vscode extension page.

> [!CAUTION]
> If you use modules like `pickle` to (de)serialize objects script-sync might causes problems because we reload all the modules at every file save. This can interfere with `pickle` thinking that a class is instanciated multiple times.

## Requirements
The plug-in needs to be installed on RhinoV8, Grasshopper and VSCode

## Issues
For bugs open an issue on the [GitHub repo](https://github.com/ibois-epfl/script-sync/issues).

## Contribution
All contributions are welcome. Have a look at the [contribution guidelines](CONTRIBUTING.md).

## References
There are a lot of plug-ins that allow to run Python in Rhino. Among them, [CodeListener](https://github.com/ccc159/CodeListener) was working until RhinoV8 and it was a source of inspiration for this project.

# For code maintainers
Packages are published (`.yak` and `.vsix`)  automatically when a GitHub release is created.

## Architecture

### Components

| Component | File(s) | Role |
|---|---|---|
| VSCode extension | `VSCode/scriptsync/src/extension.ts` | Sends script path to Rhino via TCP on F4 |
| Rhino plugin | `CsRhino/ScriptSyncStart.cs` | Receives path, runs script, handles errors |
| Grasshopper component | `GH/PyGH/components/` | Runs Python in Grasshopper |

### TCP Connection

| Direction | Host | Port |
|---|---|---|
| VSCode → Rhino | `127.0.0.1` | `58258` |
| Rhino → VSCode | `127.0.0.1` | `58260` |

### Flow: VSCode → Rhino (F4)

1. **Save** — active file is saved
2. **Send** — VSCode extension sends file path via TCP to `127.0.0.1:58258`
3. **Rhino receives** — `ScriptSyncStart.cs` reads the path
4. **Syntax check** — for `.py` files: `py_compile` validates syntax; if invalid, writes to `[script].py.error` and returns error immediately
5. **Wrap** — script is wrapped with `try/except` + `traceback` to catch runtime errors
6. **Execute** — script is passed to Rhino's `ScriptEditor`
7. **Response** — Rhino sends JSON over TCP: `{"success": true/false, "error": "..."}`
8. **Feedback** — VSCode output channel shows result; on error, a popup shows the first error line

### Error Handling Events

For `.py` files, error files are created at `[scriptpath].py.error`:

| Event | `.error` file |
|---|---|
| Before every run | Deleted if it exists |
| Syntax error | Written with `py_compile` output |
| Runtime error | Written with `traceback` |
| Success | **Not touched** — no file left |

A present `.error` file always means the last run failed.


### Shebang Selection

| Shebang | Runtime |
|---|---|
| `#! python3` | CPython (Rhino + Grasshopper) |
| `#! python2` | IronPython (Rhino only) |
| `.cs` | C# (Rhino only) |

> [!NOTE]
> The shebang is ignored on Windows for IronPython CPython targets — Rhino's `ScriptEditor` selects the interpreter internally based on the shebang in the script.

