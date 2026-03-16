// Dashboard JavaScript
const API_BASE = window.API_BASE || (window.location.origin + '/api');

// Notification settings
let lastUnreadCount = 0;
let soundEnabled = localStorage.getItem('notificationSoundEnabled') !== 'false'; // Default to true

// DOM elements
const refreshBtn = document.getElementById('refreshBtn');
const lastUpdate = document.getElementById('lastUpdate');
const totalDevices = document.getElementById('totalDevices');
const activeDevices = document.getElementById('activeDevices');
const activeAlerts = document.getElementById('activeAlerts');
const devicesContainer = document.getElementById('devicesContainer');
const alertsContainer = document.getElementById('alertsContainer');
const deviceSelect = document.getElementById('deviceSelect');
const reasonInput = document.getElementById('reasonInput');
const deviceModal = document.getElementById('deviceModal');
const deviceModalTitle = document.getElementById('deviceModalTitle');
const deviceModalContent = document.getElementById('deviceModalContent');
const closeModal = document.querySelector('.close');
const gardenSelect = document.getElementById('gardenSelect');
const addGardenBtn = document.getElementById('addGardenBtn');
const gardenModal = document.getElementById('gardenModal');
const gardenModalClose = document.getElementById('gardenModalClose');
const gardenForm = document.getElementById('gardenForm');
const gardenNameInput = document.getElementById('gardenNameInput');
const gardenLocationInput = document.getElementById('gardenLocationInput');
const gardenDescriptionInput = document.getElementById('gardenDescriptionInput');
const avgPh = document.getElementById('avgPh');
const avgTds = document.getElementById('avgTds');
const avgTemp = document.getElementById('avgTemp');
const avgHumidity = document.getElementById('avgHumidity');
const avgLight = document.getElementById('avgLight');
const phStatus = document.getElementById('phStatus');
const tdsStatus = document.getElementById('tdsStatus');
const tempStatus = document.getElementById('tempStatus');
const humidityStatus = document.getElementById('humidityStatus');
const lightStatus = document.getElementById('lightStatus');

let profileModal = null;
const actuatorStates = {
    Light: false,
    Fan: false,
    Roof: false,
    FloatSwitch: false,
    Pump: false
};

let selectedGardenId = localStorage.getItem('selectedGardenId') || '';

// Initialize dashboard
document.addEventListener('DOMContentLoaded', function() {
    bindNavigationButtons();
    initManualActuatorControls();

    // Check authentication
    checkAuthentication();

    loadGardens();
    
    loadDashboardData();
    setInterval(loadDashboardData, 10000); // Refresh every 10 seconds
});

// Bind nav buttons defensively (works even if inline onclick handlers are blocked)
function bindNavigationButtons() {
    document.getElementById('chartsBtn')?.addEventListener('click', goToCharts);
    document.getElementById('automationBtn')?.addEventListener('click', goToAutomation);
    document.getElementById('devicesBtn')?.addEventListener('click', goToDevices);
    document.getElementById('healthBtn')?.addEventListener('click', goToHealth);
}

