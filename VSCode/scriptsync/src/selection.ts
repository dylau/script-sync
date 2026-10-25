import * as vscode from 'vscode';
import { instanceLabel, readLiveInstances, RhinoInstance } from './instances';

/**
 * Notify the user that the registry is empty and the default port is used instead.
 */
function outputChannelFallback(port: number): void {
    console.log(
        `[scriptsync] no Rhino instance found in the registry, falling back to the default port ${port}`
    );
}

/**
 * The key used to remember the selected instance.
 *
 * The selection is stored per workspace (so each folder can target its own Rhino)
 * with a global fallback (so a loose file, which has no real workspace, still
 * remembers the last used instance).
 */
const WORKSPACE_SELECTION_KEY = 'scriptsync.selectedInstance';
const GLOBAL_SELECTION_KEY = 'scriptsync.selectedInstance.global';

/**
 * The remembered selection: the port of the chosen Rhino instance.
 */
interface Selection {
    port: number;
    pid: number;
}

/**
 * Read the remembered instance, preferring the workspace specific one.
 */
function readSelection(context: vscode.ExtensionContext): Selection | undefined {
    const fromWorkspace = context.workspaceState.get<Selection>(WORKSPACE_SELECTION_KEY);
    if (fromWorkspace && typeof fromWorkspace.port === 'number') {
        return fromWorkspace;
    }
    const fromGlobal = context.globalState.get<Selection>(GLOBAL_SELECTION_KEY);
    if (fromGlobal && typeof fromGlobal.port === 'number') {
        return fromGlobal;
    }
    return undefined;
}

/**
 * Remember the chosen instance, both per workspace and globally.
 */
function writeSelection(context: vscode.ExtensionContext, selection: Selection): void {
    context.workspaceState.update(WORKSPACE_SELECTION_KEY, selection);
    context.globalState.update(GLOBAL_SELECTION_KEY, selection);
}

/**
 * Let the user pick one of the running Rhino instances.
 * Returns the chosen instance, or undefined when the user cancels.
 */
export async function pickInstance(context: vscode.ExtensionContext): Promise<RhinoInstance | undefined> {
    const instances = readLiveInstances();

    if (instances.length === 0) {
        vscode.window.showWarningMessage(
            'scriptsync::No Rhino instance is listening. Run ScriptSyncStart in Rhino first.'
        );
        return undefined;
    }

    if (instances.length === 1) {
        return instances[0];
    }

    const items = instances.map((instance) => ({
        label: instanceLabel(instance),
        description: instance.documentPath,
        instance
    }));

    const picked = await vscode.window.showQuickPick(items, {
        title: 'Select the Rhino instance to send to',
        placeHolder: 'Pick a Rhino instance',
        matchOnDescription: true
    });

    return picked?.instance;
}

/**
 * Resolve which Rhino instance should receive the script.
 *
 * The remembered selection wins when that instance is still alive. Otherwise, and
 * only when the choice is unambiguous, a single running instance is used silently.
 * When several instances are running and none is selected, the user is asked.
 */
export async function resolveTargetInstance(
    context: vscode.ExtensionContext
): Promise<RhinoInstance | undefined> {
    const instances = readLiveInstances();

    if (instances.length === 0) {
        // The registry may be missing (a Rhino built before the multi-instance
        // feature, or one that failed to register). Fall back to the default port.
        const defaultPort = vscode.workspace
            .getConfiguration('scriptsync')
            .get<number>('defaultPort', 58258);
        if (defaultPort > 0) {
            outputChannelFallback(defaultPort);
            return { pid: 0, port: defaultPort, title: '', documentPath: '', startedAt: '' };
        }
        vscode.window.showWarningMessage(
            'scriptsync::No Rhino instance is listening. Run ScriptSyncStart in Rhino first.'
        );
        return undefined;
    }

    const selection = readSelection(context);
    if (selection) {
        const remembered = instances.find((i) => i.port === selection.port);
        if (remembered) {
            return remembered;
        }
        // The remembered instance is gone. Other instances may still be running.
        const others = instances.length;
        if (others === 1) {
            return instances[0];
        }
        const replace = await vscode.window.showWarningMessage(
            `scriptsync::The selected Rhino instance (port ${selection.port}) is no longer running.`,
            'Select another instance'
        );
        if (replace) {
            return pickInstance(context);
        }
        return undefined;
    }

    if (instances.length === 1) {
        return instances[0];
    }

    return pickInstance(context);
}

/**
 * Remember an instance as the current target.
 */
export async function rememberInstance(
    context: vscode.ExtensionContext,
    instance: RhinoInstance
): Promise<void> {
    writeSelection(context, { port: instance.port, pid: instance.pid });
}

/**
 * Forget the remembered instance.
 */
export async function forgetInstance(context: vscode.ExtensionContext): Promise<void> {
    await context.workspaceState.update(WORKSPACE_SELECTION_KEY, undefined);
    await context.globalState.update(GLOBAL_SELECTION_KEY, undefined);
}
