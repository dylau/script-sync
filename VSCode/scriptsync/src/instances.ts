import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

/**
 * One Rhino instance that is listening for scripts.
 *
 * This mirrors the JSON written by the Rhino plug-in
 * (CsRhino/ScriptSyncInstanceRegistry.cs).
 */
export interface RhinoInstance {
    pid: number;
    port: number;
    title: string;
    documentPath: string;
    startedAt: string;
}

/**
 * The registry file is written by the Rhino plug-in. Several Rhino instances may
 * write it concurrently, so the content may be momentarily inconsistent: the
 * plug-in writes to a temp file and then swaps it in, so a reader either sees the
 * old file or the new one. A JSON parse failure is therefore expected from time
 * to time and is simply reported as "no instances".
 */
export function registryPath(): string {
    const localAppData = process.env.LOCALAPPDATA || os.homedir();
    return path.join(localAppData, 'script-sync', 'instances.json');
}

/**
 * Read the list of registered Rhino instances.
 * Returns an empty array if the registry is missing or unreadable.
 */
export function readInstances(): RhinoInstance[] {
    const file = registryPath();
    try {
        if (!fs.existsSync(file)) {
            return [];
        }
        const content = fs.readFileSync(file, 'utf-8');
        if (!content.trim()) {
            return [];
        }
        // Strip a possible UTF-8 BOM: JSON.parse rejects it.
        const json = content.charCodeAt(0) === 0xfeff ? content.slice(1) : content;
        const parsed = JSON.parse(json);
        if (!Array.isArray(parsed)) {
            return [];
        }
        // The Rhino plug-in serializes the entries with PascalCase property names
        // (Pid, Port, Title, DocumentPath, StartedAt), so the same casing is used here.
        const instances: RhinoInstance[] = [];
        for (const entry of parsed) {
            if (!entry || typeof entry.Port !== 'number') {
                continue;
            }
            instances.push({
                pid: typeof entry.Pid === 'number' ? entry.Pid : 0,
                port: entry.Port,
                title: typeof entry.Title === 'string' ? entry.Title : '',
                documentPath: typeof entry.DocumentPath === 'string' ? entry.DocumentPath : '',
                startedAt: typeof entry.StartedAt === 'string' ? entry.StartedAt : ''
            });
        }
        return instances.sort((a, b) => a.port - b.port);
    } catch {
        // A partially written or corrupt registry is treated as "no instances".
        return [];
    }
}

/**
 * Check whether a process is still running.
 */
export function isProcessAlive(pid: number): boolean {
    if (!pid || pid <= 0) {
        return false;
    }
    try {
        // Signal 0 does not deliver a signal, it only checks that the process exists
        // and that we have the permission to signal it.
        process.kill(pid, 0);
        return true;
    } catch (err: any) {
        // EPERM means the process exists but is owned by someone else.
        return err && err.code === 'EPERM';
    }
}

/**
 * The registered instances whose process is still alive.
 *
 * Stale entries are pruned from the file as well, so that a Rhino closed
 * without running ScriptSyncStop does not linger forever.
 */
export function readLiveInstances(): RhinoInstance[] {
    const instances = readInstances();
    const live = instances.filter((i) => isProcessAlive(i.pid));

    if (live.length !== instances.length) {
        try {
            writeInstances(live);
        } catch {
            // Pruning is a convenience, a failure must not break the caller.
        }
    }
    return live;
}

/**
 * Write the registry back, keeping the format used by the Rhino plug-in.
 */
function writeInstances(instances: RhinoInstance[]): void {
    const file = registryPath();
    fs.mkdirSync(path.dirname(file), { recursive: true });
    const payload = instances.map((i) => ({
        Pid: i.pid,
        Port: i.port,
        Title: i.title,
        DocumentPath: i.documentPath,
        StartedAt: i.startedAt
    }));
    const temp = file + '.tmp';
    fs.writeFileSync(temp, JSON.stringify(payload, null, 2), 'utf-8');
    if (fs.existsSync(file)) {
        fs.unlinkSync(file);
    }
    fs.renameSync(temp, file);
}

/**
 * A human readable label for an instance, used in the quick pick.
 */
export function instanceLabel(instance: RhinoInstance): string {
    if (instance.title) {
        return `${instance.title} — port ${instance.port}`;
    }
    return `Rhino (pid ${instance.pid}) — port ${instance.port}`;
}
