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

window.msalHelper = {
    acquireTokenPopup: async function(scope) {
        try {
            console.log('Attempting to acquire token via popup for scope:', scope);

            const msalInstance = await initPopupMsal();

            await msalInstance.acquireTokenPopup({
                scopes: [scope],
                account: null,
                prompt: 'consent',
                extraScopesToConsent: []
            });

            console.log('✓ Token acquired successfully - reloading page');
            window.location.reload();

        } catch (error) {
            console.error('Error during consent, reloading:', error);
            window.location.reload();
        }
    }
};
