// The app's only MSAL instance: sign-in, sign-out, the redirect callback and every token request.
//
// Entra issues each access token for a single resource, and this app needs two: Azure Resource Manager
// (to discover namespaces) and Service Bus (to work with messages), in whichever tenant is selected.
// Consent, however, can cover several resources in one prompt: interactive requests ask for one resource
// in `scopes` and the other in `extraScopesToConsent`, after which both tokens can be acquired silently.
//
// Needs msal-browser 2.39 (see index.html). Earlier 2.x versions throw multiple_matching_tokens when the cache
// holds duplicate entries, which no sign-in fixes; 2.39 clears the duplicates and refreshes instead.

const CALLBACK_PATH = '/authentication/login-callback';
const LOGOUT_CALLBACK_PATH = '/authentication/logout-callback';
const RETURN_URL_KEY = 'bussin.auth.returnUrl';

let msalInstance = null;
let resolveReady, rejectReady;
const ready = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });

// MSAL popups and silent iframes also land on the redirect page and boot the app. The response in their
// URL belongs to the window that opened them, which reads it from there, so this window must leave it alone.
function isAuthChildWindow() {
    return window.self !== window.top ||
        (!!window.opener && window.opener !== window && window.name.startsWith('msal.'));
}

// Entra homeAccountId is "<objectId>.<homeTenantId>". Visiting a guest tenant adds another account entry for
// the same user carrying the guest tenantId; MSAL reads cached tokens by that tenantId, so pick deliberately.
function isHomeAccount(account) {
    return !!account.homeAccountId && !!account.tenantId &&
        account.homeAccountId.toLowerCase().endsWith('.' + account.tenantId.toLowerCase());
}

function homeAccount() {
    const accounts = msalInstance.getAllAccounts();
    return accounts.find(isHomeAccount) || accounts[0] || null;
}

function sameTenant(a, b) {
    return (a || '').toLowerCase() === (b || '').toLowerCase();
}

function authorityFor(tenantId) {
    return tenantId ? `https://login.microsoftonline.com/${tenantId}` : undefined;
}

function messageOf(error) {
    return error.errorMessage || error.message || String(error);
}

// Maps a silent-acquisition failure to what the user has to do about it.
// The problem names match the C# AccessProblem enum.
function toProblem(error) {
    const text = [error.errorCode, error.subError, error.errorMessage, error.message].join(' ');

    // AADSTS65001: nobody has consented to this resource for this app in this tenant yet.
    if (/consent_required|AADSTS65001/i.test(text)) {
        return { problem: 'ConsentRequired', message: messageOf(error) };
    }

    // The sign-in is no longer valid: password changed or expired, refresh token revoked or expired,
    // MFA or another sign-in requirement, or the silent iframe fallback found no session (common when
    // third-party cookies are blocked).
    if ((msal.InteractionRequiredAuthError && error instanceof msal.InteractionRequiredAuthError) ||
        /interaction_required|login_required|invalid_grant|no_tokens_found|refresh_token_expired|monitor_window_timeout/i.test(text)) {
        return { problem: 'SignInRequired', message: messageOf(error) };
    }

    return { problem: 'Failed', message: messageOf(error) };
}

window.msalHelper = {
    // Creates the instance, processes a returning sign-in redirect, and returns the signed-in user's
    // ID token claims (null when signed out). Every other function waits for this to finish.
    init: function (config) {
        if (!msalInstance) {
            msalInstance = new msal.PublicClientApplication({
                auth: {
                    clientId: config.clientId,
                    authority: config.authority,
                    redirectUri: window.location.origin + CALLBACK_PATH,
                    navigateToLoginRequestUrl: false
                },
                cache: {
                    cacheLocation: 'localStorage',
                    storeAuthStateInCookie: false
                }
            });

            (async () => {
                await msalInstance.initialize();
                if (!isAuthChildWindow()) {
                    try {
                        await msalInstance.handleRedirectPromise();
                    } catch (error) {
                        // e.g. the user cancelled at Entra or declined consent; they stay signed out.
                        console.error('[msal-helper] Sign-in redirect failed:', error);
                    }
                }
            })().then(resolveReady, rejectReady);
        }

        return ready.then(() => {
            const home = homeAccount();
            return home ? (home.idTokenClaims ?? { name: home.name, preferred_username: home.username }) : null;
        });
    },

    // Returns { token, expiresOn } or { problem, message }.
    acquireTokenSilent: async function (scope, tenantId) {
        await ready;
        try {
            const home = homeAccount();
            if (!home) {
                return { problem: 'SignInRequired', message: 'Not signed in.' };
            }

            const request = {
                scopes: [scope],
                account: (tenantId && msalInstance.getAllAccounts().find(a => sameTenant(a.tenantId, tenantId))) || home,
                authority: authorityFor(tenantId)
            };

            let response = await msalInstance.acquireTokenSilent(request);

            // Until the user has an account entry for a guest tenant, the cache lookup goes through the home
            // account and can return a home-tenant token. Skip the cache to get one from the requested tenant.
            if (tenantId && !sameTenant(response.tenantId, tenantId)) {
                response = await msalInstance.acquireTokenSilent({ ...request, forceRefresh: true });
                if (!sameTenant(response.tenantId, tenantId)) {
                    return { problem: 'SignInRequired', message: `Got a token for tenant ${response.tenantId} instead of ${tenantId}.` };
                }
            }

            return { token: response.accessToken, expiresOn: response.expiresOn?.toISOString() };
        } catch (error) {
            return toProblem(error);
        }
    },

    // One popup that signs the user in again if needed and asks consent for `scope` and `extraScopesToConsent`
    // together. Returns null on success, otherwise a message for the user.
    acquireTokenPopup: async function (scope, extraScopesToConsent, tenantId) {
        await ready;
        try {
            const home = homeAccount();
            await msalInstance.acquireTokenPopup({
                scopes: [scope],
                extraScopesToConsent,
                authority: authorityFor(tenantId),
                // Continue as the signed-in user rather than asking which account to use.
                loginHint: home?.username,
                prompt: home ? undefined : 'select_account'
            });
            return null;
        } catch (error) {
            console.error('[msal-helper] Popup failed:', error);
            switch (error.errorCode) {
                case 'user_cancelled':
                    return 'The sign-in window was closed before it finished.';
                case 'popup_window_error':
                case 'empty_window_error':
                    return 'The browser blocked the sign-in window. Allow popups for this site and try again.';
                default:
                    return messageOf(error);
            }
        }
    },

    loginRedirect: async function (scope, extraScopesToConsent) {
        await ready;
        const here = window.location.pathname + window.location.search;
        sessionStorage.setItem(RETURN_URL_KEY, here.startsWith('/authentication/') ? '/' : here);
        await msalInstance.loginRedirect({
            scopes: [scope],
            extraScopesToConsent,
            prompt: 'select_account'
        });
    },

    // Clears every cached account (home and guest tenants) and signs out of Entra.
    logoutRedirect: async function () {
        await ready;
        await msalInstance.logoutRedirect({
            postLogoutRedirectUri: window.location.origin + LOGOUT_CALLBACK_PATH
        });
    },

    // Where to go after landing on a callback page; null inside a popup or iframe, which must stay put.
    takeReturnUrl: async function () {
        await ready;
        if (isAuthChildWindow()) return null;
        const returnUrl = sessionStorage.getItem(RETURN_URL_KEY) || '/';
        sessionStorage.removeItem(RETURN_URL_KEY);
        return returnUrl;
    }
};
