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
        <button id="backBtn" class="btn-secondary" onclick="goBack()">← Back to Dashboard</button>
        <button id="logoutBtn" class="btn-secondary">Logout</button>
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

        if (!response.ok) throw new Error('Failed to load devices');

        const data = await response.json();
        const deviceSelect = document.getElementById('deviceSelect');
        deviceSelect.innerHTML = '';

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
        showError('Failed to load devices');
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
        showError('Please select a device');
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

        if (!response.ok) throw new Error('Failed to load chart data');

        const data = await response.json();
        renderCharts(data, hours);
        loadAlerts(deviceId);
    } catch (error) {
        console.error('Error loading chart data:', error);
        showError('Failed to load chart data');
    }
}

// Render charts
function renderCharts(data, hours) {
    if (!data || data.length === 0) {
        showError('No data available for selected time period');
        return;
    }

    // Prepare data
    const labels = data.map(d => new Date(d.timestamp).toLocaleString('en-US', {
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
    renderChart('ph', 'pH Level', labels, [{
        label: 'pH',
        data: phData,
        borderColor: '#667eea',
        backgroundColor: 'rgba(102, 126, 234, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // TDS Chart
    renderChart('tds', 'TDS Level', labels, [{
        label: 'TDS (ppm)',
        data: tdsData,
        borderColor: '#764ba2',
        backgroundColor: 'rgba(118, 75, 162, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // Temperature Chart
    renderChart('temp', 'Water Temperature', labels, [{
        label: 'Temperature (°C)',
        data: tempData,
        borderColor: '#f093fb',
        backgroundColor: 'rgba(240, 147, 251, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // Humidity Chart
    renderChart('humidity', 'Air Humidity', labels, [{
        label: 'Humidity (%)',
        data: humidityData,
        borderColor: '#4facfe',
        backgroundColor: 'rgba(79, 172, 254, 0.1)',
        borderWidth: 2,
        tension: 0.4,
        fill: true
    }], chartConfig);

    // All Sensors Combined
    renderChart('allSensors', 'All Sensor Parameters (Normalized)', labels, [
        {
            label: 'pH',
            data: phData,
            borderColor: '#667eea',
            borderWidth: 2,
            tension: 0.4,
            yAxisID: 'y'
        },
        {
            label: 'TDS',
            data: tdsData.map(v => v / 100), // Normalize to ~6-10 scale
            borderColor: '#764ba2',
            borderWidth: 2,
            tension: 0.4,
            yAxisID: 'y1'
        },
        {
            label: 'Temp (°C)',
            data: tempData,
            borderColor: '#f093fb',
            borderWidth: 2,
            tension: 0.4,
            yAxisID: 'y2'
        },
        {
            label: 'Humidity (%)',
            data: humidityData,
            borderColor: '#4facfe',
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
                title: { display: true, text: 'Temperature (°C)' }
            },
            y3: {
                type: 'linear',
                position: 'right',
                title: { display: true, text: 'Humidity (%)' }
            }
        }
    });

    // Calculate and display stats
    displayStats('ph', phData);
    displayStats('tds', tdsData);
    displayStats('temp', tempData);
    displayStats('humidity', humidityData);
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
            <span>Min: <strong>${min.toFixed(2)}</strong></span>
            <span>Avg: <strong>${avg}</strong></span>
            <span>Max: <strong>${max.toFixed(2)}</strong></span>
        </div>
    `;
}

// Load recent alerts
async function loadAlerts(deviceId) {
    try {
        const response = await fetch(`${API_BASE}/dashboard/latest`, {
            headers: getAuthHeaders()
        });

        if (!response.ok) throw new Error('Failed to load alerts');

        const data = await response.json();
        const alertsList = document.getElementById('alertsList');
        
        if (data.activeAlerts.length === 0) {
            alertsList.innerHTML = '<p class="no-alerts">No active alerts 🎉</p>';
            return;
        }

        alertsList.innerHTML = '';
        data.activeAlerts.slice(0, 10).forEach(alert => {
            const alertItem = document.createElement('div');
            alertItem.className = 'alert-item';
            const timestamp = new Date(alert.timestamp).toLocaleString();
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
