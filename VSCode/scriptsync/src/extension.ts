// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import * as net from 'net';
import * as path from 'path';

let server: net.Server | null = null;
let connections: net.Socket[] = [];
let isLogging = false;

const outputChannel = vscode.window.createOutputChannel('scriptsync');
let lastReceivedMessage: { guid: any; } | null = null;

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

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {
    //%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    //%% Rhino
    //%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    let rhinoSenderCmd = vscode.commands.registerCommand('scriptsync.sendPath', () => {
        console.log('scriptsync.sendPath command triggered');
        outputChannel.appendLine('F4 pressed - sending to Rhino');
        vscode.window.showInformationMessage('scriptsync::Sending to Rhino...');

        // port and ip address of the server
        const port = 58259;
        const host = '127.0.0.1';

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

        const client = new net.Socket();

        client.on('error', (error) => {
            vscode.window.showErrorMessage('scriptsync::Run ScriptSyncStart on Rhino first.');
            console.error('Error: ', error);
        });

        client.on('data', (data) => {
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
        });

        activeTextEditor.document.save().then(() => {
            client.connect(58259, '127.0.0.1', () => {
                outputChannel.appendLine('Connected to Rhino');
                const activeDocumentPath = activeTextEditor.document.uri.path;
                outputChannel.appendLine('Sending: ' + activeDocumentPath);
                client.write(activeDocumentPath);
            });
        });
    });
    context.subscriptions.push(rhinoSenderCmd);

    //%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    //%% Grasshopper
    //%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
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

