// Device Management JavaScript
const API_BASE = window.API_BASE || (window.location.origin + '/api');
let devices = [];
let crops = [];
let gardens = [];
let editingDeviceId = null;

function toVietnameseDeviceStatus(status) {
    const map = {
        Active: 'Hoạt động',
        Inactive: 'Ngừng hoạt động',
        Maintenance: 'Bảo trì'
    };
    return map[status] || status || 'Không rõ';
}

// Initialize
document.addEventListener('DOMContentLoaded', function() {
    checkAuthentication();
    loadCrops();
    loadGardens();
    loadDevices();
    loadPendingDevices();
    setupEventListeners();
});

// Check authentication
function checkAuthentication() {
    const token = Auth.getStoredToken();
    if (!token) {
        Auth.clearAuthStorage();
        window.location.href = 'login.html';
        return;
    }

    const username = localStorage.getItem('username');
    const role = localStorage.getItem('role');
    const header = document.getElementById('devicesHeader');
    header.innerHTML = `
        <span>${username} <small>(${role})</small></span>
        <button id="backBtn" class="btn-secondary" onclick="goBack()">← Về bảng điều khiển</button>
        <button id="cropsBtn" class="btn-secondary" onclick="goToCrops()">🌿 Cây trồng</button>
        <button id="logoutBtn" class="btn-secondary">Đăng xuất</button>
    `;
    document.getElementById('logoutBtn').addEventListener('click', logout);
}

// Get authorization headers
function getAuthHeaders() {
    return Auth.getAuthHeaders();
}

// Setup event listeners
function setupEventListeners() {
    document.getElementById('addDeviceForm').addEventListener('submit', createDevice);
    document.getElementById('editDeviceForm').addEventListener('submit', updateDevice);
    document.getElementById('claimDeviceForm').addEventListener('submit', claimDevice);
    document.getElementById('refreshPendingBtn').addEventListener('click', loadPendingDevices);
}

