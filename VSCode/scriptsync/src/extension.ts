// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import * as net from 'net';
import * as path from 'path';
import * as fs from 'fs';

let server: net.Server | null = null;
let connections: net.Socket[] = [];
let isLogging = false;

const RHINO_TCP_PORT = 58258;
const RHINO_TCP_HOST = '127.0.0.1';
const RHINO_PYTHON_PATH = 'C:\\Users\\uk083720\\.rhinocode\\py39-rh8\\python.exe';

const outputChannel = vscode.window.createOutputChannel('scriptsync');
let lastReceivedMessage: { guid: any; } | null = null;

// Cached sys.path diff for the current VS Code session.
// Resolved once on first F4 send, reused for subsequent sends.
let cachedPathDiff: string[] | null = null;
let pathDiffResolveAttempted = false;

function startServer() {
    isLogging = true;
    server = net.createServer((socket) => {
        socket.setTimeout(0);
        connections.push(socket);
        socket.on('end', () => {
            connections = connections.filter(conn => conn !== socket);
        });
        socket.on('data', (data) => {
            try {
                const message = JSON.parse(data.toString());

                const activeEditor = vscode.window.activeTextEditor;
                if (activeEditor) {
                    let vscodeActiveScriptName = path.basename(activeEditor.document.uri.fsPath);
                    let ghScriptName = path.basename(message.script_path);

                    if (vscodeActiveScriptName === ghScriptName) {
                        if (lastReceivedMessage !== message.msg) {
                            if (isLogging) {
                                outputChannel.clear();
                                outputChannel.appendLine(message.msg);
                                lastReceivedMessage = message.msg;
                            }
                        }
                    }
                }

            } catch (error) {
                vscode.window.showErrorMessage(`scriptsync::Message parsing Error: ${(error as Error).message}`);
            }
        });
        socket.on('error', (error) => {
            if (error.message.includes('ECONNRESET')) {
                vscode.window.showWarningMessage('scriptsync::GHListener in standby.');
            } else {
                vscode.window.showErrorMessage(`scriptsync::Socket Error: ${error.message}`);
            }
        });
    });

    // start the server by reusing the same port with SO_REUSEADDR
    server.listen(58260, '127.0.0.1', () => {
        vscode.window.showInformationMessage('scriptsync::GHListener started.');
        outputChannel.clear();
        outputChannel.appendLine('scriptsync::Ready to listen to GHcomponent.');
    });
}

function silenceServer() {
    if (server) {
        // Close all connections
        connections.forEach((conn) => conn.end());
        connections = [];

        // Close server
        server.close(() => {
            vscode.window.showInformationMessage('scriptsync::GHListener stopped.');
            outputChannel.clear();
            outputChannel.appendLine('scriptsync::GHListener stopped.');
        });
        server = null;

        isLogging = false;
    }
}

// Read sys.path from a Python interpreter by spawning it as a subprocess.
// Pass source via stdin so quoting/newlines stay safe.
async function readSysPath(interpreterPath: string): Promise<string[]> {
    return new Promise((resolve) => {
        const proc = require('child_process').spawn(
            interpreterPath,
            ['-'],
            {
                shell: false,
                timeout: 5000,
                windowsHide: true
            }
        );

        let stdout = '';
        let stderr = '';
        proc.stdout?.on('data', (data: Buffer) => { stdout += data.toString(); });
        proc.stderr?.on('data', (data: Buffer) => { stderr += data.toString(); });

        proc.on('close', (code: number | null) => {
            if (code === 0 && stdout.trim()) {
                try {
                    const parsed = JSON.parse(stdout.trim());
                    if (Array.isArray(parsed)) {
                        resolve(parsed.filter((p): p is string => typeof p === 'string'));
                        return;
                    }
                } catch { /* fall through */ }
            }
            outputChannel.appendLine(
                `[scriptsync] readSysPath failed for ${interpreterPath} (code=${code}, stderr=${stderr.trim()})`
            );
            resolve([]);
        });

        proc.on('error', (err: Error) => {
            outputChannel.appendLine(`[scriptsync] readSysPath error for ${interpreterPath}: ${err.message}`);
            resolve([]);
        });

        proc.stdin.write('import sys, json\nprint(json.dumps(sys.path))\n');
        proc.stdin.end();

        // Force-timeout after 5 seconds
        setTimeout(() => {
            try { proc.kill(); } catch { /* ignore */ }
            resolve([]);
        }, 5000);
    });
}

