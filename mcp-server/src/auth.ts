/**
 * Token acquisition for the Service Bus data plane and Azure Resource Manager.
 *
 * Entra ID is the primary path: outside the browser there is no MSAL popup, so we
 * lean on DefaultAzureCredential, which picks up an existing `az login`, a managed
 * identity, or environment-provided service-principal credentials.
 *
 * Connection strings remain supported for namespaces the signed-in identity cannot
 * see (a shared sandbox, another tenant), but they are not the expected setup.
 */

import { createHmac } from 'node:crypto';
import { DefaultAzureCredential, type TokenCredential } from '@azure/identity';
import type { Config } from './config.js';

export const SERVICE_BUS_SCOPE = 'https://servicebus.azure.net/.default';
export const ARM_SCOPE = 'https://management.azure.com/.default';

interface ParsedConnectionString {
    endpointHost: string;
    keyName: string;
    key: string;
}

function parseConnectionString(cs: string): ParsedConnectionString {
    const parts = Object.fromEntries(
        cs.split(';').filter(Boolean).map(p => {
            const eq = p.indexOf('=');
            return [p.slice(0, eq).trim(), p.slice(eq + 1).trim()];
        })
    );
    const endpoint = parts['Endpoint'];
    const keyName = parts['SharedAccessKeyName'];
    const key = parts['SharedAccessKey'];
    if (!endpoint || !keyName || !key) {
        throw new Error('Connection string must contain Endpoint, SharedAccessKeyName and SharedAccessKey.');
    }
    return { endpointHost: new URL(endpoint).hostname, keyName, key };
}

/** Build a SAS token in the form the Service Bus CBS endpoint expects. */
function createSasToken(host: string, keyName: string, key: string, ttlSeconds = 3600): string {
    const uri = encodeURIComponent(`https://${host}/`);
    const expiry = Math.floor(Date.now() / 1000) + ttlSeconds;
    const signature = createHmac('sha256', key).update(`${uri}\n${expiry}`).digest('base64');
    return `SharedAccessSignature sr=${uri}&sig=${encodeURIComponent(signature)}&se=${expiry}&skn=${keyName}`;
}

export class TokenProvider {
    private credential: TokenCredential | null = null;
    private readonly cache = new Map<string, { token: string; expiresAt: number }>();

    constructor(private readonly config: Config) {}

    /** True when we are authenticating with a connection string rather than an identity. */
    get usesConnectionString(): boolean {
        return this.config.connectionString !== null;
    }

    private getCredential(): TokenCredential {
        if (!this.credential) {
            this.credential = new DefaultAzureCredential(
                this.config.tenantId ? { tenantId: this.config.tenantId } : {}
            );
        }
        return this.credential;
    }

    /** A bearer token (or SAS token) valid for the Service Bus data plane. */
    async getDataPlaneToken(): Promise<string> {
        // The simulator never validates the token, and demo mode must not require an Azure sign-in.
        if (this.config.demo) return 'demo-token';
        if (this.config.connectionString) {
            const { endpointHost, keyName, key } = parseConnectionString(this.config.connectionString);
            return createSasToken(endpointHost, keyName, key);
        }
        return this.getAadToken(SERVICE_BUS_SCOPE);
    }

    /** A bearer token for ARM, used to enumerate namespaces and entities. */
    async getArmToken(): Promise<string> {
        if (this.config.demo) {
            throw new Error('Entity discovery is not available in --demo mode; only peek and search run against the simulator.');
        }
        if (this.config.connectionString) {
            throw new Error(
                'Entity discovery needs Entra ID. Sign in with `az login` (or drop --connection-string) ' +
                'to list namespaces, queues and topics.'
            );
        }
        return this.getAadToken(ARM_SCOPE);
    }

    /** The credential itself, for SDK clients that take one directly. */
    getTokenCredential(): TokenCredential {
        if (this.config.connectionString) {
            throw new Error('Entity discovery needs Entra ID, not a connection string.');
        }
        return this.getCredential();
    }

    private async getAadToken(scope: string): Promise<string> {
        const cached = this.cache.get(scope);
        // Refresh a minute early so a token cannot expire mid-operation.
        if (cached && cached.expiresAt > Date.now() + 60_000) return cached.token;

        const result = await this.getCredential().getToken(scope);
        if (!result) {
            throw new Error(
                `Could not acquire a token for ${scope}. Run \`az login\`, or set AZURE_TENANT_ID, ` +
                'AZURE_CLIENT_ID and AZURE_CLIENT_SECRET for a service principal.'
            );
        }
        this.cache.set(scope, { token: result.token, expiresAt: result.expiresOnTimestamp });
        return result.token;
    }
}