// Check if user is authenticated
function checkAuthentication() {
    const token = localStorage.getItem('token');
    if (!token) {
        window.location.href = 'login.html';
        return;
    }

    // Add user info to header
    const username = localStorage.getItem('username');
    const role = localStorage.getItem('role');
    
    // Create user info element
    const headerControls = document.querySelector('.header-controls');
    const userInfo = document.createElement('div');
    userInfo.className = 'user-info';
    userInfo.innerHTML = `
        <div class="notification-bell">
            <button id="notificationBell" class="bell-button">
                🔔
                <span id="notificationBadge" class="notification-badge" style="display:none;">0</span>
            </button>
            <button id="soundToggle" class="sound-toggle" title="Bật/Tắt âm thanh thông báo">
                ${soundEnabled ? '🔊' : '🔇'}
            </button>
            <div id="notificationDropdown" class="notification-dropdown" style="display:none;">
                <div class="notification-header">
                    <h3>Thông báo</h3>
                    <button id="clearNotificationsBtn" class="clear-btn">Xóa tất cả</button>
                </div>
                <div id="notificationsList" class="notifications-list">
                    <p class="no-notifications">Không có thông báo mới</p>
                </div>
            </div>
        </div>
        <span>${username} <small>(${role})</small></span>
        <button id="profileBtn" class="btn-secondary">👤 Tài khoản</button>
        <button id="logoutBtn" class="btn-secondary">Đăng xuất</button>
    `;
    headerControls.appendChild(userInfo);
    ensureProfileModal();
    
    // Load notifications
    loadNotifications();
    setInterval(loadNotifications, 10000); // Check every 10 seconds
    
    // Notification bell click handler
    document.getElementById('notificationBell').addEventListener('click', toggleNotificationDropdown);
    document.getElementById('clearNotificationsBtn').addEventListener('click', clearAllNotifications);
    document.getElementById('soundToggle').addEventListener('click', toggleNotificationSound);
    document.getElementById('profileBtn').addEventListener('click', openProfileModal);
    
    document.getElementById('logoutBtn').addEventListener('click', logout);
}

function ensureProfileModal() {
    if (document.getElementById('profileModal')) {
        profileModal = document.getElementById('profileModal');
        return;
    }

    profileModal = document.createElement('div');
    profileModal.id = 'profileModal';
    profileModal.className = 'modal';
    profileModal.innerHTML = `
        <div class="modal-content profile-modal-content">
            <span id="profileModalClose" class="close">&times;</span>
            <h2>Thông tin tài khoản</h2>
            <div id="profileContent">Đang tải thông tin tài khoản...</div>
        </div>
    `;

    document.body.appendChild(profileModal);
    document.getElementById('profileModalClose').addEventListener('click', closeProfileModal);
}

async function openProfileModal() {
    if (!profileModal) {
        ensureProfileModal();
    }

    profileModal.style.display = 'block';
    await loadCurrentUserProfile();
}

function closeProfileModal() {
    if (!profileModal) return;
    profileModal.style.display = 'none';
}

