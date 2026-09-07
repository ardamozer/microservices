// ApiGateway Endpoint Base URL
const API_BASE_URL = 'http://localhost:5000/api';

// Global Data State
let state = {
    users: [],
    products: [],
    orders: [],
    notifications: []
};

// Initial Setup
document.addEventListener('DOMContentLoaded', () => {
    initDashboard();
    // Auto refresh health and notifications every 10 seconds
    setInterval(() => {
        checkServicesHealth();
        loadNotifications();
    }, 10000);
});

async function initDashboard() {
    await checkServicesHealth();
    await loadUsers();
    await loadProducts();
    await loadOrders();
    await loadNotifications();
}

/**
 * Toast Notification Helper
 */
function showToast(message, type = 'info') {
    const container = document.getElementById('toastContainer');
    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;

    let icon = 'fa-circle-info';
    if (type === 'success') icon = 'fa-circle-check';
    if (type === 'error') icon = 'fa-circle-exclamation';

    toast.innerHTML = `
        <i class="fa-solid ${icon}"></i>
        <span>${message}</span>
    `;

    container.appendChild(toast);

    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateX(100%)';
        setTimeout(() => toast.remove(), 300);
    }, 3500);
}

/**
 * Custom Fetch Handler with Rate Limit Header Parsing
 */
async function fetchApi(endpoint, options = {}) {
    try {
        const response = await fetch(`${API_BASE_URL}${endpoint}`, {
            headers: {
                'Content-Type': 'application/json',
                ...options.headers
            },
            ...options
        });

        // Update Rate Limit info if headers present
        const remaining = response.headers.get('X-RateLimit-Remaining');
        const limit = response.headers.get('X-RateLimit-Limit');
        if (remaining !== null) {
            document.getElementById('rateLimitRemaining').textContent = remaining;
        }
        if (limit !== null) {
            document.getElementById('rateLimitMax').textContent = limit;
        }

        const gatewayPill = document.getElementById('gatewayStatusPill');
        const gatewayText = document.getElementById('gatewayStatusText');
        gatewayPill.className = 'gateway-status-pill online';
        gatewayText.textContent = 'ApiGateway (5000)';

        if (!response.ok) {
            let errorMsg = `İstek başarısız oldu (${response.status})`;
            try {
                const errData = await response.json();
                if (errData.Message) errorMsg = errData.Message;
                else if (errData.Error) errorMsg = errData.Error;
                else if (errData.message) errorMsg = errData.message;
            } catch (e) { }
            throw new Error(errorMsg);
        }

        return await response.json();
    } catch (err) {
        if (err.message.includes('Failed to fetch') || err.message.includes('NetworkError')) {
            const gatewayPill = document.getElementById('gatewayStatusPill');
            const gatewayText = document.getElementById('gatewayStatusText');
            gatewayPill.className = 'gateway-status-pill offline';
            gatewayText.textContent = 'Gateway Bağlantı Yok';
        }
        throw err;
    }
}

/**
 * 1. Health Checks
 */
async function checkServicesHealth() {
    const services = [
        { id: 'user', path: '/users/health' },
        { id: 'product', path: '/products/health' },
        { id: 'order', path: '/orders/health' },
        { id: 'notification', path: '/notifications/health' }
    ];

    for (const service of services) {
        const badgeEl = document.getElementById(`health-${service.id}`);
        try {
            const data = await fetchApi(service.path);
            badgeEl.className = 'badge badge-success';
            badgeEl.textContent = 'Aktif';
        } catch (err) {
            badgeEl.className = 'badge badge-danger';
            badgeEl.textContent = 'Kapalı';
        }
    }
}

/**
 * 2. Load Users & Populate Select Dropdown
 */
