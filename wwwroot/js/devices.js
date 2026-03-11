// Device Management JavaScript
const API_BASE = window.API_BASE || (window.location.origin + '/api');
let devices = [];
let crops = [];
let editingDeviceId = null;

// Initialize
document.addEventListener('DOMContentLoaded', function() {
    checkAuthentication();
    loadCrops();
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

    const username = localStorage.getItem('username');
    const role = localStorage.getItem('role');
    const header = document.getElementById('devicesHeader');
    header.innerHTML = `
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

// Setup event listeners
function setupEventListeners() {
    document.getElementById('addDeviceForm').addEventListener('submit', createDevice);
    document.getElementById('editDeviceForm').addEventListener('submit', updateDevice);
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

        if (!response.ok) throw new Error('Failed to load crops');

        crops = await response.json();
        populateCropSelects();
    } catch (error) {
        console.error('Error loading crops:', error);
    }
}

// Populate crop dropdowns
function populateCropSelects() {
    const select1 = document.getElementById('cropSelect');
    const select2 = document.getElementById('editCropSelect');

    [select1, select2].forEach(select => {
        select.innerHTML = '<option value="">No Crop Assigned</option>';
        crops.forEach(crop => {
            const option = document.createElement('option');
            option.value = crop.id;
            option.textContent = crop.name;
            select.appendChild(option);
        });
    });
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

        if (!response.ok) throw new Error('Failed to load devices');

        devices = await response.json();
        displayDevices();
    } catch (error) {
        console.error('Error loading devices:', error);
        showError('Failed to load devices');
    }
}

// Display devices
function displayDevices() {
    const activeDevices = devices.filter(d => d.isActive);
    const inactiveDevices = devices.filter(d => !d.isActive);

    // Active devices
    const grid = document.getElementById('devicesGrid');
    if (activeDevices.length === 0) {
        grid.innerHTML = '<p class="no-devices">No active devices. Create one to get started!</p>';
    } else {
        grid.innerHTML = '';
        activeDevices.forEach(device => {
            grid.appendChild(createDeviceCard(device));
        });
    }

    // Inactive devices
    const inactiveGrid = document.getElementById('inactiveDevicesGrid');
    if (inactiveDevices.length === 0) {
        inactiveGrid.innerHTML = '<p class="no-devices">No inactive devices</p>';
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

    const lastSeen = device.lastSeen ? new Date(device.lastSeen).toLocaleString() : 'Never';
    const createdAt = device.createdAt ? new Date(device.createdAt).toLocaleDateString() : 'Unknown';
    const cropDisplay = device.cropName || 'Not assigned';
    const statusClass = device.isActive ? 'active' : 'inactive';

    card.innerHTML = `
        <div class="card-header">
            <h3>${device.name}</h3>
            <span class="status-badge ${statusClass}">${device.isActive ? '🟢 Active' : '🔴 Inactive'}</span>
        </div>
        <div class="card-details">
            <div class="detail-row">
                <span class="label">MAC Address:</span>
                <span class="value mac">${device.macAddress}</span>
            </div>
            <div class="detail-row">
                <span class="label">Crop:</span>
                <span class="value">${cropDisplay}</span>
            </div>
            <div class="detail-row">
                <span class="label">Status:</span>
                <span class="value">${device.status || 'Unknown'}</span>
            </div>
            <div class="detail-row">
                <span class="label">Created:</span>
                <span class="value">${createdAt}</span>
            </div>
            <div class="detail-row">
                <span class="label">Last Seen:</span>
                <span class="value">${lastSeen}</span>
            </div>
        </div>
        <div class="card-actions">
            <button class="btn-small edit" onclick="openEditModal(${device.id})">✏️ Edit</button>
            <button class="btn-small delete" onclick="deleteDevice(${device.id})">🗑️ Delete</button>
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
            parseInt(document.getElementById('cropSelect').value) : null
    };

    // Validate MAC address format
    if (!/^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$/.test(deviceData.macAddress)) {
        showError('Invalid MAC address format (use AA:BB:CC:DD:EE:FF)');
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
            throw new Error(error.detail || 'Failed to create device');
        }

        showSuccess('Device created successfully!');
        document.getElementById('addDeviceForm').reset();
        loadDevices();
    } catch (error) {
        console.error('Error creating device:', error);
        showError(error.message || 'Failed to create device');
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
    document.getElementById('editStatus').value = device.status || 'active';

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

        if (!response.ok) throw new Error('Failed to update device');

        showSuccess('Device updated successfully!');
        closeEditModal();
        loadDevices();
    } catch (error) {
        console.error('Error updating device:', error);
        showError('Failed to update device');
    }
}

// Delete device
async function deleteDevice(deviceId) {
    const device = devices.find(d => d.id === deviceId);
    if (!device) return;

    if (!confirm(`Delete device "${device.name}"? This cannot be undone.`)) {
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

        if (!response.ok) throw new Error('Failed to delete device');

        showSuccess('Device deleted successfully');
        loadDevices();
    } catch (error) {
        console.error('Error deleting device:', error);
        showError('Failed to delete device');
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