async function loadCurrentUserProfile() {
    const profileContent = document.getElementById('profileContent');
    profileContent.textContent = 'Đang tải thông tin tài khoản...';

    try {
        const response = await fetch(`${API_BASE}/authentication/me`, {
            method: 'GET',
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) {
            throw new Error('Không thể tải thông tin tài khoản');
        }

        const user = await response.json();
        const createdAt = user.createdAt ? new Date(user.createdAt).toLocaleString('vi-VN') : 'Không rõ';
        const lastLogin = user.lastLogin ? new Date(user.lastLogin).toLocaleString('vi-VN') : 'Chưa ghi nhận';

        profileContent.innerHTML = `
            <div class="profile-grid">
                <div class="profile-item"><span class="label">Tên đăng nhập</span><span class="value">${user.username || '-'}</span></div>
                <div class="profile-item"><span class="label">Email</span><span class="value">${user.email || '-'}</span></div>
                <div class="profile-item"><span class="label">Vai trò</span><span class="value">${user.role || '-'}</span></div>
                <div class="profile-item"><span class="label">Ngày tạo</span><span class="value">${createdAt}</span></div>
                <div class="profile-item"><span class="label">Đăng nhập gần nhất</span><span class="value">${lastLogin}</span></div>
            </div>
        `;
    } catch (error) {
        console.error('Error loading user profile:', error);
        profileContent.innerHTML = '<p>Không thể tải thông tin tài khoản.</p>';
    }
}

// Toggle notification dropdown
function toggleNotificationDropdown() {
    const dropdown = document.getElementById('notificationDropdown');
    dropdown.style.display = dropdown.style.display === 'none' ? 'block' : 'none';
}

// Load notifications
async function loadNotifications() {
    try {
        const response = await fetch(`${API_BASE}/notification/unread`, {
            headers: getAuthHeaders()
        });
        
        if (response.status === 401) {
            logout();
            return;
        }
        
        if (!response.ok) return;
        
        const data = await response.json();
        const badge = document.getElementById('notificationBadge');
        const notificationsList = document.getElementById('notificationsList');
        
        // Check if new notifications arrived
        if (data.unreadCount > lastUnreadCount && soundEnabled && data.unreadCount > 0) {
            playNotificationSound();
        }
        lastUnreadCount = data.unreadCount;
        
        if (data.unreadCount > 0) {
            badge.textContent = data.unreadCount;
            badge.style.display = 'inline-block';
            
            notificationsList.innerHTML = '';
            data.notifications.forEach(notification => {
                const notifElement = document.createElement('div');
                notifElement.className = `notification-item notification-${notification.type.toLowerCase()}`;
                notifElement.innerHTML = `
                    <div class="notification-content">
                        <h4>${notification.title}</h4>
                        <p>${notification.message}</p>
                        <small>${new Date(notification.createdAt).toLocaleString('vi-VN')}</small>
                    </div>
                    <button class="mark-read-btn" onclick="markNotificationAsRead(${notification.id})">✓</button>
                `;
                notificationsList.appendChild(notifElement);
            });
        } else {
            badge.style.display = 'none';
            notificationsList.innerHTML = '<p class="no-notifications">Không có thông báo mới</p>';
        }
    } catch (error) {
        console.error('Error loading notifications:', error);
    }
}

// Mark notification as read
async function markNotificationAsRead(notificationId) {
    try {
        await fetch(`${API_BASE}/notification/${notificationId}/read`, {
            method: 'POST',
            headers: getAuthHeaders()
        });
        loadNotifications();
    } catch (error) {
        console.error('Error marking notification as read:', error);
    }
}

// Clear all notifications
async function clearAllNotifications() {
    try {
        if (confirm('Bạn có muốn xóa tất cả thông báo không?')) {
            await fetch(`${API_BASE}/notification/clear`, {
                method: 'DELETE',
                headers: getAuthHeaders()
            });
            loadNotifications();
        }
    } catch (error) {
        console.error('Error clearing notifications:', error);
    }
}

// Toggle notification sound
function toggleNotificationSound() {
    soundEnabled = !soundEnabled;
    localStorage.setItem('notificationSoundEnabled', soundEnabled);
    const toggleBtn = document.getElementById('soundToggle');
    toggleBtn.textContent = soundEnabled ? '🔊' : '🔇';
    showSuccess(`Âm thanh thông báo đã ${soundEnabled ? 'bật' : 'tắt'}`);
}

// Play notification sound using Web Audio API
function playNotificationSound() {
    try {
        // Create audio context
        const audioContext = new (window.AudioContext || window.webkitAudioContext)();
        
        // Create oscillator and gain nodes for beep sound
        const oscillator = audioContext.createOscillator();
        const gainNode = audioContext.createGain();
        
        oscillator.connect(gainNode);
        gainNode.connect(audioContext.destination);
        
        // Set frequency and duration for pleasant notification beep
        oscillator.frequency.value = 800; // Hz
        oscillator.type = 'sine';
        
        // Volume envelope
        gainNode.gain.setValueAtTime(0.3, audioContext.currentTime);
        gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.3);
        
        // Play sound
        oscillator.start(audioContext.currentTime);
        oscillator.stop(audioContext.currentTime + 0.3);
    } catch (error) {
        console.log('Could not play notification sound:', error);
    }
}

// Logout function
function logout() {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    localStorage.removeItem('role');
    localStorage.removeItem('userId');
    window.location.href = 'login.html';
}

// Get authorization header with token
function getAuthHeaders() {
    const token = localStorage.getItem('token');
    return {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
    };
}

// Event listeners
refreshBtn.addEventListener('click', loadDashboardData);
closeModal.addEventListener('click', () => deviceModal.style.display = 'none');
gardenModalClose?.addEventListener('click', closeGardenModal);
addGardenBtn?.addEventListener('click', openGardenModal);
gardenForm?.addEventListener('submit', handleCreateGarden);
gardenSelect?.addEventListener('change', onGardenChanged);
window.addEventListener('click', (e) => {
    if (e.target === deviceModal) {
        deviceModal.style.display = 'none';
    }
    if (e.target === gardenModal) {
        closeGardenModal();
    }
    if (profileModal && e.target === profileModal) {
        closeProfileModal();
    }
});

