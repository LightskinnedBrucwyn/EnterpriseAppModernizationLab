window.batHouseTheme = {
    toggle: function () {
        var root = document.documentElement;
        var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
        root.setAttribute('data-theme', next);
        localStorage.setItem('bat-house-theme', next);
        return next;
    },
    current: function () {
        return document.documentElement.getAttribute('data-theme') || 'light';
    }
};