// Compute the sys.path diff between the VS Code conda env and Rhino's Python.
// Returns paths that exist in the conda env but not in Rhino (case-insensitive).
// Excludes conda env's Python stdlib paths (lib/, DLLs/, python39.zip) — they
// shadow Rhino's own stdlib and cause DLL load failures on cross-version imports.
async function resolvePathDiff(): Promise<string[]> {
    const condaPath = vscode.workspace
        .getConfiguration('python')
        .get<string>('defaultInterpreterPath', '');
    if (!condaPath) return [];

    const [condaSysPath, rhinoSysPath] = await Promise.all([
        readSysPath(condaPath),
        readSysPath(RHINO_PYTHON_PATH)
    ]);

    if (condaSysPath.length === 0) return [];

    const normalize = (p: string) => p.toLowerCase().replace(/\\/g, '/').replace(/\/+$/, '');
    const rhinoLower = new Set(rhinoSysPath.map(normalize));

    // Skip conda env's own Python runtime directories (stdlib + DLLs + zip).
    // These would shadow Rhino's bundled Python 3.9.10 stdlib and break
    // extension modules like `_ctypes` (built against a different Python ABI).
    const isStdlibPath = (p: string): boolean => {
        const lower = p.toLowerCase().replace(/\\/g, '/');
        if (lower.endsWith('/dlls')) return true;
        if (lower.endsWith('/lib')) return true;
        if (lower.endsWith('.zip')) return true;
        // Match /lib/pythonX.Y/ ... (the actual stdlib directory inside conda env)
        const m = lower.match(/^(.+?\/lib)\/python\d+\.\d+(?:\/.*)?$/);
        return !!m;
    };

    return condaSysPath.filter(p => {
        const n = normalize(p);
        if (rhinoLower.has(n)) return false;
        if (isStdlibPath(p)) return false;
        return true;
    });
}

// Build the sys.path insertion string to prepend to the script.
// Accepts a list of paths (the diff result) and emits one insert per path.
function buildPathInsertion(paths: string[]): string {
    if (paths.length === 0) return '';
    const inserts = paths
        .map(p => `sys.path.insert(0, r'${p.replace(/\\/g, '/')}')`)
        .join('\n');
    return `import sys\n${inserts}\n`;
}

