(function () {
  const state = {
    search: '',
    clientsPage: 1,
    clientsPageSize: 20,
    clients: null,
    selectedClientId: null,
    clientDetail: null,
    botSubscriptionsExpanded: false,
    botSubscriptionsPage: null,
    managedSubscriptionsExpanded: new Set(),
    managedSubscriptionsPages: new Map(),
    loading: false,
    loadingDetail: false
  };

  const els = {
    searchInput: document.getElementById('searchInput'),
    refreshButton: document.getElementById('refreshButton'),
    errorBanner: document.getElementById('errorBanner'),
    clientsCount: document.getElementById('clientsCount'),
    clientsList: document.getElementById('clientsList'),
    clientsPager: document.getElementById('clientsPager'),
    detailPanel: document.getElementById('detailPanel')
  };

  let searchTimer = null;

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
      const text = await response.text();
      throw new Error(text || `Request failed: ${response.status}`);
    }

    if (response.status === 204) {
      return null;
    }

    return response.json();
  }

  function totalPages(pageData) {
    if (!pageData) return 1;
    return Math.max(1, Math.ceil(pageData.totalCount / pageData.pageSize));
  }

  async function loadClients(page = state.clientsPage) {
    state.loading = true;
    setError('');

    try {
      const query = new URLSearchParams({
        page: String(page),
        pageSize: String(state.clientsPageSize)
      });

      if (state.search.trim()) {
        query.set('search', state.search.trim());
      }

      state.clients = await api(`/api/admin/clients?${query.toString()}`);
      state.clientsPage = state.clients.page;
      els.clientsCount.textContent = String(state.clients.totalCount);

      if (!state.selectedClientId || !state.clients.items.some(x => x.userId === state.selectedClientId)) {
        state.selectedClientId = state.clients.items[0]?.userId ?? null;
        resetExpanded();
      }

      renderClients();
      renderClientsPager();

      if (state.selectedClientId) {
        await loadClientDetail(state.selectedClientId);
      } else {
        state.clientDetail = null;
        renderDetail();
      }
    } catch (error) {
      setError(error.message);
    } finally {
      state.loading = false;
    }
  }

  async function loadClientDetail(userId) {
    state.loadingDetail = true;
    renderDetail();

    try {
      state.clientDetail = await api(`/api/admin/clients/${userId}`);
      renderDetail();
    } catch (error) {
      setError(error.message);
    } finally {
      state.loadingDetail = false;
      renderDetail();
    }
  }

  async function selectClient(userId) {
    if (!userId || userId === state.selectedClientId) {
      return;
    }

    state.selectedClientId = userId;
    resetExpanded();
    renderClients();
    await loadClientDetail(userId);
  }

  function resetExpanded() {
    state.botSubscriptionsExpanded = false;
    state.botSubscriptionsPage = null;
    state.managedSubscriptionsExpanded = new Set();
    state.managedSubscriptionsPages = new Map();
  }

  async function toggleBotSubscriptions() {
    state.botSubscriptionsExpanded = !state.botSubscriptionsExpanded;
    renderDetail();

    if (state.botSubscriptionsExpanded && !state.botSubscriptionsPage) {
      await loadBotSubscriptionsPage(1);
    }
  }

  async function loadBotSubscriptionsPage(page) {
    if (!state.selectedClientId) return;
    state.botSubscriptionsPage = null;
    renderDetail();
    try {
      state.botSubscriptionsPage = await api(`/api/admin/clients/${state.selectedClientId}/bot-subscriptions?page=${page}&pageSize=10`);
      renderDetail();
    } catch (error) {
      setError(error.message);
    }
  }

  async function toggleManagedSubscriptions(managedChannelId) {
    if (state.managedSubscriptionsExpanded.has(managedChannelId)) {
      state.managedSubscriptionsExpanded.delete(managedChannelId);
      state.managedSubscriptionsPages.delete(managedChannelId);
      renderDetail();
      return;
    }

    state.managedSubscriptionsExpanded.add(managedChannelId);
    renderDetail();
    await loadManagedSubscriptionsPage(managedChannelId, 1);
  }

  async function loadManagedSubscriptionsPage(managedChannelId, page) {
    try {
      const pageData = await api(`/api/admin/managed-channels/${managedChannelId}/subscriptions?page=${page}&pageSize=10`);
      state.managedSubscriptionsPages.set(managedChannelId, pageData);
      renderDetail();
    } catch (error) {
      setError(error.message);
    }
  }

  async function patchJson(path, body) {
    await api(path, {
      method: 'PATCH',
      body: JSON.stringify(body)
    });
  }

  async function postJson(path, body) {
    await api(path, {
      method: 'POST',
      body: JSON.stringify(body)
    });
  }

  async function deleteRequest(path) {
    await api(path, { method: 'DELETE' });
  }

  async function refreshDetail() {
    if (state.selectedClientId) {
      await loadClientDetail(state.selectedClientId);

      if (state.botSubscriptionsExpanded) {
        await loadBotSubscriptionsPage(state.botSubscriptionsPage?.page ?? 1);
      }

      for (const managedChannelId of Array.from(state.managedSubscriptionsExpanded)) {
        await loadManagedSubscriptionsPage(managedChannelId, state.managedSubscriptionsPages.get(managedChannelId)?.page ?? 1);
      }
    }
  }

  function renderClients() {
    const pageData = state.clients;
    if (!pageData || pageData.items.length === 0) {
      els.clientsList.innerHTML = '<div class="empty-state">No clients found.</div>';
      return;
    }

    els.clientsList.innerHTML = pageData.items.map(client => `
      <button class="client-row ${client.userId === state.selectedClientId ? 'selected' : ''}" type="button" data-user-id="${client.userId}">
        <div class="row-top">
          <strong>${escapeHtml(client.displayName)}</strong>
          <span class="state-pill ${client.isBlockedBot ? 'blocked' : 'active'}">${client.isBlockedBot ? 'Blocked' : 'Active'}</span>
        </div>
        <p>${client.telegramUsername ? '@' + escapeHtml(client.telegramUsername.replace(/^@/, '')) : 'No username'}</p>
        <small>${client.managedChannelsCount} channels • ${client.totalSubscriptionsCount} subscriptions</small>
      </button>
    `).join('');
  }

  function renderClientsPager() {
    if (!state.clients) {
      els.clientsPager.innerHTML = '';
      return;
    }

    const pageCount = totalPages(state.clients);
    els.clientsPager.innerHTML = `
      <button type="button" ${state.clients.page <= 1 ? 'disabled' : ''} data-page-action="clients-prev">Previous</button>
      <span>Page ${state.clients.page} of ${pageCount}</span>
      <button type="button" ${state.clients.page >= pageCount ? 'disabled' : ''} data-page-action="clients-next">Next</button>
    `;
  }

  function renderDetail() {
    if (state.loadingDetail) {
      els.detailPanel.innerHTML = '<div class="empty-state">Loading client details...</div>';
      return;
    }

    const detail = state.clientDetail;
    if (!detail) {
      els.detailPanel.innerHTML = '<div class="empty-state">Select a client to load details.</div>';
      return;
    }

    const botSection = renderBotSubscriptionsSection(detail);
    const channelsSection = renderManagedChannelsSection(detail);

    els.detailPanel.innerHTML = `
      <div class="detail-head">
        <div>
          <p class="eyebrow">Client detail</p>
          <h2>${escapeHtml(detail.displayName)}</h2>
          <p class="subtitle">${detail.telegramUsername ? '@' + escapeHtml(detail.telegramUsername.replace(/^@/, '')) : 'No username'} • Telegram ID ${detail.telegramUserId}</p>
        </div>
        <div class="row-actions">
          <button class="action-button" type="button" data-action="toggle-block" data-user-id="${detail.userId}" data-is-blocked="${detail.isBlockedBot}">
            ${detail.isBlockedBot ? 'Unblock bot access' : 'Block bot access'}
          </button>
        </div>
      </div>

      <dl class="stats-grid">
        <div class="stat-box"><dt>Plan</dt><dd>${escapeHtml(detail.currentPlanName)}</dd></div>
        <div class="stat-box"><dt>Subscription limit</dt><dd>${detail.usedSubscriptionsCount} / ${detail.subscriptionLimit}</dd></div>
        <div class="stat-box"><dt>Extra slots</dt><dd>${detail.extraSubscriptionSlots}</dd></div>
        <div class="stat-box"><dt>Plan expires</dt><dd>${formatDate(detail.subscriptionExpiresAtUtc) || 'Free plan'}</dd></div>
        <div class="stat-box"><dt>Language</dt><dd>${escapeHtml(detail.preferredLanguageCode)}</dd></div>
        <div class="stat-box"><dt>Joined</dt><dd>${formatDate(detail.createdAtUtc)}</dd></div>
        <div class="stat-box"><dt>Channels</dt><dd>${detail.activeManagedChannelsCount} / ${detail.managedChannelsCount} active</dd></div>
        <div class="stat-box"><dt>Bot subscriptions</dt><dd>${detail.activeBotSubscriptionsCount} / ${detail.botSubscriptionsCount} active</dd></div>
        <div class="stat-box"><dt>Channel subscriptions</dt><dd>${detail.activeManagedChannelSubscriptionsCount} / ${detail.totalManagedChannelSubscriptionsCount} active</dd></div>
      </dl>

      <div class="sections-stack">
        <section class="section-card">
          <div class="section-head">
            <div>
              <h3>Subscription allowance</h3>
              <p class="section-description">Add extra source-channel slots for this user on top of the current plan.</p>
            </div>
          </div>
          <form class="inline-form" data-form="subscription-allowance">
            <input name="extraSubscriptionSlots" type="number" min="0" value="${detail.extraSubscriptionSlots}" />
            <button class="action-button primary" type="submit">Save slots</button>
          </form>
        </section>
        ${botSection}
        ${channelsSection}
      </div>
    `;
  }

  function renderBotSubscriptionsSection(detail) {
    let content = '';
    if (detail.botSubscriptionsCount === 0) {
      content = '<div class="empty-state">This user has no direct subscriptions in the bot.</div>';
    } else if (state.botSubscriptionsExpanded) {
      const pageData = state.botSubscriptionsPage;
      if (!pageData) {
        content = '<div class="empty-state">Loading bot subscriptions...</div>';
      } else {
        const rows = pageData.items.map(item => `
          <article class="subscription-row">
            <div class="row-top">
              <div>
                <strong>${escapeHtml(item.channelName)}</strong>
                <p class="muted">${escapeHtml(item.channelReference)}</p>
              </div>
              <div class="row-actions">
                <span class="state-pill ${item.isActive ? 'active' : 'paused'}">${item.isActive ? 'Active' : 'Paused'}</span>
                <button class="toggle-button" type="button" data-action="toggle-bot-subscription" data-subscription-id="${item.subscriptionId}" data-is-active="${item.isActive}">
                  ${item.isActive ? 'Pause' : 'Resume'}
                </button>
                <button class="danger-button" type="button" data-action="delete-bot-subscription" data-subscription-id="${item.subscriptionId}">Delete</button>
              </div>
            </div>
            <div class="row-meta">
              <div class="meta-item"><span class="meta-label">Type</span><strong>Direct bot subscription</strong></div>
              <div class="meta-item"><span class="meta-label">Last delivered</span><strong>${formatDate(item.lastDeliveredAtUtc) || 'No data'}</strong></div>
            </div>
          </article>
        `).join('');

        content = `
          <div class="rows-stack">${rows}</div>
          ${renderPager('bot', pageData)}
        `;
      }
    }

    return `
      <section class="section-card">
        <div class="section-head">
          <div>
            <h3>Bot subscriptions</h3>
            <p class="section-description">Direct subscriptions created in the bot.</p>
          </div>
          <button class="action-button" type="button" data-action="toggle-bot-list">
            ${state.botSubscriptionsExpanded ? 'Hide list' : `Show list (${detail.botSubscriptionsCount})`}
          </button>
        </div>
        <form class="inline-form" data-form="create-bot-subscription">
          <input name="channelReference" placeholder="Add channel reference or invite link" />
          <button class="action-button primary" type="submit">Add</button>
        </form>
        ${content}
      </section>
    `;
  }

  function renderManagedChannelsSection(detail) {
    if (detail.managedChannelsCount === 0) {
      return `
        <section class="section-card">
          <div class="section-head">
            <div>
              <h3>Owned channels</h3>
              <p class="section-description">Channels where the bot already has admin rights.</p>
            </div>
          </div>
          <div class="empty-state">This client has no managed channels yet.</div>
        </section>
      `;
    }

    const rows = detail.managedChannels.map(channel => {
      const isExpanded = state.managedSubscriptionsExpanded.has(channel.managedChannelId);
      const pageData = state.managedSubscriptionsPages.get(channel.managedChannelId);
      let nested = '';

      if (isExpanded) {
        if (!pageData) {
          nested = '<div class="nested-panel"><div class="empty-state">Loading channel subscriptions...</div></div>';
        } else if (pageData.items.length === 0) {
          nested = '<div class="nested-panel"><div class="empty-state">This channel has no source subscriptions.</div></div>';
        } else {
          nested = `
            <div class="nested-panel">
              <div class="rows-stack">
                ${pageData.items.map(item => `
                  <article class="subscription-row">
                    <div class="row-top">
                      <div>
                        <strong>${escapeHtml(item.channelName)}</strong>
                        <p class="muted">${escapeHtml(item.channelReference)}</p>
                      </div>
                      <div class="row-actions">
                        <span class="state-pill ${item.isActive ? 'active' : 'paused'}">${item.isActive ? 'Active' : 'Paused'}</span>
                        <button class="toggle-button" type="button" data-action="toggle-managed-subscription" data-subscription-id="${item.subscriptionId}" data-is-active="${item.isActive}">
                          ${item.isActive ? 'Pause' : 'Resume'}
                        </button>
                        <button class="danger-button" type="button" data-action="delete-managed-subscription" data-subscription-id="${item.subscriptionId}">Delete</button>
                      </div>
                    </div>
                    <div class="row-meta">
                      <div class="meta-item"><span class="meta-label">Last delivered</span><strong>${formatDate(item.lastDeliveredAtUtc) || 'No delivery yet'}</strong></div>
                    </div>
                  </article>
                `).join('')}
              </div>
              ${renderPager(`managed:${channel.managedChannelId}`, pageData)}
            </div>
          `;
        }
      }

      return `
        <article class="channel-row">
          <div class="row-top">
            <div>
              <strong>${escapeHtml(channel.channelName)}</strong>
              <p class="muted">${escapeHtml(channel.channelReference)}</p>
            </div>
            <div class="row-actions">
              <span class="state-pill ${channel.isActive ? 'active' : 'paused'}">${channel.isActive ? 'Running' : 'Paused'}</span>
              <button class="toggle-button" type="button" data-action="toggle-managed-channel" data-managed-channel-id="${channel.managedChannelId}" data-is-active="${channel.isActive}">
                ${channel.isActive ? 'Pause channel' : 'Resume channel'}
              </button>
              <button class="action-button" type="button" data-action="toggle-managed-list" data-managed-channel-id="${channel.managedChannelId}">
                ${isExpanded ? 'Hide subscriptions' : `Show subscriptions (${channel.subscriptionCount})`}
              </button>
              <button class="danger-button" type="button" data-action="delete-managed-channel" data-managed-channel-id="${channel.managedChannelId}">Delete</button>
            </div>
          </div>
          <div class="row-meta">
            <div class="meta-item"><span class="meta-label">Subscriptions via bot</span><strong>${channel.subscriptionCount}</strong></div>
            <div class="meta-item"><span class="meta-label">Active via bot</span><strong>${channel.activeSubscriptionCount}</strong></div>
            <div class="meta-item"><span class="meta-label">Last success</span><strong>${formatDate(channel.lastWriteSucceededAtUtc) || 'No data'}</strong></div>
          </div>
          <form class="inline-form" data-form="create-managed-subscription" data-managed-channel-id="${channel.managedChannelId}">
            <input name="channelReference" placeholder="Add source channel to this destination channel" />
            <button class="action-button primary" type="submit">Add source</button>
          </form>
          ${channel.lastWriteError ? `<p class="muted">${escapeHtml(channel.lastWriteError)}</p>` : ''}
          ${nested}
        </article>
      `;
    }).join('');

    return `
      <section class="section-card">
        <div class="section-head">
          <div>
            <h3>Owned channels</h3>
            <p class="section-description">Channels where the bot can forward tracked source posts.</p>
          </div>
        </div>
        <div class="rows-stack">${rows}</div>
      </section>
    `;
  }

  function renderPager(key, pageData) {
    const maxPage = totalPages(pageData);
    return `
      <div class="pager">
        <button type="button" ${pageData.page <= 1 ? 'disabled' : ''} data-action="pager-prev" data-key="${key}" data-page="${pageData.page - 1}">Previous</button>
        <span>Page ${pageData.page} of ${maxPage}</span>
        <button type="button" ${pageData.page >= maxPage ? 'disabled' : ''} data-action="pager-next" data-key="${key}" data-page="${pageData.page + 1}">Next</button>
      </div>
    `;
  }

  function formatDate(value) {
    if (!value) return '';
    return new Date(value).toLocaleString();
  }

  async function handleAction(action, dataset) {
    switch (action) {
      case 'toggle-block':
        await patchJson(`/api/admin/clients/${dataset.userId}`, { isBlockedBot: dataset.isBlocked !== 'true' });
        await loadClients(state.clientsPage);
        break;
      case 'toggle-bot-list':
        await toggleBotSubscriptions();
        break;
      case 'toggle-bot-subscription':
        await patchJson(`/api/admin/bot-subscriptions/${dataset.subscriptionId}`, { isActive: dataset.isActive !== 'true' });
        await refreshDetail();
        break;
      case 'delete-bot-subscription':
        if (confirm('Delete this bot subscription?')) {
          await deleteRequest(`/api/admin/bot-subscriptions/${dataset.subscriptionId}`);
          await refreshDetail();
          await loadClients(state.clientsPage);
        }
        break;
      case 'toggle-managed-channel':
        await patchJson(`/api/admin/managed-channels/${dataset.managedChannelId}`, { isActive: dataset.isActive !== 'true' });
        await refreshDetail();
        break;
      case 'delete-managed-channel':
        if (confirm('Delete this managed channel?')) {
          await deleteRequest(`/api/admin/managed-channels/${dataset.managedChannelId}`);
          await loadClients(state.clientsPage);
        }
        break;
      case 'toggle-managed-list':
        await toggleManagedSubscriptions(dataset.managedChannelId);
        break;
      case 'toggle-managed-subscription':
        await patchJson(`/api/admin/managed-channel-subscriptions/${dataset.subscriptionId}`, { isActive: dataset.isActive !== 'true' });
        await refreshDetail();
        break;
      case 'delete-managed-subscription':
        if (confirm('Delete this source subscription?')) {
          await deleteRequest(`/api/admin/managed-channel-subscriptions/${dataset.subscriptionId}`);
          await refreshDetail();
        }
        break;
      case 'pager-prev':
      case 'pager-next':
        await handlePager(dataset.key, Number(dataset.page));
        break;
      default:
        break;
    }
  }

  async function handlePager(key, page) {
    if (key === 'bot') {
      await loadBotSubscriptionsPage(page);
      return;
    }

    if (key.startsWith('managed:')) {
      const managedChannelId = key.split(':')[1];
      await loadManagedSubscriptionsPage(managedChannelId, page);
    }
  }

  async function handleForm(form) {
    const formType = form.dataset.form;
    if (formType === 'subscription-allowance') {
      await patchJson(`/api/admin/clients/${state.selectedClientId}/subscription-allowance`, {
        extraSubscriptionSlots: Number(form.extraSubscriptionSlots.value)
      });
      await refreshDetail();
      await loadClients(state.clientsPage);
      return;
    }

    const reference = form.channelReference.value.trim();
    if (!reference) return;

    if (formType === 'create-bot-subscription') {
      await postJson(`/api/admin/clients/${state.selectedClientId}/bot-subscriptions`, { channelReference: reference });
      form.reset();
      state.botSubscriptionsExpanded = true;
      await refreshDetail();
      await loadClients(state.clientsPage);
      return;
    }

    if (formType === 'create-managed-subscription') {
      await postJson(`/api/admin/managed-channels/${form.dataset.managedChannelId}/subscriptions`, { channelReference: reference });
      form.reset();
      state.managedSubscriptionsExpanded.add(form.dataset.managedChannelId);
      await refreshDetail();
    }
  }

  els.clientsList.addEventListener('click', async (event) => {
    const row = event.target.closest('[data-user-id]');
    if (!row) return;
    await selectClient(row.dataset.userId);
  });

  els.clientsPager.addEventListener('click', async (event) => {
    const action = event.target.dataset.pageAction;
    if (!action || !state.clients) return;
    await loadClients(action === 'clients-prev' ? state.clients.page - 1 : state.clients.page + 1);
  });

  els.detailPanel.addEventListener('click', async (event) => {
    const actionEl = event.target.closest('[data-action]');
    if (!actionEl) return;
    event.preventDefault();
    try {
      await handleAction(actionEl.dataset.action, actionEl.dataset);
    } catch (error) {
      setError(error.message);
    }
  });

  els.detailPanel.addEventListener('submit', async (event) => {
    const form = event.target.closest('form[data-form]');
    if (!form) return;
    event.preventDefault();
    try {
      await handleForm(form);
    } catch (error) {
      setError(error.message);
    }
  });

  els.searchInput.addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(async () => {
      state.search = els.searchInput.value;
      state.clientsPage = 1;
      await loadClients(1);
    }, 250);
  });

  els.refreshButton.addEventListener('click', async () => {
    await loadClients(state.clientsPage);
  });

  loadClients(1);
})();
