/**
 * Industrial IoT & Digital Twin Platform - Shared Global State & UX Guard Module
 * Version: 2.0.0
 * Provides:
 *  1. Global Connection State Management (SignalR + REST Sync)
 *  2. SignalR Reconnect Banner UX
 *  3. Command Guard (Disables write actions when PLC is offline)
 *  4. Stale Data Visual Protection (Dims gauges/cards when data stream halts)
 *  5. RBAC Guard (Enforces role permissions across pages)
 */

(function (window) {
    'use strict';

    const SharedState = {
        // State store
        isPlcConnected: false,
        plcStateName: 'Disconnected',
        isSignalRConnected: false,
        signalRState: 'Disconnected',
        currentScenario: 'Normal',
        userRole: sessionStorage.getItem('roleKey') || 'Viewer',
        userName: sessionStorage.getItem('username') || 'Misafir',

        // Event callbacks
        plcListeners: [],
        signalRListeners: [],
        telemetryListeners: [],

        // Hub Connection instance
        hubConnection: null,

        /**
         * Initialize the shared state and guards
         */
        init: function (options = {}) {
            this.injectStyles();
            this.injectBanner();
            this.enforceRbac();
            this.initSignalR(options.hubUrl || '/sensorHub');
            this.pollConnectionStatus();

            // Periodic connection sync every 5 seconds
            setInterval(() => this.pollConnectionStatus(), 5000);
        },

        /**
         * Subscribe to PLC connection changes
         */
        onPlcConnectionChanged: function (callback) {
            if (typeof callback === 'function') {
                this.plcListeners.push(callback);
                // Trigger immediate with current state
                callback(this.isPlcConnected, this.plcStateName);
            }
        },

        /**
         * Subscribe to SignalR connection changes
         */
        onSignalRConnectionChanged: function (callback) {
            if (typeof callback === 'function') {
                this.signalRListeners.push(callback);
                callback(this.isSignalRConnected, this.signalRState);
            }
        },

        /**
         * Subscribe to real-time telemetry updates
         */
        onTelemetry: function (callback) {
            if (typeof callback === 'function') {
                this.telemetryListeners.push(callback);
            }
        },

        /**
         * Update PLC connection state and notify all UI subscribers
         */
        setPlcConnectionState: function (isConnected, stateName = '') {
            const changed = this.isPlcConnected !== isConnected || (stateName && this.plcStateName !== stateName);
            this.isPlcConnected = isConnected;
            if (stateName) this.plcStateName = stateName;

            this.applyCommandGuard(isConnected);
            this.applyStaleDataGuard(!isConnected || !this.isSignalRConnected);

            if (changed) {
                this.plcListeners.forEach(cb => {
                    try { cb(this.isPlcConnected, this.plcStateName); } catch (e) { console.error(e); }
                });
            }
        },

        /**
         * Initialize SignalR with robust exponential reconnect
         */
        initSignalR: function (hubUrl) {
            if (typeof signalR === 'undefined') {
                console.warn('[SharedState] SignalR library is not loaded on this page.');
                return;
            }

            try {
                this.hubConnection = new signalR.HubConnectionBuilder()
                    .withUrl(hubUrl)
                    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                    .build();

                this.hubConnection.onreconnecting((err) => {
                    this.isSignalRConnected = false;
                    this.signalRState = 'Reconnecting';
                    this.showBanner('warning', '⚠️ Sunucu ile bağlantı koptu, yeniden bağlanılıyor...');
                    this.applyStaleDataGuard(true);
                    this.signalRListeners.forEach(cb => cb(false, 'Reconnecting'));
                });

                this.hubConnection.onreconnected((connectionId) => {
                    this.isSignalRConnected = true;
                    this.signalRState = 'Connected';
                    this.showBanner('success', '✅ Sunucu bağlantısı yeniden sağlandı.', 3000);
                    this.applyStaleDataGuard(!this.isPlcConnected);
                    this.signalRListeners.forEach(cb => cb(true, 'Connected'));
                    this.pollConnectionStatus();
                });

                this.hubConnection.onclose((err) => {
                    this.isSignalRConnected = false;
                    this.signalRState = 'Disconnected';
                    this.showBanner('danger', '❌ Sunucu bağlantısı kapandı. Sayfayı yenilemeyi deneyin.');
                    this.applyStaleDataGuard(true);
                    this.signalRListeners.forEach(cb => cb(false, 'Disconnected'));
                });

                // Listen to server hub events
                this.hubConnection.on('ReceiveSensorData', (data) => {
                    this.setPlcConnectionState(true, 'Connected');
                    this.telemetryListeners.forEach(cb => cb(data));
                });

                this.hubConnection.on('ReceiveConnectionStatus', (isConnected, stateName) => {
                    this.setPlcConnectionState(isConnected, stateName || (isConnected ? 'Connected' : 'Disconnected'));
                });

                this.hubConnection.on('ReceivePlcConnectionState', (state) => {
                    const isConn = state === 'Connected' || state === 'Running';
                    this.setPlcConnectionState(isConn, state);
                });

                this.hubConnection.start()
                    .then(() => {
                        this.isSignalRConnected = true;
                        this.signalRState = 'Connected';
                        this.hideBanner();
                        this.signalRListeners.forEach(cb => cb(true, 'Connected'));
                    })
                    .catch(err => {
                        console.warn('[SharedState] SignalR bağlantı hatası:', err);
                        this.isSignalRConnected = false;
                        this.signalRState = 'Disconnected';
                        this.showBanner('warning', '⚠️ Canlı veri sunucusuna bağlanılamadı (Offline Mod).');
                        this.applyStaleDataGuard(true);
                    });
            } catch (err) {
                console.error('[SharedState] Hub initialization failed:', err);
            }
        },

        /**
         * REST polling for initial and fallback sync
         */
        pollConnectionStatus: async function () {
            try {
                const res = await fetch('/api/Sensor/connection-status');
                if (res.ok) {
                    const data = await res.json();
                    this.setPlcConnectionState(data.isConnected, data.state);
                }
            } catch (e) {
                // If API is unreachable, mark as disconnected
                if (!this.isSignalRConnected) {
                    this.setPlcConnectionState(false, 'Disconnected');
                }
            }
        },

        /**
         * Command Guard: Enable/disable all command controls based on PLC state
         */
        applyCommandGuard: function (isConnected) {
            const commandButtons = document.querySelectorAll('.command-guarded, #btnSendCommand, #btnEmergencyStop, #btnManualTrigger');
            const commandInputs = document.querySelectorAll('.command-input, #variableAddress, #dataTypeSelect, #commandValueInput');
            const guardNotice = document.getElementById('command-guard-notice');

            commandButtons.forEach(btn => {
                // If user is viewer, always keep disabled
                if (this.userRole === 'Viewer') {
                    btn.disabled = true;
                    btn.title = "İzleyici rolü komut gönderemez.";
                    return;
                }

                btn.disabled = !isConnected;
                if (!isConnected) {
                    btn.setAttribute('data-original-title', btn.title || '');
                    btn.title = "PLC Çevrimdışı - Komut Gönderilemez!";
                } else {
                    btn.title = btn.getAttribute('data-original-title') || '';
                }
            });

            commandInputs.forEach(input => {
                if (this.userRole === 'Viewer') {
                    input.disabled = true;
                } else {
                    input.disabled = !isConnected;
                }
            });

            if (guardNotice) {
                if (!isConnected) {
                    guardNotice.classList.remove('d-none');
                    guardNotice.innerHTML = '<i class="fa-solid fa-lock me-2 text-warning"></i><b>PLC Çevrimdışı:</b> Güvenlik kilidi aktif. Komut göndermek için lütfen önce PLC bağlantısını kurun.';
                } else {
                    guardNotice.classList.add('d-none');
                }
            }
        },

        /**
         * Stale Data Guard: Visual cue when real-time data stream halts
         */
        applyStaleDataGuard: function (isStale) {
            const cards = document.querySelectorAll('.live-metric-card, .gauge-card, .stale-guarded');
            cards.forEach(card => {
                if (isStale) {
                    card.classList.add('stale-data-active');
                    let badge = card.querySelector('.stale-badge');
                    if (!badge) {
                        badge = document.createElement('span');
                        badge.className = 'badge bg-secondary stale-badge ms-2';
                        badge.innerHTML = '<i class="fa-solid fa-clock-rotate-left me-1"></i>Veri Akışı Durdu';
                        const header = card.querySelector('.card-header, h5, h6');
                        if (header) header.appendChild(badge);
                    }
                    badge.style.display = 'inline-block';
                } else {
                    card.classList.remove('stale-data-active');
                    const badge = card.querySelector('.stale-badge');
                    if (badge) badge.style.display = 'none';
                }
            });
        },

        /**
         * Role-Based Access Control (RBAC) Client Security Guard
         */
        enforceRbac: function () {
            const role = this.userRole;
            const currentPage = window.location.pathname.split('/').pop() || 'index.html';

            // 1. Page level protection
            if (role === 'Viewer') {
                if (currentPage === 'control.html' || currentPage === 'tags.html') {
                    alert('Erişim Engellendi (403): İzleyici (Viewer) rolü PLC Komut ve Değişken yönetimi sayfalarına erişemez.');
                    window.location.replace('index.html');
                    return;
                }
            }

            // 2. Element level protection
            if (role === 'Viewer') {
                document.querySelectorAll('.admin-only, .programmer-only, .write-operation').forEach(el => {
                    el.style.display = 'none';
                });

                document.querySelectorAll('.viewer-readonly').forEach(el => {
                    el.disabled = true;
                    el.title = "İzleyici rolü salt-okunur yetkiye sahiptir.";
                });
            }

            // 3. User badge presentation
            const badgeEl = document.getElementById('user-role-badge') || document.querySelector('.user-role-text');
            if (badgeEl) {
                let badgeClass = role === 'Admin' ? 'bg-danger' : (role === 'Programmer' || role === 'Programcı' ? 'bg-warning text-dark' : 'bg-info text-dark');
                badgeEl.className = `badge ${badgeClass}`;
                badgeEl.innerText = role === 'Admin' ? 'Yönetici' : (role === 'Programmer' || role === 'Programcı' ? 'Programcı' : 'İzleyici');
            }
        },

        /**
         * Floating notification banner helpers
         */
        injectBanner: function () {
            if (document.getElementById('global-reconnect-banner')) return;

            const banner = document.createElement('div');
            banner.id = 'global-reconnect-banner';
            banner.className = 'global-reconnect-banner d-none alert alert-warning text-center py-2 mb-0 shadow';
            banner.innerHTML = '<span id="global-banner-text"><i class="fa-solid fa-circle-notch fa-spin me-2"></i>Bağlantı kontrol ediliyor...</span>';
            document.body.prepend(banner);
        },

        showBanner: function (type, text, autoHideMs = 0) {
            const banner = document.getElementById('global-reconnect-banner');
            const bannerText = document.getElementById('global-banner-text');
            if (!banner || !bannerText) return;

            banner.className = `global-reconnect-banner alert alert-${type} text-center py-2 mb-0 shadow`;
            bannerText.innerHTML = text;
            banner.classList.remove('d-none');

            if (autoHideMs > 0) {
                setTimeout(() => this.hideBanner(), autoHideMs);
            }
        },

        hideBanner: function () {
            const banner = document.getElementById('global-reconnect-banner');
            if (banner) banner.classList.add('d-none');
        },

        /**
         * Shared styling tokens for guards and banners
         */
        injectStyles: function () {
            if (document.getElementById('shared-state-styles')) return;

            const style = document.createElement('style');
            style.id = 'shared-state-styles';
            style.innerHTML = `
                .global-reconnect-banner {
                    position: sticky;
                    top: 0;
                    z-index: 9999;
                    font-weight: 600;
                    border-radius: 0;
                    border-bottom: 2px solid rgba(0,0,0,0.15);
                    transition: all 0.3s ease-in-out;
                }
                .stale-data-active {
                    filter: grayscale(40%) opacity(85%);
                    transition: filter 0.5s ease;
                    position: relative;
                }
                .command-guarded:disabled {
                    cursor: not-allowed !important;
                    opacity: 0.6 !important;
                }
            `;
            document.head.appendChild(style);
        }
    };

    window.SharedState = SharedState;

})(window);
