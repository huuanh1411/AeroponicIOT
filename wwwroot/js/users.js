const API_BASE = window.API_BASE || (window.location.origin + '/api');

let users = [];
const editModal = document.getElementById('editUserModal');
const currentUserId = () => Number.parseInt(localStorage.getItem('userId') || '', 10);

document.addEventListener('DOMContentLoaded', () => {
    if (!checkAuthentication()) {
        return;
    }

    document.getElementById('refreshUsersBtn').addEventListener('click', loadUsers);
    document.getElementById('editUserForm').addEventListener('submit', saveUser);
    document.getElementById('editUserModalClose').addEventListener('click', closeEditModal);
    document.getElementById('cancelEditUserBtn').addEventListener('click', closeEditModal);
    window.addEventListener('click', (e) => {
        if (e.target === editModal) {
            closeEditModal();
        }
    });

    loadUsers();
});

function checkAuthentication() {
    const token = Auth.getStoredToken();
    if (!token) {
        Auth.clearAuthStorage();
        window.location.href = 'login.html';
        return false;
    }

    const role = localStorage.getItem('role');
    if (role !== 'Administrator') {
        window.location.href = 'index.html';
        return false;
    }

    const username = localStorage.getItem('username');
    const header = document.getElementById('usersHeader');
    header.innerHTML = `
        <span>👥 Quản lý người dùng — ${escapeHtml(username)} <small>(${escapeHtml(role)})</small></span>
        <div>
            <button type="button" class="btn-secondary" onclick="goToDashboard()">← Bảng điều khiển</button>
            <button type="button" id="logoutBtn" class="btn-secondary">Đăng xuất</button>
        </div>
    `;
    document.getElementById('logoutBtn').addEventListener('click', logout);
    return true;
}

async function loadUsers() {
    const tbody = document.getElementById('usersTableBody');
    tbody.innerHTML = '<tr><td colspan="6" class="loading-cell">Đang tải...</td></tr>';

    try {
        const response = await fetch(`${API_BASE}/users`, {
            headers: Auth.getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (response.status === 403) {
            window.location.href = 'index.html';
            return;
        }

        if (!response.ok) {
            throw new Error(parseErrorMessage(await readJsonResponse(response)));
        }

        users = Auth.unwrapApiData(await response.json()) || [];
        if (!Array.isArray(users)) {
            users = [];
        }

        renderUsers();
    } catch (error) {
        console.error('Error loading users:', error);
        tbody.innerHTML = '<tr><td colspan="6" class="loading-cell">Không thể tải danh sách người dùng</td></tr>';
        showMessage(error.message || 'Không thể tải danh sách người dùng', 'error');
    }
}

function renderUsers() {
    const tbody = document.getElementById('usersTableBody');

    if (users.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" class="loading-cell">Chưa có người dùng</td></tr>';
        return;
    }

    const selfId = currentUserId();

    tbody.innerHTML = users.map(user => {
        const isSelf = user.id === selfId;
        const roleClass = user.role === 'Administrator' ? 'admin' : 'farmer';
        const roleLabel = user.role === 'Administrator' ? 'Quản trị viên' : 'Nông dân';
        const lastLogin = user.lastLogin
            ? new Date(user.lastLogin).toLocaleString('vi-VN')
            : 'Chưa có';

        return `
            <tr>
                <td>${escapeHtml(user.username || '')}</td>
                <td>${escapeHtml(user.email || '—')}</td>
                <td><span class="role-badge ${roleClass}">${roleLabel}</span></td>
                <td>${user.deviceCount ?? 0}</td>
                <td>${lastLogin}</td>
                <td>
                    <div class="row-actions">
                        <button type="button" class="btn-secondary" onclick="openEditModal(${user.id})">Sửa</button>
                        <button type="button" class="btn-danger" onclick="deleteUser(${user.id})" ${isSelf ? 'disabled title="Không thể xóa tài khoản của chính bạn"' : ''}>Xóa</button>
                    </div>
                </td>
            </tr>
        `;
    }).join('');
}

function openEditModal(userId) {
    const user = users.find(u => u.id === userId);
    if (!user) {
        return;
    }

    document.getElementById('editUserId').value = String(user.id);
    document.getElementById('editUsername').textContent = user.username || '';
    document.getElementById('editUserEmail').value = user.email || '';
    document.getElementById('editUserRole').value = user.role === 'Administrator' ? 'Administrator' : 'Farmer';
    editModal.style.display = 'block';
}

function closeEditModal() {
    editModal.style.display = 'none';
    document.getElementById('editUserForm').reset();
}

async function saveUser(event) {
    event.preventDefault();

    const id = Number.parseInt(document.getElementById('editUserId').value, 10);
    const email = document.getElementById('editUserEmail').value.trim();
    const role = document.getElementById('editUserRole').value;

    try {
        const response = await fetch(`${API_BASE}/users/${id}`, {
            method: 'PUT',
            headers: Auth.getAuthHeaders(),
            body: JSON.stringify({ email, role })
        });

        if (response.status === 401) {
            logout();
            return;
        }

        const data = await readJsonResponse(response);
        if (!response.ok) {
            throw new Error(parseErrorMessage(data));
        }

        closeEditModal();
        showMessage('Đã cập nhật người dùng. Người dùng cần đăng nhập lại để áp dụng vai trò mới.', 'success');
        await loadUsers();
    } catch (error) {
        console.error('Error saving user:', error);
        showMessage(error.message || 'Không thể cập nhật người dùng', 'error');
    }
}

async function deleteUser(userId) {
    const user = users.find(u => u.id === userId);
    if (!user) {
        return;
    }

    if (userId === currentUserId()) {
        showMessage('Không thể xóa tài khoản của chính bạn', 'error');
        return;
    }

    if (!confirm(`Xóa người dùng "${user.username}"? Thiết bị của họ sẽ không còn được gán chủ sở hữu.`)) {
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/users/${userId}`, {
            method: 'DELETE',
            headers: Auth.getAuthHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        const data = await readJsonResponse(response);
        if (!response.ok) {
            throw new Error(parseErrorMessage(data));
        }

        showMessage('Đã xóa người dùng', 'success');
        await loadUsers();
    } catch (error) {
        console.error('Error deleting user:', error);
        showMessage(error.message || 'Không thể xóa người dùng', 'error');
    }
}

async function readJsonResponse(response) {
    const text = await response.text();
    if (!text) {
        return null;
    }
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

function parseErrorMessage(data) {
    if (!data) {
        return 'Yêu cầu thất bại';
    }
    if (data.detail) {
        return data.detail;
    }
    if (data.message) {
        return data.message;
    }
    if (data.title) {
        return data.title;
    }
    return 'Yêu cầu thất bại';
}

function showMessage(text, type) {
    const box = document.getElementById('usersMessage');
    box.textContent = text;
    box.className = `message-box ${type}`;
    box.style.display = 'block';
}

function logout() {
    Auth.clearAuthStorage();
    window.location.href = 'login.html';
}

function goToDashboard() {
    window.location.href = 'index.html';
}

function escapeHtml(value) {
    return String(value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}
