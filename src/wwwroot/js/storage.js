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

// Starts a purge of a queue (subscriptionName null) or a subscription, reporting progress to a
// .NET PurgeProgressCallback. Returns the purge controller.
window.startPurge = function (callbackRef, namespace, queueOrTopicName, subscriptionName, token, fromDeadLetter, requiresSession) {
    const onProgress = (count) => callbackRef.invokeMethodAsync('OnProgress', count);
    return subscriptionName
        ? ServiceBusAPI.purgeSubscription(namespace, queueOrTopicName, subscriptionName, token, onProgress, fromDeadLetter, requiresSession)
        : ServiceBusAPI.purgeQueue(namespace, queueOrTopicName, token, onProgress, fromDeadLetter, requiresSession);
};

// Starts a search of a queue (subscriptionName null) or a subscription, reporting progress to a
// .NET SearchProgressCallback. Returns the search controller.
window.startSearch = function (callbackRef, namespace, queueOrTopicName, subscriptionName, token, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches) {
    const onProgress = (scanned, matches, newMatches) => callbackRef.invokeMethodAsync('OnProgress', scanned, matches, newMatches);
    return subscriptionName
        ? ServiceBusAPI.searchSubscriptionMessages(namespace, queueOrTopicName, subscriptionName, token, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches, onProgress)
        : ServiceBusAPI.searchQueueMessages(namespace, queueOrTopicName, token, fromDeadLetter, bodyFilter, messageIdFilter, subjectFilter, maxMessages, maxMatches, onProgress);
};

// Waits for a purge or search controller to finish. Rejections become an Error whose message
// .NET can show (rhea errors are plain objects with a condition and description).
window.awaitControllerPromise = async function (controller) {
    try {
        return await controller.promise;
    } catch (error) {
        throw new Error(error?.message || error?.description || error?.condition || String(error));
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
