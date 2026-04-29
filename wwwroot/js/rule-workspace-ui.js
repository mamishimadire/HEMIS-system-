(function () {
    function hasOwn(source, key) {
        return Object.prototype.hasOwnProperty.call(source || {}, key);
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
        applyStates: applyStates
    };
})();
