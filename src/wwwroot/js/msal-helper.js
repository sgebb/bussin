// MSAL Popup Helper for Service Bus Consent
// 
// We need a separate MSAL instance for popup consent because:
// 1. Blazor's MSAL wrapper only supports redirect mode
// 2. Azure AD doesn't allow multi-resource consent in a single redirect
// 3. Popup mode properly handles incremental consent for different resources
// 
// Both instances share the same localStorage cache, so tokens are available to both.

let popupMsalInstance = null;
window.msalConfig = null;

// Initialize MSAL instance for popup-based consent
async function initPopupMsal() {
    if (popupMsalInstance) {
        return popupMsalInstance;
    }

    if (!window.msalConfig) {
        throw new Error('MSAL configuration not set.');
    }

    if (typeof msal === 'undefined' || !msal.PublicClientApplication) {
        throw new Error('MSAL library not loaded');
    }

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

// Get the active account from the cache to avoid multiple_matching_tokens error
function getActiveAccount(msalInstance) {
    // First try to get the active account
    let account = msalInstance.getActiveAccount();

    if (!account) {
        // If no active account, get all accounts and pick the first one
        const accounts = msalInstance.getAllAccounts();
        if (accounts.length === 1) {
            account = accounts[0];
            msalInstance.setActiveAccount(account);
        } else if (accounts.length > 1) {
            // Multiple accounts - pick the first one and set it as active
            // In a multi-account scenario, you might want more sophisticated selection
            console.warn('Multiple accounts found in cache, using the first one');
            account = accounts[0];
            msalInstance.setActiveAccount(account);
        }
    }

    return account;
}

// Check if an error indicates the user needs to re-login (e.g., password changed)
function isReloginRequiredError(error) {
    if (!error) return false;

    const errorString = error.toString().toLowerCase();
    const errorCode = error.errorCode?.toLowerCase() || '';
    const errorMessage = error.errorMessage?.toLowerCase() || '';

    // Check for various indicators that require re-login
    const reloginIndicators = [
        'invalid_grant',
        'interaction_required',
        'login_required',
        'consent_required',
        'aadsts50076',  // User needs to use MFA
        'aadsts50079',  // User needs to use MFA (enrollment required)
        'aadsts50173',  // Fresh auth required
        'aadsts50078',  // MFA required
        'aadsts700084', // Refresh token expired (SPA specific)
        'aadsts65001',  // Consent required
        'aadsts54005',  // OAuth2 token already redeemed
        'aadsts70008',  // Authorization code/refresh token expired
        'aadsts70011',  // Invalid scope
        'aadsts50014',  // Max passthrough time exceeded
        'aadsts50020',  // User account from external provider doesn't exist
        'aadsts50057',  // User account disabled
        'aadsts50058',  // Silent login failed
        'aadsts50064',  // Credential validation failed
        'aadsts50126',  // Invalid username or password (could be password changed)
        'password'      // Generic password-related errors
    ];

    return reloginIndicators.some(indicator =>
        errorString.includes(indicator) ||
        errorCode.includes(indicator) ||
        errorMessage.includes(indicator)
    );
}

window.msalHelper = {
    // scope: the API scope to request
    // loginHint: optional email/username to pre-select the account
    acquireTokenPopup: async function (scope, loginHint) {
        try {
            console.log('Attempting to acquire token via popup for scope:', scope);
            if (loginHint) {
                console.log('Using login hint:', loginHint);
            }

            const msalInstance = await initPopupMsal();
            const allAccounts = msalInstance.getAllAccounts();
            console.log('Found', allAccounts.length, 'accounts in cache');

            // Try to find the right account using login hint
            let account = null;
            if (loginHint && allAccounts.length > 0) {
                account = allAccounts.find(acc =>
                    acc.username?.toLowerCase() === loginHint.toLowerCase() ||
                    acc.name?.toLowerCase() === loginHint.toLowerCase()
                );
                if (account) {
                    console.log('Found matching account:', account.username);
                    msalInstance.setActiveAccount(account);
                }
            }

            // Fall back to active account if no match found
            if (!account) {
                account = getActiveAccount(msalInstance);
            }

            if (account) {
                console.log('Using account:', account.username);
            } else {
                console.log('No account found, proceeding without account hint');
            }

            await msalInstance.acquireTokenPopup({
                scopes: [scope],
                account: account,
                loginHint: loginHint || undefined,
                prompt: 'consent',
                extraScopesToConsent: []
            });

            console.log('✓ Token acquired successfully');
            return { success: true };


        } catch (error) {
            console.error('Error during consent:', error);

            const errorString = error.toString().toLowerCase();

            // Check if this is the multiple_matching_tokens error - a page refresh fixes it
            if (errorString.includes('multiple_matching_tokens')) {
                console.log('Multiple matching tokens error detected - page refresh will fix this');
                return {
                    success: false,
                    error: error.toString(),
                    requiresRefresh: true,
                    friendlyMessage: 'Token cache conflict detected. Refreshing the page...'
                };
            }

            // Check if the error indicates re-login is required
            if (isReloginRequiredError(error)) {
                return {
                    success: false,
                    error: error.toString(),
                    requiresRelogin: true,
                    friendlyMessage: 'Your session has expired or your credentials have changed. Please sign out and sign in again.'
                };
            }

            return { success: false, error: error.toString() };
        }
    },

    // Clear the MSAL cache to force a fresh login
    clearCache: async function () {
        try {
            const msalInstance = await initPopupMsal();
            const accounts = msalInstance.getAllAccounts();

            console.log('Clearing MSAL cache for', accounts.length, 'accounts');

            // Remove all accounts from cache
            for (const account of accounts) {
                await msalInstance.logoutPopup({
                    account: account,
                    mainWindowRedirectUri: window.location.origin
                });
            }

            // Also clear localStorage entries related to MSAL
            const keysToRemove = [];
            for (let i = 0; i < localStorage.length; i++) {
                const key = localStorage.key(i);
                if (key && (key.includes('msal') || key.includes('login.windows.net') || key.includes('login.microsoftonline.com'))) {
                    keysToRemove.push(key);
                }
            }
            keysToRemove.forEach(key => localStorage.removeItem(key));

            // Reset the popup instance so it reinitializes fresh
            popupMsalInstance = null;

            console.log('✓ MSAL cache cleared');
            return { success: true };
        } catch (error) {
            console.error('Error clearing cache:', error);
            return { success: false, error: error.toString() };
        }
    },

    // Check if the current session is valid by attempting a silent token acquisition
    checkSessionValid: async function (scope) {
        try {
            const msalInstance = await initPopupMsal();
            const account = getActiveAccount(msalInstance);

            if (!account) {
                return { valid: false, reason: 'no_account' };
            }

            // Try to acquire token silently
            await msalInstance.acquireTokenSilent({
                scopes: [scope],
                account: account,
                forceRefresh: true // Force refresh to check if the refresh token is still valid
            });

            return { valid: true };
        } catch (error) {
            console.warn('Silent token acquisition failed:', error);

            if (isReloginRequiredError(error)) {
                return {
                    valid: false,
                    reason: 'relogin_required',
                    friendlyMessage: 'Your session has expired or your credentials have changed. Please sign out and sign in again.'
                };
            }

            return { valid: false, reason: 'unknown', error: error.toString() };
        }
    }
};
