// MSAL Popup Helper - Keep it simple but solve ambiguity
let popupMsalInstance = null;
window.msalConfig = null;

async function initPopupMsal() {
    if (popupMsalInstance) return popupMsalInstance;
    if (!window.msalConfig) throw new Error('MSAL configuration not set.');

    const config = {
        auth: {
            clientId: window.msalConfig.clientId,
            authority: window.msalConfig.authority,
            redirectUri: window.location.origin + '/authentication/login-callback'
        },
        cache: {
            cacheLocation: 'localStorage',
            storeAuthStateInCookie: false
        }
    };

    popupMsalInstance = new msal.PublicClientApplication(config);
    await popupMsalInstance.initialize();
    return popupMsalInstance;
}

window.msalHelper = {
    setConfig: function (config) {
        window.msalConfig = config;
    },

    clearCache: function () {
        Object.keys(localStorage).forEach(key => {
            if (key.includes('msal.')) {
                localStorage.removeItem(key);
            }
        });
    },

    acquireTokenPopup: async function (scopes, tenantId, loginHint) {
        try {
            const msalInstance = await initPopupMsal();
            const request = {
                scopes: Array.isArray(scopes) ? scopes : [scopes],
                loginHint: loginHint || undefined,
                authority: tenantId ? `https://login.microsoftonline.com/${tenantId}` : undefined,
                prompt: 'select_account'
            };

            console.log('[msal-helper] acquireTokenPopup triggered');

            try {
                await msalInstance.acquireTokenPopup(request);
            } catch (msalError) {
                const errStr = msalError.toString();

                // If MSAL is confused by multiple accounts matching the hint, try one more time without the hint.
                // This forces the user to pick from the account list manually which always works.
                if (errStr.includes('multiple_matching_tokens')) {
                    console.warn('[msal-helper] Multiple matching tokens in cache lookup. Retrying without loginHint...');
                    delete request.loginHint;
                    await msalInstance.acquireTokenPopup(request);
                } else {
                    throw msalError;
                }
            }

            return { success: true };
        } catch (error) {
            console.error('[msal-helper] Popup error:', error);
            return { success: false, error: error.toString() };
        }
    },

    acquireTokenSilent: async function (scope, tenantId) {
        try {
            const msalInstance = await initPopupMsal();
            const accounts = msalInstance.getAllAccounts();

            if (!accounts || accounts.length === 0) {
                return { success: false, error: "MSAL_NO_ACCOUNTS" };
            }

            // Find an account that belongs to the target tenant if possible
            const account = (tenantId ? accounts.find(a => a.tenantId === tenantId) : null) || accounts[0];

            const request = {
                scopes: [scope],
                account: account,
                authority: tenantId ? `https://login.microsoftonline.com/${tenantId}` : undefined,
                forceRefresh: false
            };

            try {
                let response = await msalInstance.acquireTokenSilent(request);

                // VERIFICATION: Did we actually get a token for the tenant we asked for?
                // MSAL silent calls sometimes return a valid token for a different tenant from the cache.
                if (tenantId && response.tenantId && response.tenantId.toLowerCase() !== tenantId.toLowerCase()) {
                    console.warn(`[msal-helper] Wrong tenant returned (${response.tenantId} != ${tenantId}). Forcing refresh...`);
                    request.forceRefresh = true;
                    response = await msalInstance.acquireTokenSilent(request);

                    // If still wrong after refresh, we need interaction
                    if (response.tenantId.toLowerCase() !== tenantId.toLowerCase()) {
                        return { success: false, error: "MSAL_WRONG_TENANT" };
                    }
                }

                return {
                    success: true,
                    token: response.accessToken,
                    expiresOn: response.expiresOn.toISOString()
                };
            } catch (silentErr) {
                const errStr = silentErr.toString();
                // Treat ambiguity and token endpoint failures as "Interaction Required"
                // This pulls those scary JSON errors out of the console and triggers the prompt.
                if (errStr.includes('interaction_required') ||
                    errStr.includes('multiple_matching_tokens') ||
                    errStr.includes('AADSTS50076') ||
                    errStr.includes('invalid_grant') ||
                    errStr.includes('400')) {
                    return { success: false, error: "MSAL_INTERACTION_REQUIRED" };
                }
                return { success: false, error: errStr };
            }
        } catch (error) {
            return { success: false, error: error.toString() };
        }
    }
};
