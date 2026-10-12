define(['baseView', 'loading', 'dialogHelper', 'toast', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-select'], function (BaseView, loading, dialogHelper, toast) {
    'use strict';

    const pluginId = "e8c3b1b3-4f56-4f38-a28a-2e6c5a043007";
    const newUserId = 'new_user_temp_id';

    function getPluginConfiguration() {
        return ApiClient.getPluginConfiguration(pluginId);
    }

    function updatePluginConfiguration(config) {
        return ApiClient.updatePluginConfiguration(pluginId, config);
    }

    function getLimitedUsersStatus() {
        return ApiClient.getJSON(ApiClient.getUrl("HallMonitor/LimitedUsersStatus"));
    }


    function getLogEvents() {
        return ApiClient.getJSON(ApiClient.getUrl("HallMonitor/GetLogEvents"));
    }

    function clearTimeOut(userId) {
        return ApiClient.ajax({
            type: "POST",
            url: ApiClient.getUrl("HallMonitor/ClearTimeOut"),
            data: JSON.stringify({ UserId: userId }),
            contentType: 'application/json'
        });
    }

    function clearLogs() {
        return ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("HallMonitor/ClearLogs") });
    }



    function renderLogs(view) {
        const container = view.querySelector('#logContainer');
        getLogEvents().then(events => {
            if (events.length === 0) {
                container.innerHTML = '<p>The event log is currently empty.</p>';
                return;
            }

            const html = events.map(entry => {
                const eventDate = new Date(entry.Timestamp).toLocaleString();
                const icon = 'timer_off';

                return `
                <div class="listItem" style="display:flex; align-items: center; padding: 0.8em 0;">
                     <i class="md-icon" style="color:#52B54B; margin-right: 1em;">${icon}</i>
                     <div class="listItemBody">
                         <h3 class="listItemTitle">${esc(entry.EventType)} - ${esc(entry.Username || 'Unknown User')}</h3>
                         <div class="listItemText">${esc(entry.Message)}</div>
                         <div class="listItemText secondary">${eventDate} on ${esc(entry.ClientName || 'Unknown Client')}</div>
                     </div>
                </div>
            `;
            }).join('');
            container.innerHTML = html;
        }).catch(err => {
            console.error('Error getting log events:', err);
            container.innerHTML = '<p style="color: #f44336;">Unable to retrieve log events. Please check server logs.</p>';
        });
    }

    function formatTime(hoursDouble) {
        if (hoursDouble === null || typeof hoursDouble === 'undefined') return '';
        const hours = Math.floor(hoursDouble);
        const minutes = Math.round((hoursDouble % 1) * 60);
        return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`;
    }

    function esc(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function generateApiKey() {
        const bytes = new Uint8Array(32);
        window.crypto.getRandomValues(bytes);
        return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
    }


    return class extends BaseView {
        constructor(view, params) {
            super(view, params);

            this.config = {};
            this.watchStatusInterval = null;
            this.editingUserId = null;
            this.allUsers = [];

            view.querySelector('.hallMonitorForm').addEventListener('submit', (e) => {
                e.preventDefault();
                if (this.editingUserId) {
                    toast({ type: 'error', text: 'Please save or cancel the user you are currently editing.' });
                    return;
                }
                const publicApiOn = view.querySelector('#chkEnablePublicApi').checked;
                const apiKeyText = (view.querySelector('#txtApiKey').value || '').trim();
                if (publicApiOn && apiKeyText.length > 0 && apiKeyText.length < 24) {
                    toast({ type: 'error', text: 'The API key must be at least 24 characters when the Public API is enabled. Click Generate New Key.' });
                    return;
                }
                this.saveData(view);
                return false;
            });

            view.querySelector('.localnav').addEventListener('click', (e) => {
                e.preventDefault();
                const target = e.target.closest('.nav-button');
                if (target) {
                    const targetId = target.getAttribute('data-target');
                    view.querySelectorAll('.localnav .nav-button').forEach(b => b.classList.remove('ui-btn-active'));
                    target.classList.add('ui-btn-active');

                    view.querySelectorAll('#limiterPage, #loggingPage, #remoteAccessPage').forEach(page => {
                        page.classList.toggle('hide', page.id !== targetId);
                    });

                    if (targetId === 'loggingPage') {
                        renderLogs(view);
                    }
                }
            });

            view.querySelector('#btnAddLimitedUser').addEventListener('click', () => {
                if (this.editingUserId) return;
                this.editingUserId = newUserId;
                this.renderLimitedUsers(this.view, this.config);
            });

            view.querySelector('#btnGenerateApiKey').addEventListener('click', () => {
                this.view.querySelector('#txtApiKey').value = generateApiKey();
                toast('New API Key generated. Click Save to apply.');
            });

            view.querySelector('#btnClearApiKey').addEventListener('click', () => {
                this.view.querySelector('#txtApiKey').value = '';
                toast('API key cleared. Click Save to apply.');
            });

            view.querySelector('#btnResetAll').addEventListener('click', () => {
                ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("HallMonitor/ResetAllUsersTime") }).then(() => {
                    toast('Reset time for all users.');
                    this.renderLimitedUsers(this.view, this.config);
                }).catch(err => {
                    console.error('Error resetting all users time:', err);
                    toast({ type: 'error', text: 'Error resetting time for all users.' });
                });
            });

            view.querySelector('#btnClearLog').addEventListener('click', () => {
                clearLogs().then(() => {
                    toast('Event log has been cleared.');
                    renderLogs(view);
                }).catch(err => {
                    console.error('Error clearing logs:', err);
                    toast({ type: 'error', text: 'Error clearing event log.' });
                });
            });

            view.querySelector('#limitedUsersContainer').addEventListener('click', (e) => {
                const buttonTarget = e.target.closest('button');
                if (!buttonTarget) return;

                const userId = buttonTarget.getAttribute('data-userid');

                if (buttonTarget.classList.contains('user-editor-tab-button')) {
                    e.preventDefault();
                    const tabId = buttonTarget.getAttribute('data-tab-id');
                    const editor = buttonTarget.closest('.user-editor');
                    editor.querySelectorAll('.user-editor-tab-button').forEach(btn => btn.classList.remove('is-active'));
                    buttonTarget.classList.add('is-active');
                    editor.querySelectorAll('.user-editor-tab-content').forEach(content => content.classList.toggle('hide', content.id !== tabId));
                    return;
                }

                if (buttonTarget.classList.contains('btnExtendPeriod')) {
                    const period = buttonTarget.getAttribute('data-period');
                    const input = buttonTarget.previousElementSibling;
                    const minutes = parseInt(input ? input.value : 0);
                    if (!minutes || minutes <= 0) {
                        toast({ type: 'error', text: 'Please enter a valid number of minutes to extend.' });
                        return;
                    }
                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("HallMonitor/ExtendPeriodTime"),
                        data: JSON.stringify({ UserId: userId, Period: period, Minutes: minutes }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`${period.charAt(0).toUpperCase() + period.slice(1)} time extended.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error extending time.' }));
                    return;
                }

                if (buttonTarget.classList.contains('btnResetPeriod')) {
                    const period = buttonTarget.getAttribute('data-period');
                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("HallMonitor/ResetPeriodTime"),
                        data: JSON.stringify({ UserId: userId, Period: period }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`${period.charAt(0).toUpperCase() + period.slice(1)} time reset.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error resetting time.' }));
                    return;
                }

                if (buttonTarget.classList.contains('btnTimeOutUser')) {
                    const input = this.view.querySelector(`.timeOutMinutes[data-userid="${userId}"]`);
                    const minutes = parseInt(input.value);

                    if (!minutes || minutes <= 0) {
                        toast({ type: 'error', text: 'Please enter a valid number of minutes for the time-out.' });
                        return;
                    }

                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("HallMonitor/TimeOutUser"),
                        data: JSON.stringify({ UserId: userId, Minutes: minutes }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`User placed in time-out.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Could not place the user in time-out (the limiter must be enabled and the user enabled under Limited Users).' }));
                    return;
                }

                if (buttonTarget.classList.contains('btnClearTimeOut')) {
                    clearTimeOut(userId).then(() => {
                        toast('Time-out cleared for user.');
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error clearing time-out.' }));
                    return;
                }

                if (!userId) return;

                if (buttonTarget.classList.contains('btnEditUser')) {
                    if (this.editingUserId) return;
                    this.editingUserId = userId;
                    this.renderLimitedUsers(this.view, this.config);
                    return;
                }

                if (buttonTarget.classList.contains('btn-cancel-edit-user')) {
                    this.editingUserId = null;
                    this.renderLimitedUsers(this.view, this.config);
                    return;
                }

                if (buttonTarget.classList.contains('btn-save-user-inline')) {
                    this.saveUserInline(buttonTarget, userId);
                    return;
                }

                if (this.editingUserId) {
                    toast({ type: 'error', text: 'Please save or cancel the user you are currently editing before performing other actions.' });
                    return;
                }

                if (buttonTarget.classList.contains('btnRemoveUser')) {
                    const user = this.config.LimitedUsers.find(u => u.UserId === userId);
                    if (user) {
                        this.config.LimitedUsers = this.config.LimitedUsers.filter(u => u.UserId !== userId);
                        ApiClient.ajax({
                            type: 'POST',
                            url: ApiClient.getUrl('HallMonitor/DeleteUserData'),
                            data: JSON.stringify({ UserId: userId }),
                            contentType: 'application/json'
                        }).catch(err => console.error('Error deleting user watch data:', err));
                        toast(`User ${user.Username} removed. Please click Save to apply changes.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }
                    return;
                }

                if (buttonTarget.classList.contains('btnExtendTime')) {
                    const input = this.view.querySelector(`.extendTimeMinutes[data-userid="${userId}"]`);
                    const minutes = parseInt(input.value);

                    if (!minutes || minutes <= 0) {
                        toast({ type: 'error', text: 'Please enter a valid number of minutes to extend.' });
                        return;
                    }

                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("HallMonitor/ExtendTime"),
                        data: JSON.stringify({ UserId: userId, Minutes: minutes }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`Time extended for user.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error extending time.' }));
                    return;
                }

                if (buttonTarget.classList.contains('btnToggleUserLimit')) {
                    const user = this.config.LimitedUsers.find(u => u.UserId === userId);
                    if (user) {
                        user.IsEnabled = !user.IsEnabled;
                        toast(`Limit for ${user.Username} has been ${user.IsEnabled ? 'enabled' : 'disabled'}. Remember to save your changes.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }
                    return;
                }

                if (buttonTarget.classList.contains('btnResetUser')) {
                    ApiClient.ajax({
                        type: "POST",
                        url: ApiClient.getUrl("HallMonitor/ResetUserTime"),
                        data: JSON.stringify({ UserId: userId }),
                        contentType: 'application/json'
                    }).then(() => {
                        toast(`Time reset for user.`);
                        this.renderLimitedUsers(this.view, this.config);
                    }).catch(() => toast({ type: 'error', text: 'Error resetting time.' }));
                }
            });
        }

        getLimitedUserDisplayHtml(user, status) {
            const isEnabled = user.IsEnabled !== false;
            const disabledClass = isEnabled ? '' : 'disabled-item';
            const statusText = isEnabled ? '' : ' (Disabled)';
            const toggleTitle = isEnabled ? 'Disable Limit' : 'Enable Limit';
            const toggleIcon = isEnabled ? 'power_settings_new' : 'block';

            const isTimedOut = status && new Date(status.TimeOutUntil) > new Date();
            let timeOutHtml = '';
            if (isTimedOut) {
                const timeOutUntilString = new Date(status.TimeOutUntil).toLocaleTimeString();
                timeOutHtml = `<div class="listItemText secondary" style="color: #f44336;">Timed Out until ${timeOutUntilString}</div>`;
            }

            let remainingTimeText = 'No active limits.';
            let periodRowsHtml = '';
            {
                const formatHoursMinutes = (seconds) => {
                    const totalMinutes = Math.floor(seconds / 60);
                    const hours = Math.floor(totalMinutes / 60);
                    const minutes = totalMinutes % 60;
                    return `${hours}:${String(minutes).padStart(2, '0')}`;
                };

                const periodRows = [];

                if (user.EnableDailyLimit || user.EnablePerDayLimits) {
                    const pdDayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
                    const today = new Date().getDay();
                    let effectiveDailyLimitMinutes = user.EnableDailyLimit ? (user.DailyLimitMinutes || 0) : 0;
                    let usedPerDayOverride = false;
                    if (user.EnablePerDayLimits && user.PerDayLimits && user.PerDayLimits.length > 0) {
                        const todayOverride = user.PerDayLimits.find(o => {
                            const d = typeof o.Day === 'string' ? pdDayNames.indexOf(o.Day) : o.Day;
                            return d === today && o.IsEnabled;
                        });
                        if (todayOverride) {
                            effectiveDailyLimitMinutes = todayOverride.LimitMinutes;
                            usedPerDayOverride = true;
                        }
                    }
                    if (effectiveDailyLimitMinutes > 0) {
                        const watched = formatHoursMinutes(status ? status.SecondsWatchedDaily : 0);
                        const limit = formatHoursMinutes(effectiveDailyLimitMinutes * 60 + (status ? status.CreditSecondsDaily : 0));
                        const label = usedPerDayOverride ? "Today's time limit used" : "Daily time limit used";
                        periodRows.push({ label, value: `${watched}/${limit}`, period: 'daily' });
                    }
                }
                if (user.EnableWeeklyLimit && user.WeeklyLimitHours > 0) {
                    const watched = formatHoursMinutes(status ? status.SecondsWatchedWeekly : 0);
                    const limit = formatHoursMinutes(user.WeeklyLimitHours * 3600 + (status ? status.CreditSecondsWeekly : 0));
                    periodRows.push({ label: 'Weekly time limit used', value: `${watched}/${limit}`, period: 'weekly' });
                }
                if (user.EnableMonthlyLimit && user.MonthlyLimitHours > 0) {
                    const watched = formatHoursMinutes(status ? status.SecondsWatchedMonthly : 0);
                    const limit = formatHoursMinutes(user.MonthlyLimitHours * 3600 + (status ? status.CreditSecondsMonthly : 0));
                    periodRows.push({ label: 'Monthly time limit used', value: `${watched}/${limit}`, period: 'monthly' });
                }
                if (user.EnableYearlyLimit && user.YearlyLimitHours > 0) {
                    const watched = formatHoursMinutes(status ? status.SecondsWatchedYearly : 0);
                    const limit = formatHoursMinutes(user.YearlyLimitHours * 3600 + (status ? status.CreditSecondsYearly : 0));
                    periodRows.push({ label: 'Yearly time limit used', value: `${watched}/${limit}`, period: 'yearly' });
                }

                if (periodRows.length > 0) {
                    remainingTimeText = periodRows.map(r => `${r.label}: ${r.value}`).join(' | ');
                    periodRowsHtml = periodRows.map(r => `
                        <div style="display: flex; align-items: center; gap: 0.4em; margin-top: 0.4em; flex-wrap: wrap;">
                            <span class="listItemText" style="min-width: 220px;">${r.label}: <strong>${r.value}</strong></span>
                            <input is="emby-input" type="number" class="extendPeriodMinutes" placeholder="Mins" value="30" style="width: 70px;" data-userid="${esc(user.UserId)}" data-period="${r.period}" />
                            <button is="emby-button" type="button" class="raised mini btnExtendPeriod" data-userid="${esc(user.UserId)}" data-period="${r.period}"><span>Extend</span></button>
                            <button is="emby-button" type="button" class="raised mini button-cancel btnResetPeriod" data-userid="${esc(user.UserId)}" data-period="${r.period}"><span>Reset</span></button>
                        </div>`).join('');
                }
            }

            let timeWindowText = '';
            if (user.EnableTimeWindows && user.TimeWindows && user.TimeWindows.length > 0) {
                const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
                const activeWindows = user.TimeWindows
                    .filter(w => w.IsEnabled && w.Day >= 0 && w.Day < 7)
                    .map(w => `${days[w.Day]}: ${formatTime(w.StartHour)}-${formatTime(w.EndHour)}`)
                    .join('; ');
                if (activeWindows) {
                    timeWindowText = `Plays: ${activeWindows}`;
                }
            }

            let watchedHtml = '';
            if (status && status.ApiLevel >= 3) {
                const hm = (s) => { const m = Math.floor((s || 0) / 60); return `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}`; };
                const since = status.WatchedTrackingSince ? ` (counted since ${esc(status.WatchedTrackingSince)})` : '';
                watchedHtml = `<div class="listItemText secondary">Watched: today ${hm(status.WatchedTodaySeconds)} &middot; this week ${hm(status.WatchedWeekSeconds)} &middot; this month ${hm(status.WatchedMonthSeconds)} &middot; this year ${hm(status.WatchedYearSeconds)}${since}. Resets never change these.</div>`;
            }

            return `
            <div class="listItem ${disabledClass}" style="display:flex; align-items: flex-start; padding: 0.5em 0;">
                <div class="listItemBody" style="flex-grow: 1;">
                    <h3 class="listItemTitle">${esc(user.Username)}${statusText}</h3>
                    ${timeOutHtml}
                    ${periodRowsHtml || `<div class="listItemText secondary">${remainingTimeText}</div>`}
                    ${watchedHtml}
                    <div class="listItemText">${timeWindowText}</div>
                </div>
                <div style="display: flex; align-items: center; gap: 0.5em; margin-left: 1em; flex-wrap: wrap; justify-content: flex-end;">
                    <input is="emby-input" type="number" class="timeOutMinutes" placeholder="Mins" value="15" style="width: 80px;" data-userid="${esc(user.UserId)}" />
                    <button is="emby-button" type="button" class="raised mini button-cancel btnTimeOutUser" data-userid="${esc(user.UserId)}" title="Time-out User">
                        <span>Time-Out</span>
                    </button>
                    ${isTimedOut ? `
                    <button is="emby-button" type="button" class="raised mini button-accent btnClearTimeOut" data-userid="${esc(user.UserId)}" title="Clear Time-Out">
                        <span>Clear TO</span>
                    </button>
                    ` : ''}
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnResetUser" data-userid="${esc(user.UserId)}" title="Fresh day: reset only today's limiter counter">
                        <i class="md-icon">refresh</i>
                    </button>
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnEditUser" data-userid="${esc(user.UserId)}" title="Edit Limit">
                        <i class="md-icon">edit</i>
                    </button>
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnToggleUserLimit" data-userid="${esc(user.UserId)}" title="${toggleTitle}">
                        <i class="md-icon">${toggleIcon}</i>
                    </button>
                    <button is="emby-button" type="button" class="fab mini raised paper-icon-button-light btnRemoveUser" data-userid="${esc(user.UserId)}" title="Remove User">
                        <i class="md-icon">delete</i>
                    </button>
                </div>
            </div>`;
        }

        getLimitedUserEditorHtml(user, isNew = false) {
            const currentLimitedUserIds = new Set(this.config.LimitedUsers.map(u => u.UserId));
            const availableUsers = this.allUsers.filter(u => !currentLimitedUserIds.has(u.Id));
            let userSelectHtml = '';
            if (isNew) {
                if (availableUsers.length > 0) {
                    userSelectHtml = `<select is="emby-select" id="selectUser" class="edit-user-select" label="Select User:" required>${availableUsers.map(u => `<option value="${esc(u.Id)}" data-username="${esc(u.Name)}">${esc(u.Name)}</option>`).join('')}</select>`;
                } else {
                    userSelectHtml = '<p>No more users available to add.</p>';
                }
            } else {
                userSelectHtml = `<input is="emby-input" id="txtUsername" type="text" label="User:" value="${esc(user.Username)}" readonly />`;
            }

            const timeOptions = Array.from({ length: 48 }, (_, i) => `<option value="${i / 2}">${String(Math.floor(i / 2)).padStart(2, '0')}:${String((i % 2) * 30).padStart(2, '0')}</option>`).join('');
            const weekDayOptions = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'].map((day, i) => `<option value="${i}">${day}</option>`).join('');
            const monthDayOptions = Array.from({ length: 28 }, (_, i) => `<option value="${i + 1}">${i + 1}</option>`).join('');
            const monthOptions = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'].map((m, i) => `<option value="${i + 1}">${m}</option>`).join('');
            const yearDayOptions = Array.from({ length: 31 }, (_, i) => `<option value="${i + 1}">${i + 1}</option>`).join('');

            const daysOfWeek = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
            let timeWindowsHtml = daysOfWeek.map((day, index) => `
                <div class="day-window-row" data-day="${index}" style="display: flex; align-items: center; gap: 1em; margin-bottom: 0.8em; flex-wrap: wrap;">
                    <div style="width: 150px; flex-shrink: 0;">
                        <label class="checkboxContainer" style="padding:0;"><input is="emby-checkbox" type="checkbox" class="edit-day-window-enabled" /><span>${day}</span></label>
                    </div>
                    <div style="flex-grow:1; min-width: 150px;"><select is="emby-select" class="edit-day-window-start" label="From:">${timeOptions}</select></div>
                    <div style="flex-grow:1; min-width: 150px;"><select is="emby-select" class="edit-day-window-end" label="To:">${timeOptions}</select></div>
                </div>
            `).join('');

            const perDayLimits = user.PerDayLimits || [];
            let perDayLimitsHtml = daysOfWeek.map((day, index) => {
                const override = perDayLimits.find(o => {
                    const d = typeof o.Day === 'string' ? daysOfWeek.indexOf(o.Day) : o.Day;
                    return d === index;
                });
                const isChecked = override ? (override.IsEnabled || false) : false;
                const limitMins = override ? (override.LimitMinutes || user.DailyLimitMinutes || 120) : (user.DailyLimitMinutes || 120);
                return `
                <div class="day-limit-row" data-day="${index}" style="display: flex; align-items: center; gap: 1em; margin-bottom: 0.8em; flex-wrap: wrap;">
                    <div style="width: 120px; flex-shrink: 0;">
                        <label class="checkboxContainer" style="padding:0;"><input is="emby-checkbox" type="checkbox" class="edit-day-limit-enabled" ${isChecked ? 'checked' : ''} /><span>${day}</span></label>
                    </div>
                    <div style="flex-grow:1; max-width: 200px;">
                        <input is="emby-input" type="number" class="edit-day-limit-minutes" label="Minutes:" value="${limitMins}" />
                    </div>
                </div>
                `;
            }).join('');


            return `
            <div class="user-editor" data-userid="${esc(user.UserId)}">
                <h3>${isNew ? 'Add New Limited User' : 'Editing: ' + esc(user.Username)}</h3>
                 <div class="inputContainer">${userSelectHtml}</div>

                <div class="emby-tabs-slider" style="margin: 1.5em 0 1em;">
                    <div class="emby-tabs">
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button is-active" data-tab-id="tab-limits">Limits</button>
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-schedule">Reset Schedule</button>
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-window">Time Window</button>
                        <button is="emby-button" type="button" class="emby-tab-button user-editor-tab-button" data-tab-id="tab-notifications">Notifications</button>
                    </div>
                </div>

                <!-- Limits Tab -->
                <div id="tab-limits" class="user-editor-tab-content">
                    <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-daily" ${user.EnableDailyLimit ? 'checked' : ''} /><span>Enable Daily Limit</span></label>
                         <input is="emby-input" class="edit-daily-minutes" type="number" label="Daily Limit (Minutes):" value="${user.DailyLimitMinutes || 120}" />
                    </div>
                    <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-weekly" ${user.EnableWeeklyLimit ? 'checked' : ''} /><span>Enable Weekly Limit</span></label>
                         <input is="emby-input" class="edit-weekly-hours" type="number" label="Weekly Limit (Hours):" value="${user.WeeklyLimitHours || 20}" />
                    </div>
                     <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-monthly" ${user.EnableMonthlyLimit ? 'checked' : ''} /><span>Enable Monthly Limit</span></label>
                         <input is="emby-input" class="edit-monthly-hours" type="number" label="Monthly Limit (Hours):" value="${user.MonthlyLimitHours || 80}" />
                    </div>
                     <div class="inputContainer">
                         <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-yearly" ${user.EnableYearlyLimit ? 'checked' : ''} /><span>Enable Yearly Limit</span></label>
                         <input is="emby-input" class="edit-yearly-hours" type="number" label="Yearly Limit (Hours):" value="${user.YearlyLimitHours || 0}" />
                    </div>
                    <div class="inputContainer" style="margin-top: 1em;">
                        <label class="checkboxContainer"><input is="emby-checkbox" type="checkbox" class="edit-enable-per-day-limits" ${user.EnablePerDayLimits ? 'checked' : ''} /><span>Use different limits per day of the week</span></label>
                        <div class="fieldDescription">Set a specific limit for each day. This works independently — you do not need to enable the flat daily limit above. Days without an override checked will use the flat daily limit if that is also enabled, otherwise they are unlimited.</div>
                    </div>
                    <div class="per-day-limits-container" style="margin-top: 1em; padding-top: 1em; border-top: 1px solid #444;">
                        ${perDayLimitsHtml}
                    </div>
                </div>

                <!-- Reset Schedule Tab -->
                <div id="tab-schedule" class="user-editor-tab-content hide">
                    <div class="fieldDescription" style="margin-bottom: 1.5em;">Configure when watch time counters reset. Only enabled limits will reset on their respective schedules.</div>
                    
                    <div class="inputContainer">
                        <select is="emby-select" class="edit-reset-time" label="Reset Time of Day:">${timeOptions}</select>
                        <div class="fieldDescription">All resets occur at this time of day.</div>
                    </div>
                    
                    <hr style="margin: 1.5em 0; border-color: #444;" />
                    
                    <div class="checkboxContainer" style="margin-bottom: 0.5em;">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-daily-reset" ${user.EnableDailyReset !== undefined ? (user.EnableDailyReset ? 'checked' : '') : 'checked'} /><span>Daily Reset</span></label>
                        <div class="fieldDescription">Daily counters reset every day at the specified time.</div>
                    </div>
                    
                    <div class="checkboxContainer" style="margin: 1.5em 0 0.5em 0;">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-weekly-reset" ${user.EnableWeeklyReset ? 'checked' : ''} /><span>Weekly Reset</span></label>
                    </div>
                    <div class="inputContainer" style="margin-left: 2em;">
                        <select is="emby-select" class="edit-weekly-reset-day" label="Reset on:">${weekDayOptions}</select>
                        <div class="fieldDescription">Weekly counters reset on this day of the week.</div>
                    </div>
                    
                    <div class="checkboxContainer" style="margin: 1.5em 0 0.5em 0;">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-monthly-reset" ${user.EnableMonthlyReset ? 'checked' : ''} /><span>Monthly Reset</span></label>
                    </div>
                    <div class="inputContainer" style="margin-left: 2em;">
                        <select is="emby-select" class="edit-monthly-reset-day" label="Reset on day:">${monthDayOptions}</select>
                        <div class="fieldDescription">Monthly counters reset on this day of each month.</div>
                    </div>
                    
                    <div class="checkboxContainer" style="margin: 1.5em 0 0.5em 0;">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-yearly-reset" ${user.EnableYearlyReset ? 'checked' : ''} /><span>Yearly Reset</span></label>
                    </div>
                    <div style="display: flex; gap: 1em; margin-left: 2em;">
                        <div class="inputContainer" style="flex-grow:1;">
                            <select is="emby-select" class="edit-yearly-reset-month" label="Month:">${monthOptions}</select>
                        </div>
                        <div class="inputContainer" style="flex-grow:1;">
                            <select is="emby-select" class="edit-yearly-reset-day" label="Day:">${yearDayOptions}</select>
                        </div>
                    </div>
                    <div class="fieldDescription" style="margin-left: 2em;">Yearly counters reset on this date each year.</div>
                </div>

                <!-- Time Window Tab -->
                <div id="tab-window" class="user-editor-tab-content hide">
                    <div class="checkboxContainer">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-time-windows" ${user.EnableTimeWindows ? 'checked' : ''} /><span>Restrict playback to specific time windows per day</span></label>
                        <div class="fieldDescription">Define allowed playback times for each day of the week. If a day is not enabled, playback will be blocked for that entire day.</div>
                    </div>
                    <div class="time-windows-container" style="margin-top: 1.5em; border-top: 1px solid #444; padding-top: 1.5em;">
                        ${timeWindowsHtml}
                    </div>
                </div>

                <!-- Notifications Tab -->
                <div id="tab-notifications" class="user-editor-tab-content hide">
                     <div class="checkboxContainer">
                        <label><input is="emby-checkbox" type="checkbox" class="edit-enable-threshold-notifications" ${user.EnableThresholdNotifications ? 'checked' : ''} /><span>Enable notifications when nearing a limit</span></label>
                     </div>
                     <div class="inputContainer">
                        <input is="emby-input" class="edit-notification-thresholds" type="text" label="Notification Thresholds (%):" value="${esc(user.NotificationThresholds || '80,95')}" />
                        <div class="fieldDescription">Comma-separated list of percentages (e.g., 50, 80, 95).</div>
                     </div>
                </div>

                <div class="user-editor-buttons">
                   <button is="emby-button" type="button" class="raised button-submit btn-save-user-inline" data-userid="${esc(user.UserId)}"><span>Save User</span></button>
                   <button is="emby-button" type="button" class="raised button-cancel btn-cancel-edit-user" data-userid="${esc(user.UserId)}"><span>Cancel</span></button>
                </div>
            </div>`;
        }

        saveUserInline(saveButton, userId) {
            const editorContainer = saveButton.closest('.user-editor');
            if (!editorContainer) return;

            const userData = {
                EnableDailyLimit: editorContainer.querySelector('.edit-enable-daily').checked,
                DailyLimitMinutes: parseInt(editorContainer.querySelector('.edit-daily-minutes').value) || 0,
                EnableWeeklyLimit: editorContainer.querySelector('.edit-enable-weekly').checked,
                WeeklyLimitHours: parseInt(editorContainer.querySelector('.edit-weekly-hours').value) || 0,
                EnableMonthlyLimit: editorContainer.querySelector('.edit-enable-monthly').checked,
                MonthlyLimitHours: parseInt(editorContainer.querySelector('.edit-monthly-hours').value) || 0,
                EnableYearlyLimit: editorContainer.querySelector('.edit-enable-yearly').checked,
                YearlyLimitHours: parseInt(editorContainer.querySelector('.edit-yearly-hours').value) || 0,

                ResetTimeOfDayHours: parseFloat(editorContainer.querySelector('.edit-reset-time').value) || 3,
                
                EnableDailyReset: editorContainer.querySelector('.edit-enable-daily-reset').checked,
                EnableWeeklyReset: editorContainer.querySelector('.edit-enable-weekly-reset').checked,
                EnableMonthlyReset: editorContainer.querySelector('.edit-enable-monthly-reset').checked,
                EnableYearlyReset: editorContainer.querySelector('.edit-enable-yearly-reset').checked,
                
                WeeklyResetDay: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][parseInt(editorContainer.querySelector('.edit-weekly-reset-day').value) || 0],
                
                MonthlyResetDay: parseInt(editorContainer.querySelector('.edit-monthly-reset-day').value) || 1,
                YearlyResetMonth: parseInt(editorContainer.querySelector('.edit-yearly-reset-month').value) || 1,
                YearlyResetDay: parseInt(editorContainer.querySelector('.edit-yearly-reset-day').value) || 1,

                EnableThresholdNotifications: editorContainer.querySelector('.edit-enable-threshold-notifications').checked,
                NotificationThresholds: editorContainer.querySelector('.edit-notification-thresholds').value,

                EnableTimeWindows: editorContainer.querySelector('.edit-enable-time-windows').checked,
                TimeWindows: [],

                EnablePerDayLimits: editorContainer.querySelector('.edit-enable-per-day-limits').checked,
                PerDayLimits: []
            };

            const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
            editorContainer.querySelectorAll('.day-window-row').forEach(row => {
                const dayIndex = parseInt(row.getAttribute('data-day')) || 0;
                const startHour = parseFloat(row.querySelector('.edit-day-window-start').value);
                const endHour = parseFloat(row.querySelector('.edit-day-window-end').value);
                const dayRule = {
                    Day: dayNames[dayIndex],
                    IsEnabled: row.querySelector('.edit-day-window-enabled').checked,
                    StartHour: isNaN(startHour) ? 7 : startHour,
                    EndHour: isNaN(endHour) ? 20 : endHour
                };
                userData.TimeWindows.push(dayRule);
            });

            editorContainer.querySelectorAll('.day-limit-row').forEach(row => {
                const dayIndex = parseInt(row.getAttribute('data-day')) || 0;
                const limitMins = parseInt(row.querySelector('.edit-day-limit-minutes').value) || 120;
                userData.PerDayLimits.push({
                    Day: dayNames[dayIndex],
                    IsEnabled: row.querySelector('.edit-day-limit-enabled').checked,
                    LimitMinutes: limitMins
                });
            });

            const limitChecks = [
                [userData.EnableDailyLimit, userData.DailyLimitMinutes, 1, 1440, 'Daily limit (minutes)'],
                [userData.EnableWeeklyLimit, userData.WeeklyLimitHours, 1, 168, 'Weekly limit (hours)'],
                [userData.EnableMonthlyLimit, userData.MonthlyLimitHours, 1, 744, 'Monthly limit (hours)'],
                [userData.EnableYearlyLimit, userData.YearlyLimitHours, 1, 8784, 'Yearly limit (hours)']
            ];
            for (const [enabled, value, min, max, label] of limitChecks) {
                if (enabled && (!Number.isFinite(value) || value < min || value > max)) {
                    toast({ type: 'error', text: `${label} must be between ${min} and ${max} when its limit is enabled (0 would mean no limit).` });
                    return;
                }
            }
            if (userData.EnablePerDayLimits && userData.PerDayLimits.some(d => d.IsEnabled && (d.LimitMinutes < 1 || d.LimitMinutes > 1440))) {
                toast({ type: 'error', text: 'Per-day limits must be between 1 and 1440 minutes.' });
                return;
            }

            if (userId === newUserId) {
                const userSelect = editorContainer.querySelector('.edit-user-select');
                if (!userSelect || !userSelect.value) {
                    toast({ type: 'error', text: 'Please select a user.' });
                    return;
                }
                const selectedOption = userSelect.options[userSelect.selectedIndex];
                userData.UserId = selectedOption.value;
                userData.Username = selectedOption.getAttribute('data-username');
                userData.IsEnabled = true;
                this.config.LimitedUsers.push(userData);
                toast(`User ${userData.Username} added. Save all changes to apply.`);
            } else {
                const userToUpdate = this.config.LimitedUsers.find(u => u.UserId === userId);
                if (userToUpdate) {
                    Object.assign(userToUpdate, userData);
                    toast(`User ${userToUpdate.Username} updated. Save all changes to apply.`);
                }
            }

            this.editingUserId = null;
            this.renderLimitedUsers(this.view, this.config);
        }

        renderLimitedUsers(view, config) {
            const container = view.querySelector('#limitedUsersContainer');
            if (!config.LimitedUsers) {
                config.LimitedUsers = [];
            }

            getLimitedUsersStatus().then(userStatuses => {
                const statusMap = new Map(userStatuses.map(s => [s.UserId, s]));

                let listHtml = '';
                const usersToRender = [...config.LimitedUsers];

                if (this.editingUserId === newUserId) {
                    usersToRender.unshift({ 
                        UserId: newUserId, 
                        EnableDailyLimit: true, 
                        DailyLimitMinutes: 120,
                        EnableWeeklyLimit: false,
                        WeeklyLimitHours: 20,
                        EnableMonthlyLimit: false,
                        MonthlyLimitHours: 80,
                        EnableYearlyLimit: false,
                        YearlyLimitHours: 0,
                        EnablePerDayLimits: false,
                        PerDayLimits: [],
                        EnableThresholdNotifications: false,
                        NotificationThresholds: '80,95', 
                        ResetTimeOfDayHours: 3, 
                        EnableDailyReset: true,
                        EnableWeeklyReset: false,
                        WeeklyResetDay: 'Sunday',
                        EnableMonthlyReset: false,
                        MonthlyResetDay: 1, 
                        EnableYearlyReset: false,
                        YearlyResetMonth: 1, 
                        YearlyResetDay: 1, 
                        TimeWindows: [] 
                    });
                }

                if (usersToRender.length === 0) {
                    listHtml = '<p>No users have been added to the watch time limiter.</p>';
                } else {
                    listHtml = usersToRender.map(user => {
                        if (user.UserId === this.editingUserId) {
                            return this.getLimitedUserEditorHtml(user, user.UserId === newUserId);
                        } else {
                            return this.getLimitedUserDisplayHtml(user, statusMap.get(user.UserId));
                        }
                    }).join('<hr style="margin: 1em 0; border-color: #444;" />');
                }

                container.innerHTML = listHtml;

                if (this.editingUserId) {
                    const editor = container.querySelector('.user-editor');
                    if (editor) {
                        const user = config.LimitedUsers.find(u => u.UserId === this.editingUserId) || usersToRender[0];

                        editor.querySelector('.edit-reset-time').value = user.ResetTimeOfDayHours || 3;
                        
                        const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
                        const weeklyDayIndex = typeof user.WeeklyResetDay === 'string'
                            ? dayNames.indexOf(user.WeeklyResetDay)
                            : (user.WeeklyResetDay || 0);
                        editor.querySelector('.edit-weekly-reset-day').value = weeklyDayIndex >= 0 ? weeklyDayIndex : 0;
                        
                        editor.querySelector('.edit-monthly-reset-day').value = user.MonthlyResetDay || 1;
                        editor.querySelector('.edit-yearly-reset-month').value = user.YearlyResetMonth || 1;
                        editor.querySelector('.edit-yearly-reset-day').value = user.YearlyResetDay || 1;
                        
                        const weeklyResetCheckbox = editor.querySelector('.edit-enable-weekly-reset');
                        const monthlyResetCheckbox = editor.querySelector('.edit-enable-monthly-reset');
                        const yearlyResetCheckbox = editor.querySelector('.edit-enable-yearly-reset');
                        const weeklyResetDay = editor.querySelector('.edit-weekly-reset-day');
                        const monthlyResetDay = editor.querySelector('.edit-monthly-reset-day');
                        const yearlyResetMonth = editor.querySelector('.edit-yearly-reset-month');
                        const yearlyResetDay = editor.querySelector('.edit-yearly-reset-day');
                        
                        weeklyResetDay.disabled = !weeklyResetCheckbox.checked;
                        monthlyResetDay.disabled = !monthlyResetCheckbox.checked;
                        yearlyResetMonth.disabled = !yearlyResetCheckbox.checked;
                        yearlyResetDay.disabled = !yearlyResetCheckbox.checked;
                        
                        function updateResetControlStates() {
                            weeklyResetDay.disabled = !weeklyResetCheckbox.checked;
                            monthlyResetDay.disabled = !monthlyResetCheckbox.checked;
                            yearlyResetMonth.disabled = !yearlyResetCheckbox.checked;
                            yearlyResetDay.disabled = !yearlyResetCheckbox.checked;
                        }
                        
                        weeklyResetCheckbox.addEventListener('change', updateResetControlStates);
                        monthlyResetCheckbox.addEventListener('change', updateResetControlStates);
                        yearlyResetCheckbox.addEventListener('change', updateResetControlStates);

                        if (!user.TimeWindows) user.TimeWindows = [];

                        const timeWindowsContainer = editor.querySelector('.time-windows-container');
                        if (timeWindowsContainer) {
                            editor.querySelectorAll('.day-window-row').forEach(row => {
                                const dayIndex = parseInt(row.getAttribute('data-day'));
                                const dayName = dayNames[dayIndex];
                                const dayRule = user.TimeWindows.find(w => {
                                    const d = typeof w.Day === 'string' ? w.Day : dayNames[w.Day];
                                    return d === dayName;
                                });

                                if (dayRule) {
                                    row.querySelector('.edit-day-window-enabled').checked = dayRule.IsEnabled || false;
                                    row.querySelector('.edit-day-window-start').value = (dayRule.StartHour !== undefined && dayRule.StartHour !== null && !isNaN(dayRule.StartHour)) ? dayRule.StartHour : 7;
                                    row.querySelector('.edit-day-window-end').value = (dayRule.EndHour !== undefined && dayRule.EndHour !== null && !isNaN(dayRule.EndHour)) ? dayRule.EndHour : 20;
                                } else {
                                    row.querySelector('.edit-day-window-enabled').checked = false;
                                    row.querySelector('.edit-day-window-start').value = 7;
                                    row.querySelector('.edit-day-window-end').value = 20;
                                }
                            });
                        }

                        const mainToggle = editor.querySelector('.edit-enable-time-windows');
                        const updateVisibility = () => {
                            timeWindowsContainer.style.display = mainToggle.checked ? '' : 'none';
                        };
                        mainToggle.addEventListener('change', updateVisibility);
                        updateVisibility();

                        const perDayToggle = editor.querySelector('.edit-enable-per-day-limits');
                        const perDayContainer = editor.querySelector('.per-day-limits-container');
                        if (perDayToggle && perDayContainer) {
                            const updatePerDayVisibility = () => {
                                perDayContainer.style.display = perDayToggle.checked ? '' : 'none';
                            };
                            perDayToggle.addEventListener('change', updatePerDayVisibility);
                            updatePerDayVisibility();
                        }
                    }
                }
            }).catch(err => {
                console.error('Error getting limited users status:', err);
                container.innerHTML = '<p style="color: #f44336;">Unable to retrieve user status information. Displaying basic user list.</p>';
                if (config.LimitedUsers.length === 0) {
                    container.innerHTML += '<p>No users have been added to the watch time limiter.</p>';
                } else {
                    const basicList = config.LimitedUsers.map(user => `<div class="listItem"><div class="listItemBody"><h3 class="listItemTitle">${esc(user.Username)}</h3></div></div>`).join('');
                    container.innerHTML += basicList;
                }
            });
        }


        loadData(view) {
            loading.show();
            Promise.all([
                getPluginConfiguration(),
                ApiClient.getUsers()
            ]).then(([config, users]) => {
                this.config = config;
                this.allUsers = users;

                view.querySelectorAll('[data-config-key]').forEach(el => {
                    const key = el.getAttribute('data-config-key');
                    const value = config[key];
                    if (el.type === 'checkbox') {
                        el.checked = value;
                    } else if (!Array.isArray(value)) {
                        el.value = value || '';
                    }
                });


                this.editingUserId = null;
                this.renderLimitedUsers(view, this.config);
                loading.hide();
            }).catch(err => {
                loading.hide();
                console.error('Error loading Hall Monitor configuration page:', err);
                toast({
                    type: 'error',
                    text: 'There was an error loading page data. Please try refreshing the page.'
                });
            });
        }

        saveData(view) {
            loading.show();

            view.querySelectorAll('[data-config-key]').forEach(el => {
                const key = el.getAttribute('data-config-key');

                if (!Array.isArray(this.config[key])) {
                    if (el.type === 'checkbox') {
                        this.config[key] = el.checked;
                    } else if (el.type === 'number') {
                        this.config[key] = parseInt(el.value) || 0;
                    }
                    else {
                        this.config[key] = el.value;
                    }
                }
            });




            updatePluginConfiguration(this.config).then(result => {
                loading.hide();
                Dashboard.processPluginConfigurationUpdateResult(result);
                this.loadData(view);
            }).catch(() => {
                loading.hide();
                toast({ type: 'error', text: 'Error saving configuration.' });
            });
        }

        onResume(options) {
            super.onResume(options);
            this.loadData(this.view);
            this.watchStatusInterval = setInterval(() => {
                if (this.config.EnableWatchTimeLimiter && !this.editingUserId && document.querySelector('#limiterPage:not(.hide)')) {
                    this.renderLimitedUsers(this.view, this.config);
                }
            }, 10000);
        }

        onPause() {
            super.onPause();
            if (this.watchStatusInterval) {
                clearInterval(this.watchStatusInterval);
                this.watchStatusInterval = null;
            }
        }

        destroy() {
            if (this.watchStatusInterval) {
                clearInterval(this.watchStatusInterval);
                this.watchStatusInterval = null;
            }
            super.destroy();
        }
    };
});