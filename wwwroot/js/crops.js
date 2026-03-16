const API_BASE = window.API_BASE || (window.location.origin + '/api');

let crops = [];

document.addEventListener('DOMContentLoaded', () => {
    checkAuthentication();
    setupEventListeners();
    resetCropForm();
    loadCrops();
});

function checkAuthentication() {
    const token = localStorage.getItem('token');
    if (!token) {
        window.location.href = 'login.html';
        return;
    }

    const username = localStorage.getItem('username');
    const role = localStorage.getItem('role');
    const header = document.getElementById('cropsHeader');
    header.innerHTML = `
        <span>${username} <small>(${role})</small></span>
        <div>
            <button id="backDashboardBtn" class="btn-secondary" type="button">← Bảng điều khiển</button>
            <button id="goDevicesBtn" class="btn-secondary" type="button">🔧 Thiết bị</button>
            <button id="logoutBtn" class="btn-secondary" type="button">Đăng xuất</button>
        </div>
    `;

    document.getElementById('backDashboardBtn').addEventListener('click', () => window.location.href = 'index.html');
    document.getElementById('goDevicesBtn').addEventListener('click', () => window.location.href = 'devices.html');
    document.getElementById('logoutBtn').addEventListener('click', logout);

}

function setupEventListeners() {
    document.getElementById('cropForm').addEventListener('submit', saveCrop);
    document.getElementById('addStageBtn').addEventListener('click', () => addStageCard());
    document.getElementById('resetCropFormBtn').addEventListener('click', resetCropForm);
}

function getAuthHeaders() {
    const token = localStorage.getItem('token');
    return {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
    };
}

async function loadCrops() {
    try {
        const response = await fetch(`${API_BASE}/crop`, { headers: getAuthHeaders() });
        if (response.status === 401) {
            logout();
            return;
        }
        if (!response.ok) throw new Error('Không thể tải cây trồng');

        crops = await response.json();
        renderCrops();
    } catch (error) {
        console.error('Error loading crops:', error);
        showError('Không thể tải danh sách cây trồng');
    }
}

function renderCrops() {
    const grid = document.getElementById('cropsGrid');
    if (!crops.length) {
        grid.innerHTML = '<p class="no-devices">Chưa có cây trồng nào. Hãy tạo cây trồng đầu tiên với các giai đoạn chu kỳ.</p>';
        return;
    }

    grid.innerHTML = '';
    crops.forEach(crop => {
        const card = document.createElement('div');
        card.className = 'crop-card';
        card.innerHTML = `
            <h3>${crop.name}</h3>
            <div class="crop-meta">
                <div><strong>Tổng ngày dự kiến:</strong> ${crop.totalDaysEst ?? '-'} ngày</div>
                <div><strong>Số giai đoạn:</strong> ${crop.stageCount}</div>
                <div><strong>Mô tả:</strong> ${crop.description || 'Không có mô tả'}</div>
            </div>
            <div class="card-actions">
                <button class="btn-small edit" type="button" onclick="editCrop(${crop.id})">✏️ Sửa</button>
                <button class="btn-small delete" type="button" onclick="deleteCrop(${crop.id}, '${escapeJs(crop.name)}')">🗑️ Xóa</button>
            </div>
        `;
        grid.appendChild(card);
    });
}

async function editCrop(cropId) {
    try {
        const response = await fetch(`${API_BASE}/crop/${cropId}`, { headers: getAuthHeaders() });
        if (response.status === 401) {
            logout();
            return;
        }
        if (!response.ok) throw new Error('Không thể tải chi tiết cây trồng');

        const crop = await response.json();
        document.getElementById('cropId').value = crop.id;
        document.getElementById('cropName').value = crop.name || '';
        document.getElementById('cropDescription').value = crop.description || '';
        document.getElementById('cropTotalDays').value = crop.totalDaysEst || '';

        const stagesContainer = document.getElementById('stagesContainer');
        stagesContainer.innerHTML = '';
        (crop.stages || []).forEach(stage => addStageCard(stage));

        if (!crop.stages?.length) {
            addStageCard();
        }

        window.scrollTo({ top: 0, behavior: 'smooth' });
    } catch (error) {
        console.error('Error loading crop details:', error);
        showError('Không thể tải chi tiết cây trồng');
    }
}

function resetCropForm() {
    document.getElementById('cropForm').reset();
    document.getElementById('cropId').value = '';
    const stagesContainer = document.getElementById('stagesContainer');
    stagesContainer.innerHTML = '';
    addStageCard();
}