// Write a modified copy of the script with sys.path prepended to a temp file.
// Returns the path to the temp file.
function writeTempScript(originalPath: string, paths: string[]): string {
    const dir = path.dirname(originalPath);
    const basename = path.basename(originalPath);
    const tempName = `.${basename}__scsy_send__.py`;
    const tempPath = path.join(dir, tempName);

    const originalContent = fs.readFileSync(originalPath, 'utf-8');
    const pathInsertion = buildPathInsertion(paths);
    const modifiedContent = pathInsertion + originalContent;

    fs.writeFileSync(tempPath, modifiedContent, 'utf-8');
    outputChannel.appendLine(
        `[scriptsync] wrote temp script with ${paths.length} injected paths: ${tempPath}`
    );
    return tempPath;
}

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {
    // % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % %
    // %% Rhino
    // % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % %
    let rhinoSenderCmd = vscode.commands.registerCommand('scriptsync.sendPath', async () => {
        console.log('scriptsync.sendPath command triggered');
        outputChannel.appendLine('F4 pressed - sending to Rhino');
        vscode.window.showInformationMessage('scriptsync::Sending to Rhino...');

        // port and ip address of the server
        const port = RHINO_TCP_PORT;
        const host = RHINO_TCP_HOST;

        // check the file extension: accept only .py and .cs files
        const activeTextEditor = vscode.window.activeTextEditor;
        if (!activeTextEditor) {
            vscode.window.showWarningMessage('scriptsync::No active text editor');
            return;
        }

        let fileExtension = activeTextEditor.document.uri.path.split('.').pop() || '';
        if (fileExtension !== 'py' && fileExtension !== 'cs') {
            vscode.window.showWarningMessage('scriptsync::File extension not supported');
            return;
        }

        const originalPath = activeTextEditor.document.uri.fsPath;

        // Refuse to re-process our own temp files (they start with a dot
        // and end with __scsy_send__.py) — otherwise repeated F4 would
        // chain: main.py → .main.py__scsy_send__.py → ..main.py__scsy_send__.py__scsy_send__.py
        if (path.basename(originalPath).endsWith('__scsy_send__.py')) {
            outputChannel.appendLine(`[scriptsync] ignoring own temp file: ${originalPath}`);
            return;
        }

        // --- Resolve sys.path diff lazily once per session ---
        if (!pathDiffResolveAttempted) {
            pathDiffResolveAttempted = true;
            const config = vscode.workspace.getConfiguration('python');
            const interpreterPath = config.get<string>('defaultInterpreterPath', '');
            if (interpreterPath) {
                cachedPathDiff = await resolvePathDiff();
                if (!cachedPathDiff || cachedPathDiff.length === 0) {
                    outputChannel.appendLine('[scriptsync] WARNING: could not compute sys.path diff. Sending without path injection.');
                } else {
                    outputChannel.appendLine(`[scriptsync] resolved ${cachedPathDiff.length} paths to inject`);
                }
            } else {
                outputChannel.appendLine('[scriptsync] WARNING: python.defaultInterpreterPath not set. Sending without path injection.');
            }
        }

        const client = new net.Socket();

        client.on('error', (error: Error) => {
            vscode.window.showErrorMessage('scriptsync::Run ScriptSyncStart on Rhino first.');
            console.error('Error: ', error);
        });

        client.on('data', (data: Buffer) => {
            console.log('Received data:', data.toString());
            try {
                const response = JSON.parse(data.toString());
                outputChannel.clear();
                outputChannel.show(true);
                if (response.output && (response.output as string).trim().length > 0) {
                    outputChannel.appendLine(response.output);
                }
                if (response.success) {
                    outputChannel.appendLine('scriptsync :: ok');
                } else {
                    outputChannel.appendLine('─'.repeat(60));
                    outputChannel.appendLine('scriptsync :: error');
                    outputChannel.appendLine(response.error);
                    outputChannel.appendLine('─'.repeat(60));
                    const firstLine = (response.error as string).split('\n').find((l: string) => l.trim().length > 0) ?? 'Runtime error';
                    vscode.window.showErrorMessage(`scriptsync :: ${firstLine}`);
                }
            } catch {
                outputChannel.appendLine('Raw response: ' + data.toString());
            }
            client.end(); // Close connection after response received
        });

        // Save the document, then build the temp script and send.
        // IMPORTANT: writeTempScript must run AFTER save completes so that
        // fs.readFileSync reads the latest version from disk (not VS Code's
        // in-memory buffer, which may not yet be flushed).
        activeTextEditor.document.save().then(() => {
            let payloadPath = originalPath;
            if (cachedPathDiff && cachedPathDiff.length > 0) {
                try {
                    payloadPath = writeTempScript(originalPath, cachedPathDiff);
                } catch (err: any) {
                    outputChannel.appendLine(`[scriptsync] ERROR writing temp script: ${err.message}. Falling back to original.`);
                    payloadPath = originalPath;
                }
            } else {
                outputChannel.appendLine('[scriptsync] Sending original script (no path injection).');
            }

            client.connect(port, host, () => {
                outputChannel.appendLine('Connected to Rhino');
                outputChannel.appendLine('Sending: ' + payloadPath);
                client.write(payloadPath);
            });
        });
    });
    context.subscriptions.push(rhinoSenderCmd);

    // % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % %
    // %% Grasshopper
    // % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % % %
    let ghListenerCmd = vscode.commands.registerCommand('scriptsync.toggleGH', () => {
        // const outputChannel = vscode.window.createOutputChannel('scriptsync');
        outputChannel.show(true);

        if (server) {
            silenceServer();
            return;
        }
        startServer();

        context.subscriptions.push({
            dispose: () => server?.close()
        });
    });
    context.subscriptions.push(ghListenerCmd);
}

// This method is called when your extension is deactivated
export function deactivate() {
    if (server) {
        server.close(() => {
        });
    }
}
