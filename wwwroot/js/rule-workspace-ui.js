(function () {
    var saveLockPrefix = 'ruleWorkspaceSaveLock:';

    function hasOwn(source, key) {
        return Object.prototype.hasOwnProperty.call(source || {}, key);
    }

    function getCurrentRuleNumber() {
        var match = window.location.pathname.match(/\/Rule(\d+)(?:\/|$)/i);
        return match && match[1] ? match[1] : '';
    }

    function getClientIdFromValue(value) {
        var parsed = parseInt(String(value || '').trim(), 10);
        return isFinite(parsed) && parsed > 0 ? parsed : 0;
    }

    function getCurrentClientId() {
        var clientInput = document.getElementById('clientId');
        if (clientInput) {
            var selectedClientId = getClientIdFromValue(clientInput.value);
            if (selectedClientId > 0) {
                return selectedClientId;
            }
        }

        var query = new URLSearchParams(window.location.search || '');
        return getClientIdFromValue(query.get('clientId'));
    }

    function buildSaveLockKey(ruleNumber, clientId) {
        return saveLockPrefix + String(ruleNumber || '') + ':' + String(clientId || 0);
    }

    function setWorkspaceSaveLocked(ruleNumber, clientId, isLocked) {
        if (!ruleNumber || !clientId || typeof window.localStorage === 'undefined') return;

        var key = buildSaveLockKey(ruleNumber, clientId);
        if (isLocked) {
            window.localStorage.setItem(key, '1');
        } else {
            window.localStorage.removeItem(key);
        }
    }

    function isWorkspaceSaveLocked(ruleNumber, clientId) {
        if (!ruleNumber || !clientId || typeof window.localStorage === 'undefined') return false;
        return window.localStorage.getItem(buildSaveLockKey(ruleNumber, clientId)) === '1';
    }

    function isCurrentWorkspaceSaveLocked() {
        return isWorkspaceSaveLocked(getCurrentRuleNumber(), getCurrentClientId());
    }

    function extractClientIdFromRequestOptions(requestOptions) {
        if (!requestOptions || typeof requestOptions.body !== 'string') return 0;

        try {
            var payload = JSON.parse(requestOptions.body);
            return getClientIdFromValue(payload && payload.clientId);
        } catch (_) {
            return 0;
        }
    }

    function extractClientIdFromResponsePayload(result) {
        if (!result || typeof result !== 'object') return 0;
        if (result.workspace) {
            return getClientIdFromValue(result.workspace.clientId);
        }

        return getClientIdFromValue(result.clientId);
    }

    function refreshWorkspaceSaveLock() {
        var saveButton = document.getElementById('saveWorkspaceBtn');
        if (!saveButton) return;

        var locked = isCurrentWorkspaceSaveLocked();
        saveButton.classList.toggle('is-workspace-save-locked', locked);

        if (locked) {
            saveButton.disabled = true;
            saveButton.title = 'Run the validation again before saving this workspace again.';
        } else if (saveButton.title === 'Run the validation again before saving this workspace again.') {
            saveButton.title = '';
        }
    }

    function syncWorkspaceSaveLock(url, method, requestOptions, result) {
        var normalizedUrl = String(url || '').toLowerCase();
        var normalizedMethod = String(method || 'GET').toUpperCase();
        if (normalizedMethod !== 'POST') return;

        var ruleMatch = normalizedUrl.match(/\/rule(\d+)\//);
        var ruleNumber = ruleMatch && ruleMatch[1] ? ruleMatch[1] : getCurrentRuleNumber();
        var clientId = extractClientIdFromResponsePayload(result) || extractClientIdFromRequestOptions(requestOptions) || getCurrentClientId();
        if (!ruleNumber || !clientId) return;

        if (normalizedUrl.indexOf('/saveworkspace') >= 0 && result && result.success) {
            setWorkspaceSaveLocked(ruleNumber, clientId, true);
            refreshWorkspaceSaveLock();
            return;
        }

        if (normalizedUrl.indexOf('/runvalidation') >= 0 && result && result.success) {
            setWorkspaceSaveLocked(ruleNumber, clientId, false);
            refreshWorkspaceSaveLock();
        }
    }

    function resolveElement(target) {
        if (!target) return null;
        if (typeof target === 'string') {
            return document.getElementById(target) || document.querySelector(target);
        }
        return target;
    }

    function setFormSectionReadonly(target, isReadonly) {
        var root = resolveElement(target);
        if (!root) return null;

        root.classList.toggle('module-readonly-section', !!isReadonly);

        root.querySelectorAll('input, select, textarea, button').forEach(function (element) {
            if (element.type === 'hidden') return;

            if (isReadonly) {
                if (!hasOwn(element.dataset, 'ruleUiPrevDisabled')) {
                    element.dataset.ruleUiPrevDisabled = element.disabled ? '1' : '0';
                }

                if ('readOnly' in element && !hasOwn(element.dataset, 'ruleUiPrevReadonly')) {
                    element.dataset.ruleUiPrevReadonly = element.readOnly ? '1' : '0';
                }

                if ('disabled' in element) {
                    element.disabled = true;
                } else if ('readOnly' in element) {
                    element.readOnly = true;
                }

                return;
            }

            if (hasOwn(element.dataset, 'ruleUiPrevDisabled') && 'disabled' in element) {
                element.disabled = element.dataset.ruleUiPrevDisabled === '1';
                delete element.dataset.ruleUiPrevDisabled;
            }

            if (hasOwn(element.dataset, 'ruleUiPrevReadonly') && 'readOnly' in element) {
                element.readOnly = element.dataset.ruleUiPrevReadonly === '1';
                delete element.dataset.ruleUiPrevReadonly;
            }
        });

        return root;
    }

    function setReadonlyShell(target, isReadonly) {
        var element = resolveElement(target);
        if (!element) return null;

        element.classList.toggle('module-readonly', !!isReadonly);
        return element;
    }

    function matchesAllowSelector(element, selectors) {
        return (selectors || []).some(function (selector) {
            return !!selector && element.matches(selector);
        });
    }

    function setButtonGroupReadonly(targets, isReadonly, options) {
        var allow = options && Array.isArray(options.allow) ? options.allow : [];
        var groups = Array.isArray(targets) ? targets : [targets];

        groups.forEach(function (target) {
            var root = resolveElement(target);
            if (!root) return;

            root.querySelectorAll('button').forEach(function (element) {
                if (matchesAllowSelector(element, allow)) {
                    if (!isReadonly && hasOwn(element.dataset, 'ruleUiLockPrevDisabled')) {
                        element.disabled = element.dataset.ruleUiLockPrevDisabled === '1';
                        delete element.dataset.ruleUiLockPrevDisabled;
                    }

                    return;
                }

                if (isReadonly) {
                    if (!hasOwn(element.dataset, 'ruleUiLockPrevDisabled')) {
                        element.dataset.ruleUiLockPrevDisabled = element.disabled ? '1' : '0';
                    }

                    element.disabled = true;
                    return;
                }

                if (hasOwn(element.dataset, 'ruleUiLockPrevDisabled')) {
                    element.disabled = element.dataset.ruleUiLockPrevDisabled === '1';
                    delete element.dataset.ruleUiLockPrevDisabled;
                }
            });
        });
    }

    function applyState(config) {
        var element = resolveElement(config && (config.target || config.element || config.id));
        if (!element) return null;

        if (hasOwn(config, 'show')) {
            var displayValue = hasOwn(config, 'display') ? config.display : 'inline-flex';
            element.style.display = config.show ? displayValue : 'none';
        }

        if (hasOwn(config, 'disabled')) {
            element.disabled = !!config.disabled;
        }

        if (element.id === 'saveWorkspaceBtn' && isCurrentWorkspaceSaveLocked()) {
            element.disabled = true;
            if (!element.title) {
                element.title = 'Run the validation again before saving this workspace again.';
            }
            element.classList.add('is-workspace-save-locked');
        } else if (element.id === 'saveWorkspaceBtn') {
            element.classList.remove('is-workspace-save-locked');
            if (element.title === 'Run the validation again before saving this workspace again.') {
                element.title = '';
            }
        }

        if (hasOwn(config, 'text') && config.text != null) {
            element.textContent = config.text;
        }

        if (hasOwn(config, 'title')) {
            element.title = config.title || '';
        }

        return element;
    }

    function applyStates(configs) {
        (configs || []).forEach(applyState);
    }

    window.ruleWorkspaceUi = {
        resolveElement: resolveElement,
        setReadonlyShell: setReadonlyShell,
        setFormSectionReadonly: setFormSectionReadonly,
        setButtonGroupReadonly: setButtonGroupReadonly,
        applyState: applyState,
        applyStates: applyStates,
        refreshWorkspaceSaveLock: refreshWorkspaceSaveLock,
        syncWorkspaceSaveLock: syncWorkspaceSaveLock
    };

    document.addEventListener('change', function (event) {
        if (event.target && event.target.id === 'clientId') {
            refreshWorkspaceSaveLock();
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', refreshWorkspaceSaveLock);
    } else {
        refreshWorkspaceSaveLock();
    }
})();
