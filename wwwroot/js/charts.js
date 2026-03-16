// Charts JavaScript
const API_BASE = window.API_BASE || (window.location.origin + '/api');

// Chart instances
let charts = {
    ph: null,
    tds: null,
    temp: null,
    humidity: null,
    allSensors: null
};

// Initialize
document.addEventListener('DOMContentLoaded', function() {
    checkAuthentication();
    loadDevices();
    setupEventListeners();
});

// Check authentication
function checkAuthentication() {
    const token = localStorage.getItem('token');
    if (!token) {
        window.location.href = 'login.html';
        return;
    }

    // Add user info to header
    const username = localStorage.getItem('username');
    const role = localStorage.getItem('role');
    const chartsHeader = document.getElementById('chartsHeader');
    chartsHeader.innerHTML = `
        <span>${username} <small>(${role})</small></span>
        <button id="backBtn" class="btn-secondary" onclick="goBack()">← Về bảng điều khiển</button>
        <button id="logoutBtn" class="btn-secondary">Đăng xuất</button>
    `;
    document.getElementById('logoutBtn').addEventListener('click', logout);
}

// Get authorization headers
function getAuthHeaders() {
    const token = localStorage.getItem('token');
    return {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
    };
}

// Load devices
async function loadDevices() {
    try {
        const response = await fetch(`${API_BASE}/dashboard/latest`, {
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể tải danh sách thiết bị');

        const data = await response.json();
        const deviceSelect = document.getElementById('deviceSelect');
        deviceSelect.innerHTML = '';

        if (!data.devices || data.devices.length === 0) {
            deviceSelect.innerHTML = '<option value="">Chưa có thiết bị</option>';
            clearChartsAndStats();
            document.getElementById('alertsList').innerHTML = '<p>Chưa có thiết bị để hiển thị cảnh báo.</p>';
            return;
        }

        data.devices.forEach(device => {
            const option = document.createElement('option');
            option.value = device.id;
            option.textContent = `${device.name} (${device.macAddress})`;
            deviceSelect.appendChild(option);
        });

        if (data.devices.length > 0) {
            deviceSelect.value = data.devices[0].id;
            loadCharts();
        }
    } catch (error) {
        console.error('Error loading devices:', error);
        showError('Không thể tải danh sách thiết bị');
    }
}

// Setup event listeners
function setupEventListeners() {
    document.getElementById('deviceSelect').addEventListener('change', loadCharts);
    document.getElementById('timeRange').addEventListener('change', loadCharts);
    document.getElementById('refreshChartsBtn').addEventListener('click', loadCharts);
}

// Load chart data
async function loadCharts() {
    const deviceId = document.getElementById('deviceSelect').value;
    const hours = document.getElementById('timeRange').value;

    if (!deviceId) {
        showError('Vui lòng chọn thiết bị');
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/dashboard/history/${deviceId}?hours=${hours}`, {
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể tải dữ liệu biểu đồ');

        const data = await response.json();
        renderCharts(data, hours);
        loadAlerts(deviceId);
    } catch (error) {
        console.error('Error loading chart data:', error);
        showError('Không thể tải dữ liệu biểu đồ');
    }
}

// Render charts
function renderCharts(data, hours) {
    if (!data || data.length === 0) {
        clearChartsAndStats();
        showError('Không có dữ liệu trong khoảng thời gian đã chọn');
        return;
    }

    // Prepare data
    const labels = data.map(d => new Date(d.timestamp).toLocaleString('vi-VN', {
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
    }));

    const phData = data.map(d => d.ph);
    const tdsData = data.map(d => d.tds);
    const tempData = data.map(d => d.waterTemperature);
    const humidityData = data.map(d => d.airHumidity);

    // Chart config
    const chartConfig = {
        responsive: true,
        maintainAspectRatio: true,
        plugins: {
            legend: {
                display: true,
                position: 'top'
            }
        },
        scales: {
            y: {
                beginAtZero: false
            }
        }
    };

    // pH Chart
    renderChart('ph', 'Mức pH', labels, [{
        label: 'pH',
        data: phData,
        borderColor: '#16a34a',
        backgroundColor: 'rgba(22, 163, 74, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // TDS Chart
    renderChart('tds', 'Mức TDS', labels, [{
        label: 'TDS (ppm)',
        data: tdsData,
        borderColor: '#22c55e',
        backgroundColor: 'rgba(34, 197, 94, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // Temperature Chart
    renderChart('temp', 'Nhiệt độ nước', labels, [{
        label: 'Nhiệt độ (°C)',
        data: tempData,
        borderColor: '#65a30d',
        backgroundColor: 'rgba(101, 163, 13, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // Humidity Chart
    renderChart('humidity', 'Độ ẩm không khí', labels, [{
        label: 'Độ ẩm (%)',
        data: humidityData,
        borderColor: '#15803d',
        backgroundColor: 'rgba(21, 128, 61, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // All Sensors Combined
    renderChart('allSensors', 'Tất cả thông số cảm biến (chuẩn hóa)', labels, [
        {
            label: 'pH',
            data: phData,
            borderColor: '#16a34a',
            borderWidth: 2,
            tension: 0.4,
            yAxisID: 'y'
        },
        {
            label: 'TDS',
            data: tdsData.map(v => v / 100), // Normalize to ~6-10 scale
            borderColor: '#22c55e',
            borderWidth: 2,
            tension: 0.4,
            yAxisID: 'y1'
        },
        {
            label: 'Nhiệt độ (°C)',
            data: tempData,
            borderColor: '#65a30d',
            borderWidth: 2,
            tension: 0.4,
            yAxisID: 'y2'
        },
        {
            label: 'Độ ẩm (%)',
            data: humidityData,
            borderColor: '#15803d',
            borderWidth: 2,
            tension: 0.4,
            yAxisID: 'y3'
        }
    ], {
        responsive: true,
        maintainAspectRatio: true,
        plugins: {
            legend: {
                display: true,
                position: 'top'
            }
        },
        scales: {
            y: {
                type: 'linear',
                position: 'left',
                title: { display: true, text: 'pH' }
            },
            y1: {
                type: 'linear',
                position: 'left',
                title: { display: true, text: 'TDS (ppm/100)' }
            },
            y2: {
                type: 'linear',
                position: 'right',
                title: { display: true, text: 'Nhiệt độ (°C)' }
            },
            y3: {
                type: 'linear',
                position: 'right',
                title: { display: true, text: 'Độ ẩm (%)' }
            }
        }
    });

    // Calculate and display stats
    displayStats('ph', phData);
    displayStats('tds', tdsData);
    displayStats('temp', tempData);
    displayStats('humidity', humidityData);
}

function clearChartsAndStats() {
    Object.keys(charts).forEach(key => {
        if (charts[key]) {
            charts[key].destroy();
            charts[key] = null;
        }
    });

    ['phStats', 'tdsStats', 'tempStats', 'humidityStats'].forEach(id => {
        const el = document.getElementById(id);
        if (el) {
            el.innerHTML = '<div class="stat-item"><span>Chưa có dữ liệu</span></div>';
        }
    });
}

// Render individual chart
function renderChart(chartId, chartLabel, labels, datasets, config) {
    const ctx = document.getElementById(chartId + 'Chart').getContext('2d');
    
    // Destroy existing chart if it exists
    if (charts[chartId]) {
        charts[chartId].destroy();
    }

    charts[chartId] = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: datasets
        },
        options: config
    });
}

// Display statistics
function displayStats(metric, data) {
    if (!data || data.length === 0) return;

    const validData = data.filter(v => v !== null && v !== undefined);
    if (validData.length === 0) return;

    const min = Math.min(...validData);
    const max = Math.max(...validData);
    const avg = (validData.reduce((a, b) => a + b, 0) / validData.length).toFixed(2);

    const statsEl = document.getElementById(metric + 'Stats');
    statsEl.innerHTML = `
        <div class="stat-item">
            <span>Thấp nhất: <strong>${min.toFixed(2)}</strong></span>
            <span>Trung bình: <strong>${avg}</strong></span>
            <span>Cao nhất: <strong>${max.toFixed(2)}</strong></span>
        </div>
    `;
}

// Load recent alerts
async function loadAlerts(deviceId) {
    try {
        const response = await fetch(`${API_BASE}/dashboard/latest`, {
            headers: getAuthHeaders()
        });

        if (!response.ok) throw new Error('Không thể tải cảnh báo');

        const data = await response.json();
        const alertsList = document.getElementById('alertsList');
        
        if (data.activeAlerts.length === 0) {
            alertsList.innerHTML = '<p class="no-alerts">Không có cảnh báo đang hoạt động 🎉</p>';
            return;
        }

        alertsList.innerHTML = '';
        data.activeAlerts.slice(0, 10).forEach(alert => {
            const alertItem = document.createElement('div');
            alertItem.className = 'alert-item';
            const timestamp = new Date(alert.timestamp).toLocaleString('vi-VN');
            let className = 'alert-info';
            if (alert.type === 1) className = 'alert-warning';
            if (alert.type === 2) className = 'alert-error';

            alertItem.classList.add(className);
            alertItem.innerHTML = `
                <div class="alert-time">${timestamp}</div>
                <div class="alert-title">${alert.title}</div>
                <div class="alert-message">${alert.message || ''}</div>
            `;
            alertsList.appendChild(alertItem);
        });
    } catch (error) {
        console.error('Error loading alerts:', error);
    }
}

// Logout
function logout() {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    localStorage.removeItem('role');
    localStorage.removeItem('userId');
    window.location.href = 'login.html';
}

// Go back to dashboard
function goBack() {
    window.location.href = 'index.html';
}

// Show error
function showError(message) {
    const notification = document.createElement('div');
    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        background: #f44336;
        color: white;
        padding: 1rem;
        border-radius: 4px;
        z-index: 1001;
    `;
    notification.textContent = message;
    document.body.appendChild(notification);

    setTimeout(() => {
        document.body.removeChild(notification);
    }, 5000);
}
