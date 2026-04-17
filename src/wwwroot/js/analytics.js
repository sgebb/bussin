// Google Analytics helper for Blazor SPA navigation
window.Analytics = {
    // Track page views for SPA navigation
    trackPageView: function(url, title) {
        if (typeof gtag !== 'undefined') {
            gtag('event', 'page_view', {
                page_title: title,
                page_location: url,
                page_path: new URL(url).pathname
            });
        }
    },
    
    // Track custom events
    trackEvent: function(eventName, eventParams) {
        if (typeof gtag !== 'undefined') {
            gtag('event', eventName, eventParams);
        }
    },
    
    // Track exceptions/errors
    trackException: function(description, fatal) {
        if (typeof gtag !== 'undefined') {
            gtag('event', 'exception', {
                description: description,
                fatal: fatal || false
            });
        }
    },
    
    // Track timing (e.g., API calls, operations)
    trackTiming: function(category, variable, value, label) {
        if (typeof gtag !== 'undefined') {
            gtag('event', 'timing_complete', {
                name: variable,
                value: value,
                event_category: category,
                event_label: label
            });
        }
    }
};
