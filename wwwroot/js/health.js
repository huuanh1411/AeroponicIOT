const HEALTH_URL = '/health';

function toVietnameseHealthStatus(value) {
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

document.addEventListener('DOMContentLoaded', () => {
    loadHealth();
    // Auto-refresh every 10 seconds
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
            // Non-JSON body; show as raw text only.
            data = {};
        }

        const ok = response.ok;

        statusEl.textContent = toVietnameseHealthStatus(data.status) || (ok ? 'Không rõ' : 'Không khỏe');
        dbEl.textContent = toVietnameseHealthStatus(data.db) || 'Không rõ';
        mqttEl.textContent = toVietnameseHealthStatus(data.mqtt) || 'Không rõ';

        if (data.timestamp) {
            tsEl.textContent = new Date(data.timestamp).toLocaleString('vi-VN');
        } else {
            tsEl.textContent = 'Không rõ';
        }

        lastCheckedEl.textContent = `Lần kiểm tra cuối: ${new Date().toLocaleString('vi-VN')}`;
        rawEl.textContent = text || '(phản hồi rỗng)';

        // Simple coloring based on status
        statusEl.style.color = ok && data.status === 'Healthy' ? '#4CAF50' : '#f44336';
        dbEl.style.color = data.db === 'Connected' ? '#4CAF50' : '#f44336';
        mqttEl.style.color = data.mqtt === 'Running' ? '#4CAF50' : '#f44336';
    } catch (err) {
        statusEl.textContent = 'Không thể kết nối';
        dbEl.textContent = 'Không rõ';
        mqttEl.textContent = 'Không rõ';
        tsEl.textContent = 'Không rõ';
        lastCheckedEl.textContent = `Lần kiểm tra cuối: ${new Date().toLocaleString('vi-VN')}`;
        rawEl.textContent = `Yêu cầu thất bại: ${err}`;

        statusEl.style.color = '#f44336';
        dbEl.style.color = '#f44336';
        mqttEl.style.color = '#f44336';
    }
}

function goBackToDashboard() {
    window.location.href = 'index.html';
}

