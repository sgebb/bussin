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