async function loadUsers() {
    const usersListEl = document.getElementById('usersList');
    const userSelectEl = document.getElementById('orderUserSelect');

    try {
        const data = await fetchApi('/users');
        state.users = Array.isArray(data) ? data : [];
        document.getElementById('statUserCount').textContent = state.users.length;

        // Render Sidebar List
        if (state.users.length === 0) {
            usersListEl.innerHTML = '<li class="empty-feed">Kullanıcı bulunamadı.</li>';
        } else {
            usersListEl.innerHTML = state.users.map(u => `
                <li class="user-item">
                    <div class="user-info">
                        <span class="user-name">${escapeHtml(u.name || u.Name)}</span>
                        <span class="user-email">${escapeHtml(u.email || u.Email)}</span>
                    </div>
                    <span class="user-id-badge">ID: ${u.id || u.Id}</span>
                </li>
            `).join('');
        }

        // Render Select Options
        userSelectEl.innerHTML = '<option value="">-- Kullanıcı Seçin --</option>' +
            state.users.map(u => `<option value="${u.id || u.Id}">${escapeHtml(u.name || u.Name)} (ID: ${u.id || u.Id})</option>`).join('');

    } catch (err) {
        usersListEl.innerHTML = `<li class="loading-text" style="color: var(--accent-danger)">Hata: ${err.message}</li>`;
    }
}

/**
 * Create User
 */
async function handleCreateUser(e) {
    e.preventDefault();
    const name = document.getElementById('userName').value.trim();
    const email = document.getElementById('userEmail').value.trim();

    if (!name || !email) return;

    try {
        await fetchApi('/users', {
            method: 'POST',
            body: JSON.stringify({ name, email })
        });
        showToast('Kullanıcı başarıyla oluşturuldu!', 'success');
        document.getElementById('addUserForm').reset();
        await loadUsers();
    } catch (err) {
        showToast(`Kullanıcı eklenemedi: ${err.message}`, 'error');
    }
}

/**
 * 3. Load Products & Round-Robin Instance Badge
 */
async function loadProducts() {
    const productsGridEl = document.getElementById('productsGrid');
    const productSelectEl = document.getElementById('orderProductSelect');
    const instanceBadge = document.getElementById('productInstanceBadge');

    try {
        const responseData = await fetchApi('/products');
        let productsList = [];
        let instanceInfo = 'Round-Robin';

        if (responseData && responseData.data) {
            productsList = responseData.data;
            instanceInfo = responseData.instance || 'ProductService';
        } else if (Array.isArray(responseData)) {
            productsList = responseData;
        }

        state.products = productsList;
        document.getElementById('statProductCount').textContent = productsList.length;
        instanceBadge.textContent = `Yanıt: ${instanceInfo}`;

        if (productsList.length === 0) {
            productsGridEl.innerHTML = '<div class="empty-feed">Ürün kataloğu boş.</div>';
        } else {
            productsGridEl.innerHTML = productsList.map(p => `
                <div class="product-card">
                    <div class="product-card-top">
                        <span class="product-title">${escapeHtml(p.name || p.Name)}</span>
                        <span class="user-id-badge">ID: ${p.id || p.Id}</span>
                    </div>
                    <div class="product-price">${(p.price || p.Price).toLocaleString('tr-TR')} ₺</div>
                    <div class="product-card-bottom">
                        <span class="stock-info">Stok: <strong>${p.stock !== undefined ? p.stock : p.Stock}</strong> adet</span>
                        <span class="instance-tag">${instanceInfo}</span>
                    </div>
                </div>
            `).join('');
        }

        // Render Select Options
        productSelectEl.innerHTML = '<option value="">-- Ürün Seçin --</option>' +
            productsList.map(p => `<option value="${p.id || p.Id}">${escapeHtml(p.name || p.Name)} - ${p.price || p.Price} ₺ (Stok: ${p.stock !== undefined ? p.stock : p.Stock})</option>`).join('');

    } catch (err) {
        productsGridEl.innerHTML = `<div class="empty-feed" style="color: var(--accent-danger)">Ürünler yüklenemedi: ${err.message}</div>`;
    }
}

/**
 * Create Product
 */
async function handleCreateProduct(e) {
    e.preventDefault();
    const name = document.getElementById('productName').value.trim();
    const price = parseFloat(document.getElementById('productPrice').value);
    const stock = parseInt(document.getElementById('productStock').value);

    if (!name || isNaN(price) || isNaN(stock)) return;

    try {
        await fetchApi('/products', {
            method: 'POST',
            body: JSON.stringify({ name, price, stock })
        });
        showToast('Ürün kataloğa eklendi!', 'success');
        document.getElementById('addProductForm').reset();
        await loadProducts();
    } catch (err) {
        showToast(`Ürün eklenemedi: ${err.message}`, 'error');
    }
}

