// Shared client auth helpers for static pages
(function (global) {
    const AUTH_KEYS = ['token', 'username', 'role', 'userId'];

    function getApiBase() {
        return global.API_BASE || (global.location.origin + '/api');
    }

    function isValidJwt(token) {
        if (typeof token !== 'string') {
            return false;
        }

        const trimmed = token.trim();
        if (!trimmed || trimmed === 'undefined' || trimmed === 'null') {
            return false;
        }

        const parts = trimmed.split('.');
        return parts.length === 3 && parts.every(part => part.length > 0);
    }

    function clearAuthStorage() {
        AUTH_KEYS.forEach(key => localStorage.removeItem(key));
    }

    function saveAuthSession(session) {
        if (!session || !isValidJwt(session.token)) {
            throw new Error('Invalid auth session');
        }

        localStorage.setItem('token', session.token.trim());
        localStorage.setItem('username', session.username || '');
        localStorage.setItem('role', session.role || 'Farmer');
        localStorage.setItem('userId', session.userId != null ? String(session.userId) : '');
    }

    function getStoredToken() {
        const token = localStorage.getItem('token');
        return isValidJwt(token) ? token.trim() : null;
    }

    function getAuthHeaders() {
        const token = getStoredToken();
        const headers = { 'Content-Type': 'application/json' };
        if (token) {
            headers.Authorization = `Bearer ${token}`;
        }
        return headers;
    }

    async function verifyStoredSession() {
        const token = getStoredToken();
        if (!token) {
            clearAuthStorage();
            return false;
        }

        try {
            const response = await fetch(`${getApiBase()}/authentication/me`, {
                method: 'GET',
                headers: { Authorization: `Bearer ${token}` }
            });

            if (!response.ok) {
                clearAuthStorage();
                return false;
            }

            return true;
        } catch {
            return false;
        }
    }

    function extractLoginResult(data) {
        if (!data) {
            return null;
        }

        if (data.token || data.Token) {
            return {
                token: data.token || data.Token,
                username: data.username || data.Username,
                role: data.role || data.Role,
                userId: data.userId ?? data.UserId
            };
        }

        const payload = data.data || data.Data;
        if (payload && (payload.token || payload.Token)) {
            return {
                token: payload.token || payload.Token,
                username: payload.username || payload.Username,
                role: payload.role || payload.Role,
                userId: payload.userId ?? payload.UserId
            };
        }

        return null;
    }

    function unwrapApiData(body) {
        if (body == null) {
            return body;
        }

        if (Array.isArray(body)) {
            return body;
        }

        if (typeof body === 'object' && (body.data != null || body.Data != null)) {
            return body.data ?? body.Data;
        }

        return body;
    }

    global.Auth = {
        getApiBase,
        isValidJwt,
        clearAuthStorage,
        saveAuthSession,
        getStoredToken,
        getAuthHeaders,
        verifyStoredSession,
        extractLoginResult,
        unwrapApiData
    };
})(window);
