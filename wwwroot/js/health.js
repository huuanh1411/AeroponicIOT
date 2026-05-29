const HEALTH_URL = '/health';

function toVietnameseHealthStatus(value) {
    if (!value) {
        return '';
    }

    const map = {
        Healthy: 'Khỏe',
        Unhealthy: 'Không khỏe',
        Degraded: 'Suy giảm',
        Connected: 'Đã kết nối',
        Disconnected: 'Mất kết nối',
        Running: 'Đang chạy',
        Stopped: 'Đã dừng'
    };

    return map[value] || value;
}

function findCheck(checks, name) {
    if (!Array.isArray(checks)) {
        return null;
    }

    return checks.find(check =>
        check &&
        typeof check.name === 'string' &&
        check.name.toLowerCase() === name.toLowerCase()
    ) || null;
}

function formatCheckStatus(check) {
    if (!check) {
        return { label: 'Không rõ', isHealthy: false, detail: '' };
    }

    const label = toVietnameseHealthStatus(check.status) || check.status || 'Không rõ';
    const isHealthy = check.status === 'Healthy';
    const detail = check.description || check.error || '';

    return { label, isHealthy, detail };
}

function applyStatusColor(element, isHealthy) {
    element.style.color = isHealthy ? '#4CAF50' : '#f44336';
}

document.addEventListener('DOMContentLoaded', () => {
    loadHealth();
    setInterval(loadHealth, 10000);
});

async function loadHealth() {
    const statusEl = document.getElementById('healthStatus');
    const dbEl = document.getElementById('dbStatus');
    const mqttEl = document.getElementById('mqttStatus');
    const tsEl = document.getElementById('healthTimestamp');
    const rawEl = document.getElementById('healthRaw');
    const lastCheckedEl = document.getElementById('healthLastChecked');

    try {
        const response = await fetch(HEALTH_URL, { method: 'GET' });
        const text = await response.text();

        let data = {};
        try {
            data = text ? JSON.parse(text) : {};
        } catch {
            data = {};
        }

        const ok = response.ok;
        const checks = data.checks || [];

        const overall = formatCheckStatus({ status: data.status });
        const database = formatCheckStatus(findCheck(checks, 'database'));
        const mqtt = formatCheckStatus(findCheck(checks, 'mqtt'));

        statusEl.textContent = overall.label || (ok ? 'Không rõ' : 'Không khỏe');
        dbEl.textContent = database.label;
        mqttEl.textContent = mqtt.label;

        if (data.timestamp) {
            tsEl.textContent = new Date(data.timestamp).toLocaleString('vi-VN');
        } else {
            tsEl.textContent = 'Không rõ';
        }

        lastCheckedEl.textContent = `Lần kiểm tra cuối: ${new Date().toLocaleString('vi-VN')}`;
        rawEl.textContent = text || '(phản hồi rỗng)';

        applyStatusColor(statusEl, ok && data.status === 'Healthy');
        applyStatusColor(dbEl, database.isHealthy);
        applyStatusColor(mqttEl, mqtt.isHealthy);
    } catch (err) {
        statusEl.textContent = 'Không thể kết nối';
        dbEl.textContent = 'Không rõ';
        mqttEl.textContent = 'Không rõ';
        tsEl.textContent = 'Không rõ';
        lastCheckedEl.textContent = `Lần kiểm tra cuối: ${new Date().toLocaleString('vi-VN')}`;
        rawEl.textContent = `Yêu cầu thất bại: ${err}`;

        applyStatusColor(statusEl, false);
        applyStatusColor(dbEl, false);
        applyStatusColor(mqttEl, false);
    }
}

function goBackToDashboard() {
    window.location.href = 'index.html';
}