function addStageCard(stage = {}) {
    const stagesContainer = document.getElementById('stagesContainer');
    const stageIndex = stagesContainer.children.length + 1;
    const card = document.createElement('div');
    card.className = 'stage-card';
    card.innerHTML = `
        <div class="stage-card-header">
            <h4>Giai đoạn ${stageIndex}</h4>
            <button type="button" class="btn-small delete">Xóa giai đoạn</button>
        </div>
        <div class="stage-grid">
            <div class="form-group"><label>Tên giai đoạn</label><input type="text" data-field="stageName" value="${escapeHtml(stage.stageName || '')}" required></div>
            <div class="form-group"><label>Từ ngày</label><input type="number" data-field="dayStart" min="1" value="${stage.dayStart ?? ''}" required></div>
            <div class="form-group"><label>Đến ngày</label><input type="number" data-field="dayEnd" min="1" value="${stage.dayEnd ?? ''}" required></div>
            <div class="form-group"><label>pH thấp nhất</label><input type="number" step="0.1" data-field="phMin" value="${stage.phMin ?? ''}"></div>
            <div class="form-group"><label>pH cao nhất</label><input type="number" step="0.1" data-field="phMax" value="${stage.phMax ?? ''}"></div>
            <div class="form-group"><label>PPM thấp nhất</label><input type="number" data-field="ppmMin" value="${stage.ppmMin ?? ''}"></div>
            <div class="form-group"><label>PPM cao nhất</label><input type="number" data-field="ppmMax" value="${stage.ppmMax ?? ''}"></div>
            <div class="form-group"><label>Nhiệt độ nước thấp nhất</label><input type="number" data-field="waterTempMin" value="${stage.waterTempMin ?? ''}"></div>
            <div class="form-group"><label>Nhiệt độ nước cao nhất</label><input type="number" data-field="waterTempMax" value="${stage.waterTempMax ?? ''}"></div>
            <div class="form-group"><label>Độ ẩm thấp nhất</label><input type="number" data-field="humidityMin" value="${stage.humidityMin ?? ''}"></div>
            <div class="form-group"><label>Độ ẩm cao nhất</label><input type="number" data-field="humidityMax" value="${stage.humidityMax ?? ''}"></div>
            <div class="form-group"><label>Bơm bật (phút)</label><input type="number" data-field="pumpOnMinutes" min="1" value="${stage.pumpOnMinutes ?? ''}"></div>
            <div class="form-group"><label>Bơm tắt (phút)</label><input type="number" data-field="pumpOffMinutes" min="1" value="${stage.pumpOffMinutes ?? ''}"></div>
        </div>
    `;

    card.querySelector('.delete').addEventListener('click', () => {
        card.remove();
        renumberStageCards();
    });

    stagesContainer.appendChild(card);
}

function renumberStageCards() {
    document.querySelectorAll('.stage-card').forEach((card, index) => {
        const title = card.querySelector('h4');
        if (title) {
            title.textContent = `Giai đoạn ${index + 1}`;
        }
    });
}

function collectStages() {
    return Array.from(document.querySelectorAll('.stage-card')).map(card => {
        const read = field => card.querySelector(`[data-field="${field}"]`).value;
        const readNumber = field => {
            const value = read(field);
            return value === '' ? null : Number(value);
        };

        return {
            stageName: read('stageName').trim(),
            dayStart: readNumber('dayStart'),
            dayEnd: readNumber('dayEnd'),
            phMin: readNumber('phMin'),
            phMax: readNumber('phMax'),
            ppmMin: readNumber('ppmMin'),
            ppmMax: readNumber('ppmMax'),
            waterTempMin: readNumber('waterTempMin'),
            waterTempMax: readNumber('waterTempMax'),
            humidityMin: readNumber('humidityMin'),
            humidityMax: readNumber('humidityMax'),
            pumpOnMinutes: readNumber('pumpOnMinutes'),
            pumpOffMinutes: readNumber('pumpOffMinutes')
        };
    });
}

async function saveCrop(e) {
    e.preventDefault();

    const cropId = document.getElementById('cropId').value;
    const payload = {
        name: document.getElementById('cropName').value.trim(),
        description: document.getElementById('cropDescription').value.trim() || null,
        totalDaysEst: document.getElementById('cropTotalDays').value ? Number(document.getElementById('cropTotalDays').value) : null,
        stages: collectStages()
    };

    try {
        const response = await fetch(cropId ? `${API_BASE}/crop/${cropId}` : `${API_BASE}/crop`, {
            method: cropId ? 'PUT' : 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(payload)
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.detail || 'Không thể lưu cây trồng');
        }

        showSuccess(cropId ? 'Cập nhật cây trồng thành công' : 'Tạo cây trồng thành công');
        resetCropForm();
        await loadCrops();
    } catch (error) {
        console.error('Error saving crop:', error);
        showError(error.message || 'Không thể lưu cây trồng');
    }
}

async function deleteCrop(cropId, cropName) {
    if (!confirm(`Bạn có chắc chắn muốn xóa cây trồng "${cropName}"?`)) {
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/crop/${cropId}`, {
            method: 'DELETE',
            headers: getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.detail || 'Không thể xóa cây trồng');
        }

        showSuccess('Đã xóa cây trồng');
        await loadCrops();
    } catch (error) {
        console.error('Error deleting crop:', error);
        showError(error.message || 'Không thể xóa cây trồng');
    }
}

function logout() {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    localStorage.removeItem('role');
    localStorage.removeItem('userId');
    window.location.href = 'login.html';
}

function showSuccess(message) {
    notify(message, '#4CAF50', 3000);
}

function showError(message) {
    notify(message, '#f44336', 5000);
}

function notify(message, background, duration) {
    const notification = document.createElement('div');
    notification.style.cssText = `position: fixed; top: 20px; right: 20px; background: ${background}; color: white; padding: 1rem; border-radius: 4px; z-index: 1001; max-width: 420px;`;
    notification.textContent = message;
    document.body.appendChild(notification);
    setTimeout(() => document.body.removeChild(notification), duration);
}

function escapeHtml(value) {
    return value
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function escapeJs(value) {
    return String(value).replaceAll('\\', '\\\\').replaceAll("'", "\\'");
}