// GoatCounter helper for Blazor SPA navigation
window.Analytics = {
    // Track page views for SPA navigation
    trackPageView: function (url, title) {
        if (window.goatcounter && typeof window.goatcounter.count === 'function') {
            const parsed = new URL(url);
            window.goatcounter.count({
                path: parsed.pathname + parsed.search,
                title: title
            });
        }
    },

    // Track custom events
    trackEvent: function (eventName, eventParams) {
        if (window.goatcounter && typeof window.goatcounter.count === 'function') {
            window.goatcounter.count({
                path: eventName,
                event: true
            });
        }
    }
};
