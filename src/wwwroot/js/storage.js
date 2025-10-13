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
window.createProgressCallback = function(dotnetRef) {
    return (count) => dotnetRef.invokeMethodAsync('OnProgress', count);
};

// Helper to await a promise from a controller object
window.awaitControllerPromise = async function(controller) {
    return await controller.promise;
};
