{
    let theme = null;
    try {
        theme = localStorage.getItem('chaos-theme');
    } catch {
    }
    if (theme === 'light' || theme === 'dark') {
        document.documentElement.dataset.theme = theme;
    }
}