// Load crops
async function loadCrops() {
    try {
        const response = await fetch(`${API_BASE}/crop`, {
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể tải danh sách cây trồng');

        crops = await response.json();
        populateCropSelects();
    } catch (error) {
        console.error('Error loading crops:', error);
    }
}

async function loadGardens() {
    try {
        const response = await fetch(`${API_BASE}/garden`, {
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể tải danh sách khu vườn');

        gardens = await response.json();
        populateGardenSelects();
    } catch (error) {
        console.error('Error loading gardens:', error);
    }
}

// Populate crop dropdowns
function populateCropSelects() {
    const select1 = document.getElementById('cropSelect');
    const select2 = document.getElementById('editCropSelect');
    const select3 = document.getElementById('claimCropSelect');

    [select1, select2, select3].forEach(select => {
        select.innerHTML = '<option value="">Chưa gán cây trồng</option>';
        crops.forEach(crop => {
            const option = document.createElement('option');
            option.value = crop.id;
            option.textContent = crop.name;
            select.appendChild(option);
        });
    });
}

function populateGardenSelects() {
    const select1 = document.getElementById('gardenSelect');
    const select2 = document.getElementById('editGardenSelect');
    const select3 = document.getElementById('claimGardenSelect');

    [select1, select2, select3].forEach(select => {
        select.innerHTML = '<option value="">Chưa gán khu vườn</option>';
        gardens.forEach(garden => {
            const option = document.createElement('option');
            option.value = garden.id;
            option.textContent = garden.name;
            select.appendChild(option);
        });
    });
}

async function loadPendingDevices() {
    try {
        const response = await fetch(`${API_BASE}/device/pending`, {
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể tải thiết bị chờ nhận quyền');

        const pendingDevices = await response.json();
        renderPendingDevices(pendingDevices);
    } catch (error) {
        console.error('Error loading pending devices:', error);
        showError('Không thể tải thiết bị chờ nhận quyền');
    }
}

function renderPendingDevices(pendingDevices) {
    const pendingGrid = document.getElementById('pendingDevicesGrid');

    if (!pendingDevices || pendingDevices.length === 0) {
        pendingGrid.innerHTML = '<p class="no-devices">Không có thiết bị chờ nhận quyền</p>';
        return;
    }

    pendingGrid.innerHTML = '';
    pendingDevices.forEach(device => {
        const card = document.createElement('div');
        card.className = 'device-card';

        const lastSeen = device.lastSeen ? new Date(device.lastSeen).toLocaleString('vi-VN') : 'Chưa thấy';
        const provisionedAt = device.provisionedAt ? new Date(device.provisionedAt).toLocaleString('vi-VN') : 'Không rõ';

        card.innerHTML = `
            <div class="card-header">
                <h3>${device.name || 'Pending Device'}</h3>
                <span class="status-badge inactive">🟡 Chờ nhận quyền</span>
            </div>
            <div class="card-details">
                <div class="detail-row"><span class="label">MAC:</span><span class="value mac">${device.macAddress}</span></div>
                <div class="detail-row"><span class="label">Chip ID:</span><span class="value">${device.chipId || '-'}</span></div>
                <div class="detail-row"><span class="label">Firmware:</span><span class="value">${device.firmwareVersion || '-'}</span></div>
                <div class="detail-row"><span class="label">Provisioned:</span><span class="value">${provisionedAt}</span></div>
                <div class="detail-row"><span class="label">Lần cuối online:</span><span class="value">${lastSeen}</span></div>
            </div>
        `;

        pendingGrid.appendChild(card);
    });
}

async function claimDevice(e) {
    e.preventDefault();

    const payload = {
        claimCode: document.getElementById('claimCode').value.trim().toUpperCase(),
        name: document.getElementById('claimDeviceName').value.trim() || null,
        currentCropId: document.getElementById('claimCropSelect').value ? parseInt(document.getElementById('claimCropSelect').value) : null,
        gardenId: document.getElementById('claimGardenSelect').value ? parseInt(document.getElementById('claimGardenSelect').value) : null
    };

    if (!payload.claimCode) {
        showError('Vui lòng nhập mã nhận quyền');
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/device/claim`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(payload)
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.detail || 'Không thể nhận quyền thiết bị');
        }

        showSuccess('Nhận quyền thiết bị thành công');
        document.getElementById('claimDeviceForm').reset();
        await loadPendingDevices();
        await loadDevices();
    } catch (error) {
        console.error('Error claiming device:', error);
        showError(error.message || 'Không thể nhận quyền thiết bị');
    }
}

// Load devices
async function loadDevices() {
    try {
        const response = await fetch(`${API_BASE}/device`, {
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể tải danh sách thiết bị');

        devices = await response.json();
        displayDevices();
    } catch (error) {
        console.error('Error loading devices:', error);
        showError('Không thể tải danh sách thiết bị');
    }
}

// Display devices
function displayDevices() {
    const activeDevices = devices.filter(d => d.isActive);
    const inactiveDevices = devices.filter(d => !d.isActive);

    // Active devices
    const grid = document.getElementById('devicesGrid');
    if (activeDevices.length === 0) {
        grid.innerHTML = '<p class="no-devices">Không có thiết bị đang hoạt động. Hãy tạo mới thiết bị!</p>';
    } else {
        grid.innerHTML = '';
        activeDevices.forEach(device => {
            grid.appendChild(createDeviceCard(device));
        });
    }

    // Inactive devices
    const inactiveGrid = document.getElementById('inactiveDevicesGrid');
    if (inactiveDevices.length === 0) {
        inactiveGrid.innerHTML = '<p class="no-devices">Không có thiết bị ngừng hoạt động</p>';
    } else {
        inactiveGrid.innerHTML = '';
        inactiveDevices.forEach(device => {
            inactiveGrid.appendChild(createDeviceCard(device, true));
        });
    }
}

// Create device card
function createDeviceCard(device, isInactive = false) {
    const card = document.createElement('div');
    card.className = 'device-card';
    if (isInactive) card.classList.add('inactive');

    const lastSeen = device.lastSeen ? new Date(device.lastSeen).toLocaleString('vi-VN') : 'Chưa bao giờ';
    const createdAt = device.createdAt ? new Date(device.createdAt).toLocaleDateString('vi-VN') : 'Không rõ';
    const cropDisplay = device.cropName || 'Chưa gán';
    const gardenDisplay = device.gardenName || 'Chưa gán';
    const statusClass = device.isActive ? 'active' : 'inactive';

    card.innerHTML = `
        <div class="card-header">
            <h3>${device.name}</h3>
            <span class="status-badge ${statusClass}">${device.isActive ? '🟢 Hoạt động' : '🔴 Ngừng hoạt động'}</span>
        </div>
        <div class="card-details">
            <div class="detail-row">
                <span class="label">Địa chỉ MAC:</span>
                <span class="value mac">${device.macAddress}</span>
            </div>
            <div class="detail-row">
                <span class="label">Cây trồng:</span>
                <span class="value">${cropDisplay}</span>
            </div>
            <div class="detail-row">
                <span class="label">Khu vườn:</span>
                <span class="value">${gardenDisplay}</span>
            </div>
            <div class="detail-row">
                <span class="label">Trạng thái:</span>
                <span class="value">${toVietnameseDeviceStatus(device.status)}</span>
            </div>
            <div class="detail-row">
                <span class="label">Ngày tạo:</span>
                <span class="value">${createdAt}</span>
            </div>
            <div class="detail-row">
                <span class="label">Lần cuối online:</span>
                <span class="value">${lastSeen}</span>
            </div>
        </div>
        <div class="card-actions">
            <button class="btn-small edit" onclick="openEditModal(${device.id})">✏️ Sửa</button>
            <button class="btn-small delete" onclick="deleteDevice(${device.id})">🗑️ Xóa</button>
        </div>
    `;

    return card;
}

// Create device
async function createDevice(e) {
    e.preventDefault();

    const deviceData = {
        name: document.getElementById('deviceName').value,
        macAddress: document.getElementById('macAddress').value,
        currentCropId: document.getElementById('cropSelect').value ? 
            parseInt(document.getElementById('cropSelect').value) : null,
        gardenId: document.getElementById('gardenSelect').value ?
            parseInt(document.getElementById('gardenSelect').value) : null
    };

    // Validate MAC address format
    if (!/^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$/.test(deviceData.macAddress)) {
        showError('Định dạng MAC không hợp lệ (dùng AA:BB:CC:DD:EE:FF)');
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/device`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(deviceData)
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.detail || 'Không thể tạo thiết bị');
        }

        showSuccess('Tạo thiết bị thành công!');
        document.getElementById('addDeviceForm').reset();
        loadDevices();
    } catch (error) {
        console.error('Error creating device:', error);
        showError(error.message || 'Không thể tạo thiết bị');
    }
}

// Open edit modal
async function openEditModal(deviceId) {
    const device = devices.find(d => d.id === deviceId);
    if (!device) return;

    editingDeviceId = deviceId;
    document.getElementById('editDeviceId').value = deviceId;
    document.getElementById('editDeviceName').value = device.name;
    document.getElementById('editCropSelect').value = device.currentCropId || '';
    document.getElementById('editGardenSelect').value = device.gardenId || '';
    document.getElementById('editStatus').value = device.status || 'Active';

    document.getElementById('editModal').style.display = 'flex';
}

// Close edit modal
function closeEditModal() {
    document.getElementById('editModal').style.display = 'none';
    editingDeviceId = null;
}

// Update device
async function updateDevice(e) {
    e.preventDefault();

    const deviceId = parseInt(document.getElementById('editDeviceId').value);
    const updateData = {
        name: document.getElementById('editDeviceName').value,
        currentCropId: document.getElementById('editCropSelect').value ? 
            parseInt(document.getElementById('editCropSelect').value) : null,
        gardenId: document.getElementById('editGardenSelect').value ?
            parseInt(document.getElementById('editGardenSelect').value) : null,
        status: document.getElementById('editStatus').value
    };

    try {
        const response = await fetch(`${API_BASE}/device/${deviceId}`, {
            method: 'PUT',
            headers: getAuthHeaders(),
            body: JSON.stringify(updateData)
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể cập nhật thiết bị');

        showSuccess('Cập nhật thiết bị thành công!');
        closeEditModal();
        loadDevices();
    } catch (error) {
        console.error('Error updating device:', error);
        showError('Không thể cập nhật thiết bị');
    }
}

// Delete device
async function deleteDevice(deviceId) {
    const device = devices.find(d => d.id === deviceId);
    if (!device) return;

    if (!confirm(`Bạn có chắc chắn muốn xóa thiết bị "${device.name}"? Không thể hoàn tác.`)) {
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/device/${deviceId}`, {
            method: 'DELETE',
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) throw new Error('Không thể xóa thiết bị');

        showSuccess('Đã xóa thiết bị thành công');
        loadDevices();
    } catch (error) {
        console.error('Error deleting device:', error);
        showError('Không thể xóa thiết bị');
    }
}

// Logout
function logout() {
    Auth.clearAuthStorage();
    window.location.href = 'login.html';
}

// Go back to dashboard
function goBack() {
    window.location.href = 'index.html';
}

function goToCrops() {
    window.location.href = 'crops.html';
}

// Show success
function showSuccess(message) {
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
        max-width: 400px;
    `;
    notification.textContent = message;
    document.body.appendChild(notification);

    setTimeout(() => {
        document.body.removeChild(notification);
    }, 3000);
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
        max-width: 400px;
    `;
    notification.textContent = message;
    document.body.appendChild(notification);

    setTimeout(() => {
        document.body.removeChild(notification);
    }, 5000);
}

// Close modal when clicking outside
window.addEventListener('click', function(e) {
    const modal = document.getElementById('editModal');
    if (e.target === modal) {
        closeEditModal();
    }
});
