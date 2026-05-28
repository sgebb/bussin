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
    const type = args[args.length - 1]; // Last argument is type

    try {
        if (type === 'queue') {
            const [namespace, queueName, token, callbackRef, fromDeadLetter] = args;
            const progressCallback = (count) => callbackRef.invokeMethodAsync('OnProgress', count);
            console.log('[startPurgeWithProgress] Starting queue purge:', queueName);
            return await ServiceBusAPI.purgeQueue(namespace, queueName, token, progressCallback, fromDeadLetter);
        } else {
            const [namespace, topicName, subscriptionName, token, callbackRef, fromDeadLetter] = args;
            const progressCallback = (count) => callbackRef.invokeMethodAsync('OnProgress', count);
            console.log('[startPurgeWithProgress] Starting subscription purge:', topicName, subscriptionName);
            return await ServiceBusAPI.purgeSubscription(namespace, topicName, subscriptionName, token, progressCallback, fromDeadLetter);
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
// Queue signature: (namespace, queueName, null, null, token, callbackRef, fromDeadLetter, "queue", bodyFilter, messageIdFilter, subjectFilter, maxMessages)
// Subscription signature: (namespace, null, topicName, subscriptionName, token, callbackRef, fromDeadLetter, "subscription", bodyFilter, messageIdFilter, subjectFilter, maxMessages)
window.startSearchWithProgress = async function (namespace, queueName, topicName, subscriptionName, token, callbackRef, fromDeadLetter, type, bodyFilter, messageIdFilter, subjectFilter, maxMessages) {
    try {
        const progressCallback = (scanned, matches, newMatches) => {
            callbackRef.invokeMethodAsync('OnProgress', scanned, matches, newMatches);
        };

        if (type === 'queue') {
            console.log('[startSearchWithProgress] Starting queue search:', queueName);
            return await ServiceBusAPI.searchQueueMessages(namespace, queueName, token, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, progressCallback);
        } else {
            console.log('[startSearchWithProgress] Starting subscription search:', topicName, subscriptionName);
            return await ServiceBusAPI.searchSubscriptionMessages(namespace, topicName, subscriptionName, token, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, progressCallback);
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
