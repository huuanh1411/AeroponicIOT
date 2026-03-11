// Optional override:
// window.APP_CONFIG = { API_BASE: 'http://your-server:5062/api' };
(function () {
    var defaultBase = window.location.origin + '/api';
    var appConfig = window.APP_CONFIG || {};
    var base = appConfig.API_BASE || window.API_BASE || defaultBase;
    base = base.replace(/\/+$/, '');
    window.API_BASE = base;
})();