/**
 * 4. Create Order (Distributed Lock & Polly Resilience Test)
 */
async function handleCreateOrder(e) {
    e.preventDefault();
    const userId = parseInt(document.getElementById('orderUserSelect').value);
    const productId = parseInt(document.getElementById('orderProductSelect').value);
    const quantity = parseInt(document.getElementById('orderQuantity').value);
    const submitBtn = document.getElementById('submitOrderBtn');

    if (!userId || !productId || !quantity) {
        showToast('Lütfen kullanıcı, ürün ve adet alanlarını doldurun.', 'error');
        return;
    }

    submitBtn.disabled = true;
    submitBtn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Sipariş İşleniyor (Lock & PubSub)...';

    try {
        const response = await fetchApi('/orders', {
            method: 'POST',
            body: JSON.stringify({ userId, productId, quantity })
        });

        showToast(response.message || 'Sipariş oluşturuldu!', 'success');
        document.getElementById('createOrderForm').reset();
        
        // Refresh items
        await loadProducts();
        await loadOrders();
        await loadNotifications();
    } catch (err) {
        showToast(`Sipariş Hatası: ${err.message}`, 'error');
    } finally {
        submitBtn.disabled = false;
        submitBtn.innerHTML = '<i class="fa-solid fa-bolt"></i> Siparişi Tamamla (Publish Event)';
    }
}

/**
 * 5. Load Orders History
 */
async function loadOrders() {
    const tbody = document.getElementById('ordersTableBody');

    try {
        const responseData = await fetchApi('/orders');
        let ordersList = [];
        if (responseData && responseData.data) {
            ordersList = responseData.data;
        } else if (Array.isArray(responseData)) {
            ordersList = responseData;
        }

        state.orders = ordersList;
        document.getElementById('statOrderCount').textContent = ordersList.length;

        if (ordersList.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="text-center">Henüz sipariş kaydı yok.</td></tr>';
        } else {
            tbody.innerHTML = ordersList.map(o => {
                const dateStr = o.createdAt || o.CreatedAt ? new Date(o.createdAt || o.CreatedAt).toLocaleString('tr-TR') : 'Bugün';
                return `
                    <tr>
                        <td>#${o.id || o.Id}</td>
                        <td><span class="user-id-badge">Kullanıcı ${o.userId || o.UserId}</span></td>
                        <td><strong>${escapeHtml(o.productName || o.ProductName || 'Ürün')}</strong></td>
                        <td>${o.quantity || o.Quantity} Adet</td>
                        <td>${dateStr}</td>
                        <td><span class="badge badge-success">${o.status || o.Status || 'Tamamlandı'}</span></td>
                    </tr>
                `;
            }).join('');
        }
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="6" class="text-center" style="color: var(--accent-danger)">Siparişler yüklenemedi: ${err.message}</td></tr>`;
    }
}

/**
 * 6. Load Notifications (Redis Pub/Sub Event Stream)
 */
async function loadNotifications() {
    const feedEl = document.getElementById('notificationsFeed');

    try {
        const notifications = await fetchApi('/notifications');
        state.notifications = Array.isArray(notifications) ? notifications : [];
        document.getElementById('statNotificationCount').textContent = state.notifications.length;

        if (state.notifications.length === 0) {
            feedEl.innerHTML = '<div class="empty-feed">Henüz yayınlanmış event bildirimi yok.</div>';
        } else {
            // Sort newest first
            const sorted = [...state.notifications].reverse();
            feedEl.innerHTML = sorted.map(n => {
                const timeStr = n.timestamp || n.Timestamp ? new Date(n.timestamp || n.Timestamp).toLocaleTimeString('tr-TR') : 'Şimdi';
                return `
                    <div class="notification-item">
                        <div class="notification-icon"><i class="fa-solid fa-bell font-icon"></i></div>
                        <div class="notification-content">
                            <span class="notification-msg">${escapeHtml(n.message || n.Message)}</span>
                            <span class="notification-time"><i class="fa-regular fa-clock"></i> ${timeStr} - Redis Channel: order_created_channel</span>
                        </div>
                    </div>
                `;
            }).join('');
        }
    } catch (err) {
        // Notification service endpoint error optional fallbacks
    }
}

/**
 * Utility: HTML Escape
 */
function escapeHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}