function onGardenChanged() {
    selectedGardenId = gardenSelect.value;
    localStorage.setItem('selectedGardenId', selectedGardenId);
    loadDashboardData();
}

function openGardenModal() {
    gardenModal.style.display = 'block';
    gardenNameInput?.focus();
}

function closeGardenModal() {
    if (!gardenModal) return;
    gardenModal.style.display = 'none';
    gardenForm?.reset();
}

async function loadGardens() {
    try {
        const response = await fetch(`${API_BASE}/garden`, {
            method: 'GET',
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể tải danh sách khu vườn');

        const gardens = await response.json();
        updateGardenSelect(gardens);
    } catch (error) {
        console.error('Error loading gardens:', error);
        showError('Không thể tải danh sách khu vườn');
    }
}

function updateGardenSelect(gardens) {
    if (!gardenSelect) return;

    gardenSelect.innerHTML = '<option value="">Tất cả khu vườn</option>';

    gardens.forEach(garden => {
        const option = document.createElement('option');
        option.value = String(garden.id);
        option.textContent = `${garden.name} (${garden.deviceCount} thiết bị)`;
        gardenSelect.appendChild(option);
    });

    if (selectedGardenId && gardens.some(g => String(g.id) === selectedGardenId)) {
        gardenSelect.value = selectedGardenId;
    } else {
        selectedGardenId = '';
        localStorage.setItem('selectedGardenId', '');
    }
}

async function handleCreateGarden(e) {
    e.preventDefault();

    const name = gardenNameInput.value.trim();
    const location = gardenLocationInput.value.trim();
    const description = gardenDescriptionInput.value.trim();

    if (!name) {
        showError('Tên khu vườn không được để trống');
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/garden`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify({
                name,
                location: location || null,
                description: description || null
            })
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) {
            const errorData = await response.json().catch(() => ({}));
            throw new Error(errorData.detail || 'Không thể tạo khu vườn');
        }

        const created = await response.json();
        showSuccess('Tạo khu vườn thành công');
        closeGardenModal();
        await loadGardens();

        selectedGardenId = String(created.id);
        localStorage.setItem('selectedGardenId', selectedGardenId);
        if (gardenSelect) {
            gardenSelect.value = selectedGardenId;
        }

        loadDashboardData();
    } catch (error) {
        console.error('Error creating garden:', error);
        showError(error.message || 'Không thể tạo khu vườn');
    }
}

// Load dashboard data
async function loadDashboardData() {
    try {
        const query = selectedGardenId ? `?gardenId=${encodeURIComponent(selectedGardenId)}` : '';
        const response = await fetch(`${API_BASE}/dashboard/latest${query}`, {
            method: 'GET',
            headers: getAuthHeaders()
        });
        
        if (response.status === 401) {
            logout();
            return;
        }
        
        if (!response.ok) throw new Error('Không thể tải dữ liệu bảng điều khiển');

        const data = await response.json();
        updateDashboard(data);
        updateLastUpdate();
    } catch (error) {
        console.error('Error loading dashboard data:', error);
        showError('Không thể tải dữ liệu bảng điều khiển');
    }
}

// Update dashboard with data
function updateDashboard(data) {
    // Update overview stats
    totalDevices.textContent = data.totalDevices;
    activeDevices.textContent = data.activeDevices;
    activeAlerts.textContent = data.activeAlerts.length;

    // Update devices
    updateDevices(data.devices);

    // Update alerts
    updateAlerts(data.activeAlerts);

    // Update device select for manual control
    updateDeviceSelect(data.devices);

    // Update KPI cards
    updateKpiCards(data);
}

function updateKpiCards(data) {
    const sensors = data.devices
        .map(device => device.latestSensorData)
        .filter(sensor => sensor);

    const avg = (values) => {
        const valid = values.filter(v => Number.isFinite(v));
        if (valid.length === 0) return null;
        return valid.reduce((sum, value) => sum + value, 0) / valid.length;
    };

    const avgPhValue = avg(sensors.map(s => s.ph));
    const avgTdsValue = avg(sensors.map(s => s.tds));
    const avgTempValue = avg(sensors.map(s => s.waterTemperature));
    const avgHumidityValue = avg(sensors.map(s => s.airHumidity));
    const avgLightValue = avg(sensors.map(s => s.lightIntensity));

    avgTemp.textContent = avgTempValue !== null ? `${avgTempValue.toFixed(1)}°C` : '28°C';
    avgHumidity.textContent = avgHumidityValue !== null ? `${avgHumidityValue.toFixed(0)}%` : '70%';
    avgLight.textContent = avgLightValue !== null ? `${avgLightValue.toFixed(0)} Lux` : '12000 Lux';
    avgPh.textContent = avgPhValue !== null ? avgPhValue.toFixed(2) : '6.5';
    avgTds.textContent = avgTdsValue !== null ? `${avgTdsValue.toFixed(0)} ppm` : '850 ppm';

    tempStatus.textContent = getTempStatus(avgTempValue);
    humidityStatus.textContent = getHumidityStatus(avgHumidityValue);
    lightStatus.textContent = getLightStatus(avgLightValue);
    phStatus.textContent = getPhStatus(avgPhValue);
    tdsStatus.textContent = getTdsStatus(avgTdsValue);
}

function getPhStatus(value) {
    if (value === null) return 'Chưa có dữ liệu';
    if (value >= 5.5 && value <= 6.5) return 'Tối ưu cho đa số cây trồng';
    if (value < 5.5) return 'pH thấp, cần tăng pH';
    return 'pH cao, cần giảm pH';
}

function getTdsStatus(value) {
    if (value === null) return 'Chưa có dữ liệu';
    if (value >= 600 && value <= 1500) return 'Nồng độ dinh dưỡng ổn định';
    if (value < 600) return 'TDS thấp, cần bổ sung dinh dưỡng';
    return 'TDS cao, cần pha loãng dung dịch';
}

function getTempStatus(value) {
    if (value === null) return 'Chưa có dữ liệu';
    if (value >= 18 && value <= 26) return 'Nhiệt độ trong ngưỡng tốt';
    if (value < 18) return 'Nhiệt độ thấp, cân nhắc sưởi';
    return 'Nhiệt độ cao, cần làm mát';
}

function getHumidityStatus(value) {
    if (value === null) return 'Chưa có dữ liệu';
    if (value >= 55 && value <= 75) return 'Độ ẩm trong ngưỡng phù hợp';
    if (value < 55) return 'Độ ẩm thấp, cần tăng ẩm';
    return 'Độ ẩm cao, cần thông gió';
}

function getLightStatus(value) {
    if (value === null) return 'Chưa có dữ liệu';
    if (value >= 8000 && value <= 18000) return 'Trạng thái: Tốt';
    if (value < 8000) return 'Ánh sáng thấp, cần tăng đèn';
    return 'Ánh sáng cao, cần giảm cường độ';
}

// Update devices display
function updateDevices(devices) {
    devicesContainer.innerHTML = '';

    if (devices.length === 0) {
        devicesContainer.innerHTML = '<p>Chưa có thiết bị nào được đăng ký</p>';
        return;
    }

    devices.forEach(device => {
        const deviceCard = createDeviceCard(device);
        devicesContainer.appendChild(deviceCard);
    });
}

// Create device card
function createDeviceCard(device) {
    const card = document.createElement('div');
    card.className = 'device-card';

    const statusClass = device.isActive ? 'status-active' : 'status-inactive';
    const statusText = device.isActive ? 'Hoạt động' : 'Ngừng hoạt động';

    let sensorHtml = '<div class="sensor-grid">';
    if (device.latestSensorData) {
        const sensors = [
            { label: 'pH', value: device.latestSensorData.ph, unit: '' },
            { label: 'TDS', value: device.latestSensorData.tds, unit: ' ppm' },
            { label: 'Nhiệt độ', value: device.latestSensorData.waterTemperature, unit: '°C' },
            { label: 'Độ ẩm', value: device.latestSensorData.airHumidity, unit: '%' },
            { label: 'Ánh sáng', value: device.latestSensorData.lightIntensity, unit: ' lux' }
        ];

        sensors.forEach(sensor => {
            if (sensor.value !== null && sensor.value !== undefined) {
                sensorHtml += `
                    <div class="sensor-item">
                        <div class="sensor-label">${sensor.label}</div>
                        <div class="sensor-value">${sensor.value.toFixed(1)}${sensor.unit}</div>
                    </div>
                `;
            }
        });
    } else {
        sensorHtml += '<p>Không có dữ liệu cảm biến</p>';
    }
    sensorHtml += '</div>';

    const lastSeen = device.lastSeen ? new Date(device.lastSeen).toLocaleString('vi-VN') : 'Chưa bao giờ';

    card.innerHTML = `
        <div class="device-header">
            <div class="device-name">${device.name}</div>
            <div class="device-status ${statusClass}">${statusText}</div>
        </div>
        <div class="device-mac">MAC: ${device.macAddress}</div>
        <div class="device-crop">Khu vườn: ${device.gardenName || 'Chưa gán'}</div>
        <div class="device-crop">Cây trồng: ${device.cropName || 'Chưa gán'}</div>
        <div>Lần cuối online: ${lastSeen}</div>
        ${sensorHtml}
        <div class="device-actions">
            <button class="btn-secondary" onclick="showDeviceDetails(${device.id})">Chi tiết</button>
        </div>
    `;

    return card;
}

// Update alerts display
function updateAlerts(alerts) {
    alertsContainer.innerHTML = '';

    if (alerts.length === 0) {
        alertsContainer.innerHTML = '<p>Không có cảnh báo đang hoạt động</p>';
        return;
    }

    alerts.forEach(alert => {
        const alertItem = createAlertItem(alert);
        alertsContainer.appendChild(alertItem);
    });
}

// Create alert item
function createAlertItem(alert) {
    const item = document.createElement('div');
    item.className = 'alert-item';

    let alertClass = 'alert-info';
    if (alert.type === 1) alertClass = 'alert-warning'; // Warning
    if (alert.type === 2) alertClass = 'alert-error';   // Error

    item.classList.add(alertClass);

    const timestamp = new Date(alert.timestamp).toLocaleString('vi-VN');

    item.innerHTML = `
        <div class="alert-title">${alert.title}</div>
        <div class="alert-message">${alert.message || ''}</div>
        <div class="alert-timestamp">${timestamp}</div>
    `;

    return item;
}

// Update device select for manual control
function updateDeviceSelect(devices) {
    deviceSelect.innerHTML = '<option value="">Chọn thiết bị</option>';

    devices.forEach(device => {
        if (device.isActive) {
            const option = document.createElement('option');
            option.value = device.macAddress;
            option.textContent = `${device.name} (${device.macAddress})`;
            deviceSelect.appendChild(option);
        }
    });

    const hasActiveDevice = deviceSelect.options.length > 1;
    document.querySelectorAll('.actuator-toggle').forEach(toggle => {
        toggle.disabled = !hasActiveDevice;
    });
}

function initManualActuatorControls() {
    document.querySelectorAll('.actuator-toggle').forEach(toggle => {
        const actuator = toggle.dataset.actuator;
        if (!actuator) return;

        toggle.addEventListener('click', async () => {
            const nextAction = actuatorStates[actuator] ? 'OFF' : 'ON';
            await sendManualControlCommand(actuator, nextAction);
        });
    });
}

function applyActuatorState(actuator, isOn) {
    actuatorStates[actuator] = isOn;
    const card = document.querySelector(`.manual-actuator-card[data-actuator="${actuator}"]`);
    const status = document.getElementById(`status-${actuator}`);

    if (card) {
        card.classList.toggle('is-on', isOn);
    }
    if (status) {
        status.textContent = isOn ? 'Bật' : 'Tắt';
    }
}

async function sendManualControlCommand(actuator, action) {
    const selectedMacAddress = deviceSelect.value;
    if (!selectedMacAddress) {
        showError('Vui lòng chọn thiết bị trước khi điều khiển');
        return;
    }

    const reason = reasonInput?.value.trim() || null;
    const controlData = {
        macAddress: selectedMacAddress,
        actuatorType: actuator,
        action,
        controlType: 'Manual',
        reason
    };

    try {
        const response = await fetch(`${API_BASE}/actuator/control`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(controlData)
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) {
            throw new Error('Không thể gửi lệnh điều khiển');
        }

        applyActuatorState(actuator, action === 'ON');
        showSuccess(`${getActuatorDisplayName(actuator)}: ${action === 'ON' ? 'Bật' : 'Tắt'}`);
    } catch (error) {
        console.error('Error sending control command:', error);
        showError('Không thể gửi lệnh điều khiển');
    }
}

function getActuatorDisplayName(actuator) {
    const map = {
        Light: 'Đèn',
        Fan: 'Quạt',
        Roof: 'Mái che',
        FloatSwitch: 'Phao điện tử',
        Pump: 'Bơm nước'
    };
    return map[actuator] || actuator;
}

// Show device details
async function showDeviceDetails(deviceId) {
    try {
        // Get device history (last 24 hours)
        const response = await fetch(`${API_BASE}/dashboard/history/${deviceId}?hours=24`, {
            method: 'GET',
            headers: getAuthHeaders()
        });
        
        if (response.status === 401) {
            logout();
            return;
        }
        
        if (!response.ok) throw new Error('Không thể tải lịch sử thiết bị');

        const history = await response.json();

        // Create modal content
        let content = '<h3>Lịch sử cảm biến (24 giờ gần nhất)</h3>';

        if (history.length === 0) {
            content += '<p>Không có dữ liệu cảm biến</p>';
        } else {
            content += '<table style="width: 100%; border-collapse: collapse;">';
            content += '<thead><tr><th>Thời gian</th><th>pH</th><th>TDS</th><th>Nhiệt độ (°C)</th><th>Độ ẩm (%)</th></tr></thead>';
            content += '<tbody>';

            history.forEach(log => {
                const time = new Date(log.timestamp).toLocaleString('vi-VN');
                content += `<tr>
                    <td>${time}</td>
                    <td>${log.ph?.toFixed(2) || '-'}</td>
                    <td>${log.tds?.toFixed(0) || '-'}</td>
                    <td>${log.waterTemperature?.toFixed(1) || '-'}</td>
                    <td>${log.airHumidity?.toFixed(1) || '-'}</td>
                </tr>`;
            });

            content += '</tbody></table>';
        }

        deviceModalContent.innerHTML = content;
        deviceModalTitle.textContent = `Chi tiết thiết bị - ID: ${deviceId}`;
        deviceModal.style.display = 'block';
    } catch (error) {
        console.error('Error loading device details:', error);
        showError('Không thể tải chi tiết thiết bị');
    }
}

// Utility functions
function updateLastUpdate() {
    lastUpdate.textContent = `Cập nhật lần cuối: ${new Date().toLocaleString('vi-VN')}`;
}

function showSuccess(message) {
    // Simple success notification
    const notification = document.createElement('div');
    notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        background: #4CAF50;
        color: white;
        padding: 1rem;
        border-radius: 4px;
        z-index: 1001;
    `;
    notification.textContent = message;
    document.body.appendChild(notification);

    setTimeout(() => {
        document.body.removeChild(notification);
    }, 3000);
}

function showError(message) {
    // Simple error notification
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

// Navigate to charts page
function goToCharts() {
    window.location.href = 'charts.html';
}

// Navigate to automation page
function goToAutomation() {
    window.location.href = 'automation.html';
}

// Navigate to device management page
function goToDevices() {
    window.location.href = 'devices.html';
}

// Navigate to health page
function goToHealth() {
    window.location.href = 'health.html';
}
