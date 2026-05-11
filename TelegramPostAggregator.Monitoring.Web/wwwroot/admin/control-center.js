(function () {
  const state = {
    session: null,
    users: [],
    selectedUserId: null,
    selectedDetail: null,
    showCreateForm: false,
    billingSettings: null
  };

  const els = {
    errorBanner: document.getElementById('errorBanner'),
    usersList: document.getElementById('adminUsersList'),
    panel: document.getElementById('controlCenterPanel'),
    newUserButton: document.getElementById('newUserButton'),
    billingPanel: document.getElementById('billingPanel')
  };

  function escapeHtml(value) {
    return String(value ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }

  function setError(message) {
    if (!message) {
      els.errorBanner.textContent = '';
      els.errorBanner.classList.add('hidden');
      return;
    }

    els.errorBanner.textContent = message;
    els.errorBanner.classList.remove('hidden');
  }

  async function api(path, options = {}) {
    const response = await fetch(path, {
      credentials: 'same-origin',
      headers: {
        'Content-Type': 'application/json',
        ...(options.headers || {})
      },
      ...options
    });

    if (response.status === 401) {
      window.location.href = `/login?returnUrl=${encodeURIComponent(window.location.pathname)}`;
      throw new Error('Unauthorized');
    }

    if (!response.ok) {
      let message = `Request failed: ${response.status}`;
      try {
        const data = await response.json();
        message = data.error || message;
      } catch {
        const text = await response.text();
        message = text || message;
      }

      throw new Error(message);
    }

    if (response.status === 204) {
      return null;
    }

    return response.json();
  }

  async function loadSession() {
    state.session = await api('/api/admin/session');
  }

  async function loadUsers() {
    state.users = await api('/api/admin/admin-users');

    if (!state.selectedUserId || !state.users.some(user => user.adminUserId === state.selectedUserId)) {
      state.selectedUserId = state.users[0]?.adminUserId ?? null;
    }

    renderUsers();

    if (state.selectedUserId) {
      await loadUserDetail(state.selectedUserId);
    } else {
      state.selectedDetail = null;
      renderPanel();
    }
  }

  async function loadBillingSettings() {
    state.billingSettings = await api('/api/admin/billing/settings');
    renderBillingPanel();
  }

  async function loadUserDetail(adminUserId) {
    state.selectedDetail = await api(`/api/admin/admin-users/${adminUserId}`);
    renderPanel();
  }

  async function selectUser(adminUserId) {
    if (!adminUserId || adminUserId === state.selectedUserId) {
      return;
    }

    state.selectedUserId = adminUserId;
    state.showCreateForm = false;
    renderUsers();
    await loadUserDetail(adminUserId);
  }

  function renderUsers() {
    if (state.users.length === 0) {
      els.usersList.innerHTML = '<div class="empty-state">No admin users yet.</div>';
      return;
    }

    els.usersList.innerHTML = state.users.map(user => `
      <button class="admin-user-row ${user.adminUserId === state.selectedUserId ? 'selected' : ''}" type="button" data-admin-user-id="${user.adminUserId}">
        <div class="row-top">
          <strong>${escapeHtml(user.displayName)}</strong>
          <span class="state-pill ${user.isActive ? 'active' : 'danger'}">${user.isActive ? 'Active' : 'Disabled'}</span>
        </div>
        <p>${escapeHtml(user.username)}</p>
        <small>${user.canManageClients ? 'Clients' : 'No clients'} • ${user.canManageAdminUsers ? 'Admin users' : 'No admin rights'}</small>
      </button>
    `).join('');
  }

  function renderPanel() {
    const detail = state.selectedDetail;
    const createSection = renderCreateSection();

    if (!detail) {
      els.panel.innerHTML = `
        <div class="stacked-sections">
          ${createSection}
          <div class="empty-state">Select an admin user to edit access.</div>
        </div>
      `;
      return;
    }

    els.panel.innerHTML = `
      <div class="stacked-sections">
        ${createSection}
        <section class="section-card">
          <div class="section-head">
            <div>
              <h3>${escapeHtml(detail.displayName)}</h3>
              <p class="section-description">${escapeHtml(detail.username)}${detail.isCurrentUser ? ' • current session' : ''}</p>
            </div>
            <span class="state-pill ${detail.isActive ? 'active' : 'danger'}">${detail.isActive ? 'Active' : 'Disabled'}</span>
          </div>

          <div class="row-meta">
            <div class="meta-item"><span class="meta-label">Created</span><strong>${formatDate(detail.createdAtUtc)}</strong></div>
            <div class="meta-item"><span class="meta-label">Last login</span><strong>${formatDate(detail.lastLoginAtUtc) || 'Never'}</strong></div>
            <div class="meta-item"><span class="meta-label">Rights</span><strong>${buildRightsLabel(detail)}</strong></div>
          </div>

          <form id="updateAdminUserForm">
            <div class="form-grid">
              <div class="form-field">
                <label for="editUsername">Username</label>
                <input id="editUsername" name="username" value="${escapeHtml(detail.username)}" required />
              </div>
              <div class="form-field">
                <label for="editDisplayName">Display name</label>
                <input id="editDisplayName" name="displayName" value="${escapeHtml(detail.displayName)}" required />
              </div>
            </div>

            <fieldset class="checkbox-grid">
              <legend>Permissions</legend>
              <label class="checkbox-option"><input type="checkbox" name="isActive" ${detail.isActive ? 'checked' : ''} /> User can sign in</label>
              <label class="checkbox-option"><input type="checkbox" name="canManageClients" ${detail.canManageClients ? 'checked' : ''} /> Can manage clients</label>
              <label class="checkbox-option"><input type="checkbox" name="canManageAdminUsers" ${detail.canManageAdminUsers ? 'checked' : ''} /> Can manage admin users</label>
            </fieldset>

            <div class="section-actions">
              <button class="action-button primary" type="submit">Save changes</button>
            </div>
          </form>

          <div class="danger-zone">
            <form id="changePasswordForm" class="inline-form">
              <input name="password" type="password" placeholder="Set a new password" minlength="8" required />
              <button class="action-button" type="submit">Change password</button>
            </form>

            <div class="section-actions">
              <button class="danger-button" type="button" data-action="delete-user" data-admin-user-id="${detail.adminUserId}" ${detail.isCurrentUser ? 'disabled' : ''}>Delete user</button>
            </div>
          </div>
        </section>
      </div>
    `;
  }

  function renderBillingPanel() {
    const settings = state.billingSettings;
    if (!settings) {
      els.billingPanel.innerHTML = '<div class="empty-state">Loading billing settings...</div>';
      return;
    }

    const plans = settings.plans.map(plan => `
      <form class="section-card" data-form="plan" data-plan-id="${plan.id}">
        <div class="section-head">
          <div>
            <h3>${escapeHtml(plan.displayName)}</h3>
            <p class="section-description">${escapeHtml(plan.code)}</p>
          </div>
          <span class="state-pill ${plan.isEnabled ? 'active' : 'danger'}">${plan.isEnabled ? 'Enabled' : 'Disabled'}</span>
        </div>
        <div class="form-grid">
          <div class="form-field">
            <label>Display name</label>
            <input name="displayName" value="${escapeHtml(plan.displayName)}" required />
          </div>
          <div class="form-field">
            <label>Source channel limit</label>
            <input name="channelLimit" type="number" min="1" value="${plan.channelLimit}" required />
          </div>
          <div class="form-field">
            <label>Owned channel limit</label>
            <input name="managedChannelLimit" type="number" min="1" value="${plan.managedChannelLimit}" required />
          </div>
          <div class="form-field">
            <label>Stars price</label>
            <input name="priceStars" type="number" min="0" value="${plan.priceStars}" required />
          </div>
          <div class="form-field">
            <label>Duration days</label>
            <input name="durationDays" type="number" min="1" value="${plan.durationDays ?? ''}" ${plan.isDefaultPlan || plan.priceStars > 0 ? 'disabled' : ''} />
          </div>
          <div class="form-field">
            <label>Sort order</label>
            <input name="sortOrder" type="number" value="${plan.sortOrder}" required />
          </div>
        </div>
        <fieldset class="checkbox-grid">
          <legend>Plan options</legend>
          <label class="checkbox-option"><input type="checkbox" name="isEnabled" ${plan.isEnabled || plan.isDefaultPlan ? 'checked' : ''} ${plan.isDefaultPlan ? 'disabled' : ''} /> Visible for purchase</label>
          <label class="checkbox-option"><input type="checkbox" ${plan.isDefaultPlan ? 'checked' : ''} disabled /> Default free fallback</label>
        </fieldset>
        <div class="section-actions">
          <button class="action-button primary" type="submit">Save plan</button>
        </div>
      </form>
    `).join('');

    const donations = settings.donations.map(option => `
      <form class="section-card" data-form="donation" data-donation-id="${option.id}">
        <div class="section-head">
          <div>
            <h3>${escapeHtml(option.displayName)}</h3>
            <p class="section-description">${escapeHtml(option.code)}</p>
          </div>
          <span class="state-pill ${option.isEnabled ? 'active' : 'danger'}">${option.isEnabled ? 'Enabled' : 'Disabled'}</span>
        </div>
        <div class="form-grid">
          <div class="form-field">
            <label>Display name</label>
            <input name="displayName" value="${escapeHtml(option.displayName)}" required />
          </div>
          <div class="form-field">
            <label>Stars amount</label>
            <input name="starsAmount" type="number" min="1" value="${option.starsAmount}" required />
          </div>
          <div class="form-field">
            <label>Sort order</label>
            <input name="sortOrder" type="number" value="${option.sortOrder}" required />
          </div>
        </div>
        <fieldset class="checkbox-grid">
          <legend>Donation option</legend>
          <label class="checkbox-option"><input type="checkbox" name="isEnabled" ${option.isEnabled ? 'checked' : ''} /> Visible for users</label>
        </fieldset>
        <div class="section-actions">
          <button class="action-button primary" type="submit">Save donation</button>
        </div>
      </form>
    `).join('');

    els.billingPanel.innerHTML = `
      <div class="stacked-sections">
        <section class="section-card">
          <div class="section-head">
            <div>
              <h3>Subscription plans</h3>
              <p class="section-description">Edit source-channel limits, owned-channel limits, and Stars price. Paid Telegram Stars subscriptions renew every 30 days.</p>
            </div>
          </div>
          <div class="stacked-sections">${plans}</div>
        </section>

        <section class="section-card">
          <div class="section-head">
            <div>
              <h3>Donation buttons</h3>
              <p class="section-description">Edit the Stars amounts shown in “Підтримати проект”.</p>
            </div>
          </div>
          <div class="stacked-sections">${donations}</div>
        </section>
      </div>
    `;
  }

  function renderCreateSection() {
    if (!state.showCreateForm) {
      return '';
    }

    return `
      <section class="section-card">
        <div class="section-head">
          <div>
            <h3>Create admin user</h3>
            <p class="section-description">Add a new login for the control center.</p>
          </div>
        </div>

        <form id="createAdminUserForm">
          <div class="form-grid">
            <div class="form-field">
              <label for="createUsername">Username</label>
              <input id="createUsername" name="username" required />
            </div>
            <div class="form-field">
              <label for="createDisplayName">Display name</label>
              <input id="createDisplayName" name="displayName" required />
            </div>
            <div class="form-field">
              <label for="createPassword">Password</label>
              <input id="createPassword" name="password" type="password" minlength="8" required />
            </div>
          </div>

          <fieldset class="checkbox-grid">
            <legend>Permissions</legend>
            <label class="checkbox-option"><input type="checkbox" name="isActive" checked /> User can sign in</label>
            <label class="checkbox-option"><input type="checkbox" name="canManageClients" checked /> Can manage clients</label>
            <label class="checkbox-option"><input type="checkbox" name="canManageAdminUsers" /> Can manage admin users</label>
          </fieldset>

          <div class="section-actions">
            <button class="action-button primary" type="submit">Create user</button>
            <button class="ghost-link" type="button" data-action="cancel-create">Cancel</button>
          </div>
        </form>
      </section>
    `;
  }

  function buildRightsLabel(detail) {
    const rights = [];
    if (detail.canManageClients) rights.push('Clients');
    if (detail.canManageAdminUsers) rights.push('Admin users');
    return rights.length > 0 ? rights.join(', ') : 'Read-only dashboard';
  }

  function formatDate(value) {
    if (!value) return '';
    return new Date(value).toLocaleString();
  }

  async function handleCreate(form) {
    const body = {
      username: form.username.value.trim(),
      displayName: form.displayName.value.trim(),
      password: form.password.value,
      isActive: form.isActive.checked,
      canManageClients: form.canManageClients.checked,
      canManageAdminUsers: form.canManageAdminUsers.checked
    };

    const created = await api('/api/admin/admin-users', {
      method: 'POST',
      body: JSON.stringify(body)
    });

    state.showCreateForm = false;
    state.selectedUserId = created.adminUserId;
    await loadUsers();
  }

  async function handleUpdate(form) {
    const body = {
      username: form.username.value.trim(),
      displayName: form.displayName.value.trim(),
      isActive: form.isActive.checked,
      canManageClients: form.canManageClients.checked,
      canManageAdminUsers: form.canManageAdminUsers.checked
    };

    await api(`/api/admin/admin-users/${state.selectedUserId}`, {
      method: 'PATCH',
      body: JSON.stringify(body)
    });

    await loadUsers();
  }

  async function handlePasswordChange(form) {
    await api(`/api/admin/admin-users/${state.selectedUserId}/password`, {
      method: 'PATCH',
      body: JSON.stringify({ password: form.password.value })
    });

    form.reset();
    setError('');
  }

  async function handleDelete(adminUserId) {
    if (!confirm('Delete this admin user?')) {
      return;
    }

    await api(`/api/admin/admin-users/${adminUserId}`, {
      method: 'DELETE'
    });

    state.selectedUserId = null;
    state.selectedDetail = null;
    await loadUsers();
  }

  async function handlePlanSave(form) {
    const planId = form.dataset.planId;
    await api(`/api/admin/billing/plans/${planId}`, {
      method: 'PATCH',
      body: JSON.stringify({
        displayName: form.displayName.value.trim(),
        channelLimit: Number(form.channelLimit.value),
        managedChannelLimit: Number(form.managedChannelLimit.value),
        priceStars: Number(form.priceStars.value),
        durationDays: form.durationDays.disabled || !form.durationDays.value ? null : Number(form.durationDays.value),
        isEnabled: form.isEnabled.checked,
        sortOrder: Number(form.sortOrder.value)
      })
    });

    await loadBillingSettings();
  }

  async function handleDonationSave(form) {
    const donationId = form.dataset.donationId;
    await api(`/api/admin/billing/donations/${donationId}`, {
      method: 'PATCH',
      body: JSON.stringify({
        displayName: form.displayName.value.trim(),
        starsAmount: Number(form.starsAmount.value),
        isEnabled: form.isEnabled.checked,
        sortOrder: Number(form.sortOrder.value)
      })
    });

    await loadBillingSettings();
  }

  els.usersList.addEventListener('click', async event => {
    const row = event.target.closest('[data-admin-user-id]');
    if (!row) return;

    try {
      await selectUser(row.dataset.adminUserId);
    } catch (error) {
      setError(error.message);
    }
  });

  els.panel.addEventListener('click', async event => {
    const actionEl = event.target.closest('[data-action]');
    if (!actionEl) return;

    try {
      if (actionEl.dataset.action === 'cancel-create') {
        state.showCreateForm = false;
        renderPanel();
      }

      if (actionEl.dataset.action === 'delete-user') {
        await handleDelete(actionEl.dataset.adminUserId);
      }
    } catch (error) {
      setError(error.message);
    }
  });

  els.panel.addEventListener('submit', async event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) return;

    event.preventDefault();

    try {
      if (form.id === 'createAdminUserForm') {
        await handleCreate(form);
      }

      if (form.id === 'updateAdminUserForm') {
        await handleUpdate(form);
      }

      if (form.id === 'changePasswordForm') {
        await handlePasswordChange(form);
      }

      if (form.dataset.form === 'plan') {
        await handlePlanSave(form);
      }

      if (form.dataset.form === 'donation') {
        await handleDonationSave(form);
      }
    } catch (error) {
      setError(error.message);
    }
  });

  els.billingPanel.addEventListener('submit', async event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) return;

    event.preventDefault();

    try {
      if (form.dataset.form === 'plan') {
        await handlePlanSave(form);
      }

      if (form.dataset.form === 'donation') {
        await handleDonationSave(form);
      }
    } catch (error) {
      setError(error.message);
    }
  });

  els.newUserButton.addEventListener('click', () => {
    state.showCreateForm = !state.showCreateForm;
    renderPanel();
  });

  async function init() {
    try {
      await loadSession();
      await loadUsers();
      await loadBillingSettings();
    } catch (error) {
      setError(error.message);
    }
  }

  init();
})();
