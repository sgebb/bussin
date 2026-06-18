// LocalStorage wrapper for Blazor interop
window.storageHelper = {
    getItem: function (key) {
        const value = localStorage.getItem(key);
        return value;
    },
    setItem: function (key, value) {
        localStorage.setItem(key, value);
    },
    removeItem: function (key) {
        localStorage.removeItem(key);
    },
    clear: function () {
        localStorage.clear();
    }
};

// Helper to create callback wrappers for .NET interop
window.createProgressCallback = function (dotnetRef) {
    return (count) => dotnetRef.invokeMethodAsync('OnProgress', count);
};

// Helper to start purge with progress callback
// Queue signature: (namespace, queueName, token, callbackRef, fromDeadLetter, "queue")
// Subscription signature: (namespace, topicName, subscriptionName, token, callbackRef, fromDeadLetter, "subscription")
window.startPurgeWithProgress = async function (...args) {
    const hasSessionParam = typeof args[args.length - 1] === 'boolean';
    const requiresSession = hasSessionParam ? args[args.length - 1] : false;
    const type = hasSessionParam ? args[args.length - 2] : args[args.length - 1];

    try {
        if (type === 'queue') {
            const [namespace, queueName, token, callbackRef, fromDeadLetter] = args;
            const progressCallback = (count) => callbackRef.invokeMethodAsync('OnProgress', count);
            console.log('[startPurgeWithProgress] Starting queue purge:', queueName, 'requiresSession:', requiresSession);
            return await ServiceBusAPI.purgeQueue(namespace, queueName, token, progressCallback, fromDeadLetter, requiresSession);
        } else {
            const [namespace, topicName, subscriptionName, token, callbackRef, fromDeadLetter] = args;
            const progressCallback = (count) => callbackRef.invokeMethodAsync('OnProgress', count);
            console.log('[startPurgeWithProgress] Starting subscription purge:', topicName, subscriptionName, 'requiresSession:', requiresSession);
            return await ServiceBusAPI.purgeSubscription(namespace, topicName, subscriptionName, token, progressCallback, fromDeadLetter, requiresSession);
        }
    } catch (error) {
        console.error('[startPurgeWithProgress] Error:', error);
        throw new Error(error.message || error.toString());
    }
};

// Helper to await a promise from a controller object
window.awaitControllerPromise = async function (controller) {
    try {
        return await controller.promise;
    } catch (error) {
        console.error("[awaitControllerPromise] Error:", error);

        // Extract error message from various error formats (especially RHEA errors)
        let errorMsg = 'Unknown error';
        if (error) {
            if (typeof error === 'string') {
                errorMsg = error;
            } else if (error.message) {
                errorMsg = error.message;
            } else if (error.description) {
                errorMsg = error.description;
            } else if (error.condition) {
                // RHEA AMQP errors use 'condition' field
                errorMsg = error.condition;
            } else if (error.toString && error.toString() !== '[object Object]') {
                errorMsg = error.toString();
            }
        }

        console.error("[awaitControllerPromise] Extracted error message:", errorMsg);
        throw new Error(errorMsg);
    }
};

// Helper to start background search with progress callback
// Queue signature: (namespace, queueName, null, null, token, callbackRef, fromDeadLetter, "queue", bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches)
// Subscription signature: (namespace, null, topicName, subscriptionName, token, callbackRef, fromDeadLetter, "subscription", bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches)
window.startSearchWithProgress = async function (namespace, queueName, topicName, subscriptionName, token, callbackRef, fromDeadLetter, type, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches) {
    try {
        const progressCallback = (scanned, matches, newMatches) => {
            callbackRef.invokeMethodAsync('OnProgress', scanned, matches, newMatches);
        };

        if (type === 'queue') {
            console.log('[startSearchWithProgress] Starting queue search:', queueName);
            return await ServiceBusAPI.searchQueueMessages(namespace, queueName, token, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches, progressCallback);
        } else {
            console.log('[startSearchWithProgress] Starting subscription search:', topicName, subscriptionName);
            return await ServiceBusAPI.searchSubscriptionMessages(namespace, topicName, subscriptionName, token, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches, progressCallback);
        }
    } catch (error) {
        console.error('[startSearchWithProgress] Error:', error);
        throw new Error(error.message || error.toString());
    }
};

// Helper to await search result from a search controller
window.awaitSearchResult = async function (controller) {
    try {
        return await controller.promise;
    } catch (error) {
        console.error("[awaitSearchResult] Error:", error);

        let errorMsg = 'Unknown error';
        if (error) {
            if (typeof error === 'string') {
                errorMsg = error;
            } else if (error.message) {
                errorMsg = error.message;
            } else if (error.description) {
                errorMsg = error.description;
            } else if (error.toString && error.toString() !== '[object Object]') {
                errorMsg = error.toString();
            }
        }

        throw new Error(errorMsg);
    }
};

// Helper to download a text file
window.downloadFile = function (fileName, contentType, contentString) {
    const blob = new Blob([contentString], { type: contentType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
